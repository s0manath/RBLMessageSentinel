using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RBLMessageSentinel
{
    /// <summary>
    /// HTML email to the VF (Infinito) email API, the same request as the old handleClient.sendMailVFAsync():
    /// POST channels.vfEmail.url, Basic auth, JSON { apiver, email { ver, dlr, messages [ subject, from, reply_to, addresses, content ] } }.
    /// Used for rules with "emailTemplate" (see DCMSRule.EmailTemplate).
    /// </summary>
    internal static class VfEmailChannel
    {
        private static readonly ConcurrentDictionary<int, HttpClient> clients = new ConcurrentDictionary<int, HttpClient>();

        public static bool IsEnabled(DCMSConfig cfg)
        {
            return cfg.Channels.VfEmail.Enabled && !string.IsNullOrWhiteSpace(cfg.Channels.VfEmail.Url);
        }

        /// <summary>Sends one email; returns the result line for the alert's log block.</summary>
        public static string Send(DCMSConfig cfg, OutgoingAlert msg)
        {
            VfEmailChannelConfig c = cfg.Channels.VfEmail;
            AlertData a = msg.Alert;
            clsLog objclsLog = new clsLog
            {
                logtype = "VFEmail",
                clNum = a.ClNo,
                trantype = a.Value("TranType", null),
                RespCode = a.Value("RespCode", null),
                ExtendedTranType = a.Value("ExtendedTranType", null),
                MobNo = a.Value("Mobile", null),
                EmailId = msg.VfEmailTo,
                CustId = a.Value("CustId", null),
                sSendMsg = Regex.Replace(msg.VfEmailBody, @"<[^>]*>", "")   // same as old: body without HTML tags
            };

            System.Diagnostics.Stopwatch timer = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var request = new
                {
                    apiver = "1.0",
                    email = new
                    {
                        ver = "1.0",
                        dlr = new { url = "" },
                        messages = new[]
                        {
                            new
                            {
                                addresses = new[] { new { to = new[] { new { emailid = msg.VfEmailTo } } } },
                                subject = c.Subject,
                                content = new[] { new { type = "text/html", value = msg.VfEmailBody } },
                                from = new { emailid = c.FromEmail, name = c.FromName },
                                reply_to = new { emailid = c.ReplyToEmail, name = c.ReplyToName }
                            }
                        }
                    }
                };
                string json = JsonSerializer.Serialize(request);

                using HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, c.Url)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
                req.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(c.User + ":" + c.Password)));
                req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                objclsLog.ReqDatetime = DateTime.Now;
                using HttpResponseMessage resp = Http(c).Send(req);
                using StreamReader reader = new StreamReader(resp.Content.ReadAsStream());
                string body = reader.ReadToEnd();
                objclsLog.ResDatetime = DateTime.Now;
                objclsLog.sResponse = body;

                if (!resp.IsSuccessStatusCode) throw new Exception("HTTP " + (int)resp.StatusCode + " : " + body);

                Log.InsertRBLTransLog(objclsLog);
                return "SENT  HTTP " + (int)resp.StatusCode + "  " + timer.ElapsedMilliseconds + " ms  response: " + OneLine(body);
            }
            catch (Exception ex)
            {
                string error = ex.InnerException == null ? ex.Message : ex.Message + " -> " + ex.InnerException.Message;
                Console.WriteLine(DateTime.Now.ToString() + " >> VF Email Err #" + a.ClNo + " : " + msg.RuleName + " : " + error);
                Log.Writerrorlog(DateTime.Now.ToString() + " >> VF Email Err #" + a.ClNo + " : " + msg.RuleName + " : " + error);
                objclsLog.logtype = "ERROR";
                objclsLog.ErrorMsg = ex.ToString();
                Log.InsertRBLTransErrorLog(objclsLog);
                return "FAILED  " + timer.ElapsedMilliseconds + " ms  error: " + OneLine(error);
            }
        }

        private static string OneLine(string text)
        {
            string t = Regex.Replace(text ?? "", @"\s+", " ").Trim();
            return t.Length > 300 ? t.Substring(0, 300) + " ..." : t;
        }

        private static HttpClient Http(VfEmailChannelConfig c)
        {
            int timeout = c.TimeoutSeconds > 0 ? c.TimeoutSeconds : 30;
            return clients.GetOrAdd(timeout, t => new HttpClient { Timeout = TimeSpan.FromSeconds(t) });
        }
    }
}
