using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml;

namespace RBLMessageSentinel
{
    /// <summary>A message ready to send: rule already matched and {tokens} already filled.</summary>
    internal sealed class OutgoingAlert
    {
        public string RuleName = "";
        public bool Acl = false;            // true = plain SMS via channels.acl, false = <sendMessage> XML
        public string VfEmailTo = "";       // set = email goes via channels.vfEmail (then Email is empty: no <emailID> in the XML)
        public string VfEmailBody = "";
        public string Topic = "";
        public string Mobile = "";
        public string Email = "";
        public string SmsBody = "";
        public List<KeyValuePair<string, string>> MergeVars = new List<KeyValuePair<string, string>>();
        public AlertData Alert = null!;

        // set by Send (for the trace log)
        public string Result = "";          // SENT | FAILED
        public string ResultDetail = "";    // gateway response or error
        public int HttpStatus = 0;
        public long ElapsedMs = 0;
    }

    /// <summary>
    /// The only delivery route: builds the &lt;sendMessage&gt; XML and POSTs it to channels.xml.url,
    /// with the same file and DB logging as before.
    /// </summary>
    internal static class XmlChannel
    {
        private static readonly ConcurrentDictionary<string, HttpClient> clients = new ConcurrentDictionary<string, HttpClient>();

        public static bool IsEnabled(DCMSConfig cfg)
        {
            return cfg.Channels.Xml.Enabled && !string.IsNullOrWhiteSpace(cfg.Channels.Xml.Url);
        }

        public static void Send(DCMSConfig cfg, OutgoingAlert msg)
        {
            XmlChannelConfig c = cfg.Channels.Xml;
            AlertData a = msg.Alert;
            clsLog objclsLog = new clsLog
            {
                logtype = "XML",
                clNum = a.ClNo,
                trantype = a.Value("TranType", null),
                RespCode = a.Value("RespCode", null),
                ExtendedTranType = a.Value("ExtendedTranType", null),
                MobNo = msg.Mobile,
                EmailId = msg.Email,
                CustId = a.Value("CustId", null),
                sSendMsg = msg.Topic + " : " + msg.SmsBody
            };

            System.Diagnostics.Stopwatch timer = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                string xml = BuildXml(c, msg);

                using HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, c.Url)
                {
                    Content = new StringContent(xml, Encoding.UTF8, c.ContentType)
                };
                if (!string.IsNullOrEmpty(c.User))
                {
                    req.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                        Convert.ToBase64String(Encoding.UTF8.GetBytes(c.User + ":" + c.Password)));
                }

                objclsLog.ReqDatetime = DateTime.Now;
                using HttpResponseMessage resp = Http(c).Send(req);
                msg.HttpStatus = (int)resp.StatusCode;
                using StreamReader reader = new StreamReader(resp.Content.ReadAsStream());
                string body = reader.ReadToEnd();
                objclsLog.ResDatetime = DateTime.Now;
                objclsLog.sResponse = body;

                if (!resp.IsSuccessStatusCode) throw new Exception("HTTP " + (int)resp.StatusCode + " : " + body);

                msg.Result = "SENT";
                msg.ResultDetail = body;
                msg.ElapsedMs = timer.ElapsedMilliseconds;
                Log.InsertRBLTransLog(objclsLog);
            }
            catch (Exception ex)
            {
                msg.Result = "FAILED";
                msg.ResultDetail = ErrorText(ex);
                msg.ElapsedMs = timer.ElapsedMilliseconds;
                Console.WriteLine(DateTime.Now.ToString() + " >> XML Err #" + a.ClNo + " : " + msg.RuleName + " : " + ErrorText(ex));
                Log.Writerrorlog(DateTime.Now.ToString() + " >> XML Err #" + a.ClNo + " : " + msg.RuleName + " : " + ErrorText(ex));
                objclsLog.logtype = "ERROR";
                objclsLog.ErrorMsg = ex.ToString();
                Log.InsertRBLTransErrorLog(objclsLog);
            }
        }

        /// <summary>The XML as it is (or would be) sent, with mobile / email / account / customer id masked, for the log.</summary>
        public static string Preview(DCMSConfig cfg, OutgoingAlert msg)
        {
            return Log.MaskXml(BuildXml(cfg.Channels.Xml, msg));
        }

        private static string BuildXml(XmlChannelConfig c, OutgoingAlert msg)
        {
            XmlWriterSettings settings = new XmlWriterSettings { OmitXmlDeclaration = true, Indent = true, IndentChars = "    " };
            StringBuilder sb = new StringBuilder();

            using (XmlWriter w = XmlWriter.Create(sb, settings))
            {
                w.WriteStartElement("sendMessage");
                w.WriteElementString("version", c.Version);
                w.WriteElementString("appID", c.AppId);
                w.WriteElementString("topicName", msg.Topic);

                w.WriteStartElement("recipient");
                w.WriteStartElement("subscriber");
                w.WriteElementString("customerID", c.SendCustomerIdAndAccountNo ? msg.Alert.Value("CustId", null) : "");
                w.WriteElementString("accountNo", c.SendCustomerIdAndAccountNo ? msg.Alert.Value("FromAc", null) : "");
                w.WriteStartElement("contact");
                if (msg.Mobile != "") w.WriteElementString("mobileNo", msg.Mobile);
                if (msg.Email != "") w.WriteElementString("emailID", msg.Email);
                w.WriteEndElement(); // contact
                w.WriteEndElement(); // subscriber
                w.WriteEndElement(); // recipient

                w.WriteStartElement("mergeVarArray");
                foreach (KeyValuePair<string, string> v in msg.MergeVars)
                {
                    w.WriteStartElement("mergeVar");
                    w.WriteElementString("name", v.Key);
                    w.WriteStartElement("content");
                    w.WriteElementString("stringContent", v.Value);
                    w.WriteEndElement(); // content
                    w.WriteEndElement(); // mergeVar
                }
                w.WriteEndElement(); // mergeVarArray

                w.WriteElementString("referenceNo", ReferenceNo(msg.Topic, msg.Alert.ClNo));
                w.WriteEndElement(); // sendMessage
            }

            return sb.ToString();
        }

        // topic_yyyyMMddHHmmssfff + 5 digit message counter
        private static string ReferenceNo(string topic, string clNo)
        {
            long counter;
            long.TryParse(clNo, out counter);
            return topic + "_" + DateTime.Now.ToString("yyyyMMddHHmmssfff") + (counter % 100000).ToString("D5");
        }

        // ---------------------------------------------------------------- HTTP client + certificates

        /// <summary>
        /// Shared HttpClient per timeout + certificates (HttpClient is meant to be reused).
        /// A new one is made when the settings or a certificate file change.
        /// </summary>
        private static HttpClient Http(XmlChannelConfig c)
        {
            int timeout = c.TimeoutSeconds > 0 ? c.TimeoutSeconds : 30;
            string certPath = CertificateFullPath(c.CertificatePath);
            string serverCertPath = CertificateFullPath(c.ServerCertificatePath);
            string key = timeout + "|" + certPath + "|" + c.CertificatePassword + "|" + FileStamp(certPath) + "|" + serverCertPath + "|" + FileStamp(serverCertPath);

            return clients.GetOrAdd(key, _ =>
            {
                HttpClientHandler handler = new HttpClientHandler();

                if (certPath != "")
                {
                    X509Certificate2 cert = LoadClientCertificate(certPath, c.CertificatePassword);
                    handler.ClientCertificateOptions = ClientCertificateOption.Manual;
                    handler.ClientCertificates.Add(cert);
                    Log.Write(DateTime.Now.ToString() + " >> Client certificate loaded : " + certPath + " : " + cert.Subject + " (expires " + cert.NotAfter.ToString("dd-MMM-yyyy") + ")");
                }

                if (serverCertPath != "")
                {
                    if (!File.Exists(serverCertPath)) throw new FileNotFoundException("Server certificate not found", serverCertPath);

                    X509Certificate2 trusted = new X509Certificate2(serverCertPath);
                    handler.ServerCertificateCustomValidationCallback = (request, serverCert, chain, errors) => IsTrustedServer(request.RequestUri?.Host ?? "", serverCert, chain, errors, trusted);
                    Log.Write(DateTime.Now.ToString() + " >> Server certificate trusted : " + serverCertPath + " : " + trusted.Subject + " (expires " + trusted.NotAfter.ToString("dd-MMM-yyyy") + ")");
                }

                return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(timeout) };
            });
        }

        /// <summary>
        /// .pfx / .p12: loaded with its private key.
        /// .cer / .crt: public part only, so the same certificate WITH its private key is taken from the
        /// Windows certificate store (Personal / My of the current user or the local machine), matched by thumbprint.
        /// </summary>
        private static X509Certificate2 LoadClientCertificate(string path, string password)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("Client certificate not found", path);

            X509Certificate2 cert = new X509Certificate2(path, password, X509KeyStorageFlags.MachineKeySet);
            if (cert.HasPrivateKey) return cert;

            foreach (StoreLocation location in new[] { StoreLocation.CurrentUser, StoreLocation.LocalMachine })
            {
                using X509Store store = new X509Store(StoreName.My, location);
                store.Open(OpenFlags.ReadOnly);
                X509Certificate2? installed = store.Certificates
                    .Find(X509FindType.FindByThumbprint, cert.Thumbprint, false)
                    .OfType<X509Certificate2>()
                    .FirstOrDefault(x => x.HasPrivateKey);
                if (installed != null) return installed;
            }

            throw new InvalidOperationException("Client certificate " + path + " has no private key and is not installed with its private key in the Windows certificate store (thumbprint " + cert.Thumbprint + "). Use the .pfx, or install it in Personal certificates.");
        }

        /// <summary>
        /// Server certificate is accepted when Windows already trusts it, or when it is the configured .cer
        /// itself, or it chains up to the configured .cer (CA). The host name must still match.
        /// </summary>
        private static bool IsTrustedServer(string host, X509Certificate2? serverCert, X509Chain? chain, System.Net.Security.SslPolicyErrors errors, X509Certificate2 trusted)
        {
            if (errors == System.Net.Security.SslPolicyErrors.None) return true;
            if (serverCert == null) return Rejected(host, null, chain, errors, trusted, "server sent no certificate");
            if ((errors & System.Net.Security.SslPolicyErrors.RemoteCertificateNameMismatch) != 0)
                return Rejected(host, serverCert, chain, errors, trusted, "host name '" + host + "' is not in the server certificate");
            if (string.Equals(serverCert.Thumbprint, trusted.Thumbprint, StringComparison.OrdinalIgnoreCase)) return true;

            using X509Chain custom = new X509Chain();
            custom.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            custom.ChainPolicy.CustomTrustStore.Add(trusted);
            custom.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            if (chain != null)
            {
                foreach (X509ChainElement e in chain.ChainElements) custom.ChainPolicy.ExtraStore.Add(e.Certificate);
            }
            if (custom.Build(serverCert)) return true;
            return Rejected(host, serverCert, chain, errors, trusted, "server certificate is not the configured .cer and does not chain to it : "
                + string.Join(", ", custom.ChainStatus.Select(s => s.Status + " " + s.StatusInformation.Trim())));
        }

        /// <summary>Writes why the server certificate was rejected (alertLog + Errorlog), then returns false.</summary>
        private static bool Rejected(string host, X509Certificate2? serverCert, X509Chain? chain, System.Net.Security.SslPolicyErrors errors, X509Certificate2 trusted, string reason)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(DateTime.Now.ToString() + " >> SERVER CERT REJECTED : " + host + " : " + reason);
            sb.AppendLine("    sslPolicyErrors : " + errors);
            if (serverCert != null)
            {
                sb.AppendLine("    server cert     : " + serverCert.Subject + " | issuer " + serverCert.Issuer + " | thumbprint " + serverCert.Thumbprint
                    + " | valid " + serverCert.NotBefore.ToString("dd-MMM-yyyy") + " to " + serverCert.NotAfter.ToString("dd-MMM-yyyy"));
                string san = string.Join(", ", serverCert.Extensions.OfType<X509SubjectAlternativeNameExtension>().SelectMany(x => x.EnumerateDnsNames()));
                sb.AppendLine("    server DNS names: " + (san == "" ? "-" : san));
            }
            if (chain != null)
            {
                foreach (X509ChainElement e in chain.ChainElements)
                    sb.AppendLine("    chain           : " + e.Certificate.Subject + " | thumbprint " + e.Certificate.Thumbprint
                        + (e.ChainElementStatus.Length == 0 ? "" : " | " + string.Join(", ", e.ChainElementStatus.Select(s => s.Status.ToString()))));
            }
            sb.Append("    configured .cer : " + trusted.Subject + " | thumbprint " + trusted.Thumbprint + " | expires " + trusted.NotAfter.ToString("dd-MMM-yyyy"));
            Log.Writerrorlog(sb.ToString());
            return false;
        }

        private static string FileStamp(string path)
        {
            if (path == "") return "";
            return File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks.ToString() : "missing";
        }

        private static string CertificateFullPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "";
            path = path.Trim();
            return Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);
        }

        // message plus inner causes, e.g. "The SSL connection could not be established -> The remote certificate is invalid ..."
        private static string ErrorText(Exception ex)
        {
            string text = ex.Message;
            for (Exception? inner = ex.InnerException; inner != null; inner = inner.InnerException)
            {
                if (!text.Contains(inner.Message)) text += " -> " + inner.Message;
            }
            return text;
        }
    }
}
