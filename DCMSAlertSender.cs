using System.Configuration;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;

namespace RBLMessageSentinel
{
    /// <summary>
    /// Alert rule engine. All rules, texts and the XML gateway settings come from Appconfig.json next to the exe
    /// (or the path in appSettings "DCMSConfigFile"); the file is re-read when it changes, no restart needed.
    ///
    /// For each enabled rule whose flag / "when" conditions match, the {tokens} in its texts are filled from the
    /// alert and the &lt;sendMessage&gt; XML is POSTed to channels.xml.url (XmlChannel.cs).
    /// A matched rule clears its clientMessage flag, so later rules on the same flag do not fire.
    /// </summary>
    public static class DCMSAlertSender
    {
        private static readonly object configLock = new object();
        private static volatile DCMSConfig config = new DCMSConfig();
        private static DateTime configWriteTime = DateTime.MinValue;
        private static string configPath = "";
        private static long nextCheckTicks = 0;   // UTC ticks of the next Appconfig.json date check

        private static readonly Regex tokenRegex = new Regex(@"\{([A-Za-z0-9_.\-]+)(?::([^}]+))?\}", RegexOptions.Compiled);

        /// <summary>Sends the XML for every matching rule of the alert.</summary>
        public static void Handle(clientMessage clientMessage)
        {
            if (clientMessage == null || clientMessage.Data == null) return;
            DCMSConfig cfg = LoadConfig();

            AlertData a;
            try
            {
                a = new AlertData(clientMessage);
            }
            catch (Exception ex)
            {
                Log.Writerrorlog(DateTime.Now.ToString() + " >> Alert read Err #" + clientMessage.Counter + " : " + ex.Message);
                return;
            }

            HashSet<string> consumedFlags = new HashSet<string>(StringComparer.Ordinal);
            List<DCMSRule> toSend = new List<DCMSRule>();

            // one block per alert in alertLog (rule, recipients, text, XML, gateway result)
            bool anyRule = false;
            StringBuilder trace = new StringBuilder();
            {
                trace.AppendLine("========== #" + a.ClNo + "  " + DateTime.Now.ToString("dd-MM-yyyy HH:mm:ss.fff"));
                trace.AppendLine("  flags   : " + (a.FlagsSet.Any() ? string.Join(", ", a.FlagsSet) : "(none set by Program.cs)"));
            }

            foreach (DCMSRule rule in cfg.Rules)
            {
                if (!rule.Enabled) continue;
                if (string.IsNullOrWhiteSpace(rule.Flag) && (rule.When == null || rule.When.Count == 0)) continue; // would match everything

                FieldInfo? flagField = null;
                if (!string.IsNullOrWhiteSpace(rule.Flag))
                {
                    flagField = AlertData.FlagField(rule.Flag);
                    if (flagField == null || consumedFlags.Contains(rule.Flag) || !(bool)flagField.GetValue(clientMessage)!) continue;
                }

                if (!Matches(rule, a)) continue;

                if (flagField != null)
                {
                    flagField.SetValue(clientMessage, false);
                    consumedFlags.Add(rule.Flag!);
                }

                anyRule = true;
                if (rule.Drop)
                {
                    trace.AppendLine("  rule    : '" + rule.Name + "' matched -> DROP (nothing sent)");
                    continue;
                }
                trace.AppendLine("  rule    : '" + rule.Name + "' matched");
                toSend.Add(rule);
            }

            if (!anyRule)
            {
                trace.AppendLine("  rule    : no rule matched"
                    + (clientMessage.IntlZero || clientMessage.IntlOne ? " (international I0/I1 is handled by handleClient, see below)" : " - nothing sent"));
            }

            foreach (DCMSRule rule in toSend)
            {
                OutgoingAlert msg = new OutgoingAlert
                {
                    RuleName = rule.Name,
                    Acl = string.Equals(rule.Channel, "acl", StringComparison.OrdinalIgnoreCase),
                    Topic = Fill(rule.Topic ?? "", a),   // topic may use tokens, e.g. "dconuslimitmod{SetLimit:lower}success"
                    Mobile = rule.Mobile ? a.Value("Mobile", null) : "",
                    Email = rule.Email ? a.Value("Email", null) : "",
                    SmsBody = Fill(rule.Sms ?? "", a),
                    Alert = a
                };
                if (!string.IsNullOrEmpty(rule.Sms)) msg.MergeVars.Add(new KeyValuePair<string, string>("SMSBODY", msg.SmsBody));
                if (rule.MergeVars != null)
                {
                    foreach (KeyValuePair<string, string> v in rule.MergeVars)
                        msg.MergeVars.Add(new KeyValuePair<string, string>(v.Key, Fill(v.Value ?? "", a)));
                }

                // email like the old app: own HTML to VF, and no <emailID> in the XML (no second email from DCMS)
                string? html;
                if (msg.Email != "" && !string.IsNullOrWhiteSpace(rule.EmailTemplate) && VfEmailChannel.IsEnabled(cfg)
                    && cfg.EmailTemplates.TryGetValue(rule.EmailTemplate, out html) && !string.IsNullOrWhiteSpace(html))
                {
                    msg.VfEmailTo = msg.Email;
                    msg.VfEmailBody = Fill(html, a);
                    msg.Email = "";
                }
                Send(cfg, msg, rule, trace);
            }

            Log.Write(trace.ToString());
        }

        /// <summary>Raw text from "oldApp" in Appconfig.json, or the same App.config key when missing/empty there.</summary>
        public static string SmsText(string key)
        {
            string? text;
            DCMSConfig cfg = LoadConfig();
            if (cfg.OldApp != null && cfg.OldApp.TryGetValue(key, out text) && !string.IsNullOrWhiteSpace(text)) return text;

            return ConfigurationManager.AppSettings[key] ?? "";
        }

        /// <summary>
        /// International I0 / I1 (text built by handleClient): plain SMS via ACL like the old sendSMS(), or as XML
        /// when "oldApp" has a topic "&lt;key&gt;Topic", e.g. "INTLAdvertisementI0Topic".
        /// </summary>
        public static void SendInternational(clientMessage m, string textKey, string text)
        {
            DCMSConfig cfg = LoadConfig();
            AlertData a = new AlertData(m);
            string? topic;
            cfg.OldApp.TryGetValue(textKey + "Topic", out topic);

            OutgoingAlert msg = new OutgoingAlert
            {
                RuleName = textKey,
                Acl = string.IsNullOrWhiteSpace(topic),   // like the old app: no DCMS topic = plain SMS via ACL
                Topic = topic ?? "",
                Mobile = a.Value("Mobile", null),
                SmsBody = text,
                Alert = a
            };
            msg.MergeVars.Add(new KeyValuePair<string, string>("SMSBODY", text));

            StringBuilder trace = new StringBuilder();
            trace.AppendLine("========== #" + a.ClNo + "  " + DateTime.Now.ToString("dd-MM-yyyy HH:mm:ss.fff") + "  international (handleClient)");
            trace.AppendLine("  rule    : '" + textKey + "'");
            Send(cfg, msg, null, trace);
            Log.Write(trace.ToString());
        }

        // ---------------------------------------------------------------- send

        /// <summary>Sends the SMS (XML or ACL) and, for rules with "emailTemplate", the VF email; adds what happened to the alert's log block.</summary>
        private static void Send(DCMSConfig cfg, OutgoingAlert msg, DCMSRule? rule, StringBuilder trace)
        {
            SendSms(cfg, msg, trace);
            if (msg.VfEmailTo != "")
            {
                trace.AppendLine("    email   : VF to " + Log.Mask(msg.VfEmailTo) + " (template '" + rule?.EmailTemplate + "')");
                trace.AppendLine("    Email Response : " + VfEmailChannel.Send(cfg, msg));
            }
        }

        /// <summary>Checks and sends the SMS part (XML, or plain SMS for "channel": "acl").</summary>
        private static void SendSms(DCMSConfig cfg, OutgoingAlert msg, StringBuilder trace)
        {
            AlertData a = msg.Alert;

            if (msg.Acl)
            {
                trace.AppendLine("    channel : ACL SMS");
                trace.AppendLine("    to      : mobile=" + Show(Log.Mask(msg.Mobile)));
                trace.AppendLine("    text    : " + Show(MaskCustomer(OneLine(msg.SmsBody), a)));

                string aclNotSent = "";
                if (msg.Mobile == "") aclNotSent = "DROPPED - mobile no is blank";
                else if (msg.SmsBody == "") aclNotSent = "NOT SENT - no sms text";
                else if (!AclChannel.IsEnabled(cfg)) aclNotSent = "NOT SENT - channels.acl is disabled or has no url in Appconfig.json";
                if (aclNotSent != "")
                {
                    trace.AppendLine("    result  : " + aclNotSent);
                    return;
                }
                AclChannel.Send(cfg, msg);
            }
            else
            {
                trace.AppendLine("    topic   : " + Show(msg.Topic));
                trace.AppendLine("    to      : mobile=" + Show(Log.Mask(msg.Mobile)) + "  email=" + Show(Log.Mask(msg.Email)));
                trace.AppendLine("    text    : " + Show(MaskCustomer(OneLine(msg.SmsBody), a)));

                string notSent = "";
                if (msg.Mobile == "" && msg.Email == "" && msg.VfEmailTo != "") notSent = "XML not needed - no mobile, the email goes via VF";
                else if (msg.Mobile == "" && msg.Email == "") notSent = "DROPPED - mobile no / email id is blank";
                else if (string.IsNullOrWhiteSpace(msg.Topic)) notSent = "NOT SENT - no topic (topicName) for the XML";
                else if (!XmlChannel.IsEnabled(cfg)) notSent = "NOT SENT - channels.xml is disabled or has no url in Appconfig.json";

                if (cfg.TraceLog && msg.Topic != "")
                {
                    trace.AppendLine("    XML     :" + (notSent == "" ? " POST " + cfg.Channels.Xml.Url : ""));
                    trace.AppendLine(MaskCustomer(XmlChannel.Preview(cfg, msg), a));
                }

                if (notSent != "")
                {
                    trace.AppendLine("    result  : " + notSent);
                    return;
                }

                XmlChannel.Send(cfg, msg);
            }

            if (msg.Result == "SENT")
                trace.AppendLine("    Response  : SENT  HTTP " + msg.HttpStatus + "  " + msg.ElapsedMs + " ms  response: " + OneLine(msg.ResultDetail));
            else
                trace.AppendLine("    Response  : FAILED  " + (msg.HttpStatus > 0 ? "HTTP " + msg.HttpStatus + "  " : "") + msg.ElapsedMs + " ms  error: " + OneLine(msg.ResultDetail));
        }

        // ---------------------------------------------------------------- matching / templates

        private static bool Matches(DCMSRule rule, AlertData a)
        {
            if (rule.When == null) return true;

            foreach (KeyValuePair<string, string> c in rule.When)
            {
                string actual = c.Key.StartsWith("flag:", StringComparison.OrdinalIgnoreCase)
                    ? a.FlagAtStart(c.Key.Substring(5))
                    : a.Value(c.Key, null);

                if (!MatchValue(actual, c.Value ?? "")) return false;
            }
            return true;
        }

        // "A|B" any of, "!A|B" none of, "*" any non-empty value, "" empty / missing. Case-insensitive,
        // except "=A|B": exact case (like the old code's == compare, e.g. "=CONTACTLESS" does not match "Contactless").
        private static bool MatchValue(string actual, string pattern)
        {
            pattern = pattern.Trim();
            bool negate = pattern.StartsWith("!");
            if (negate) pattern = pattern.Substring(1);
            bool exactCase = pattern.StartsWith("=");
            if (exactCase) pattern = pattern.Substring(1);
            StringComparison cmp = exactCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

            bool any = pattern.Split('|').Any(p =>
                p.Trim() == "*" ? actual != "" : string.Equals(actual, p.Trim(), cmp));

            return negate ? !any : any;
        }

        private static string Fill(string template, AlertData a)
        {
            if (string.IsNullOrEmpty(template)) return "";
            return tokenRegex.Replace(template, t => a.Value(t.Groups[1].Value, t.Groups[2].Success ? t.Groups[2].Value : null));
        }

        /// <summary>For the log: the customer's account no, mobile, email and customer id masked wherever they appear in a text.</summary>
        private static string MaskCustomer(string text, AlertData a)
        {
            foreach (string name in new[] { "FromAc", "Mobile", "Email", "CustId" })
            {
                string v = a.Value(name, null);
                if (v.Length >= 4) text = text.Replace(v, Log.Mask(v), StringComparison.OrdinalIgnoreCase);
            }
            string otp = a.Value("Activationcode", null);   // Google Pay / Green PIN OTP: never in the log
            if (otp != "") text = text.Replace(otp, new string('X', otp.Length));
            return text;
        }

        private static string Show(string value)
        {
            return string.IsNullOrEmpty(value) ? "-" : value;
        }

        private static string OneLine(string text, int max = 500)
        {
            string t = Regex.Replace(text ?? "", @"\s+", " ").Trim();
            return t.Length > max ? t.Substring(0, max) + " ..." : t;
        }

        // ---------------------------------------------------------------- config

        /// <summary>
        /// Rules in memory. Appconfig.json is only re-checked every "configCheckSeconds" (default 5) and only
        /// re-read when its file date changed; all other alerts just get the rules already loaded.
        /// </summary>
        private static DCMSConfig LoadConfig()
        {
            if (DateTime.UtcNow.Ticks < Interlocked.Read(ref nextCheckTicks)) return config;   // fast path: no file access, no lock

            lock (configLock)
            {
                if (DateTime.UtcNow.Ticks < nextCheckTicks) return config;   // another thread just checked
                try
                {
                    if (configPath == "")
                    {
                        string? p = ConfigurationManager.AppSettings["DCMSConfigFile"];
                        if (string.IsNullOrWhiteSpace(p)) p = "Appconfig.json";
                        configPath = Path.IsPathRooted(p) ? p : Path.Combine(AppContext.BaseDirectory, p);
                    }

                    if (!File.Exists(configPath))
                    {
                        // no rules = no alerts are sent (logged once per check interval)
                        Log.Writerrorlog(DateTime.Now.ToString() + " >> Alert config not found : " + configPath);
                        config = new DCMSConfig();
                        configWriteTime = DateTime.MinValue;
                        return config;
                    }

                    DateTime writeTime = File.GetLastWriteTimeUtc(configPath);
                    if (writeTime != configWriteTime)
                    {
                        configWriteTime = writeTime;   // set first: a broken file is tried (and logged) once, again only after the next save
                        JsonSerializerOptions opt = new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true,
                            ReadCommentHandling = JsonCommentHandling.Skip,
                            AllowTrailingCommas = true
                        };
                        DCMSConfig? loaded = JsonSerializer.Deserialize<DCMSConfig>(File.ReadAllText(configPath), opt);
                        if (loaded != null)
                        {
                            config = loaded;
                            Log.Write(DateTime.Now.ToString() + " >> Alert config loaded : " + configPath + " : rules=" + config.Rules.Count
                                + ", xml=" + config.Channels.Xml.Enabled + " " + config.Channels.Xml.Url
                                + ", acl=" + config.Channels.Acl.Enabled + " " + config.Channels.Acl.Url
                                + ", vfEmail=" + config.Channels.VfEmail.Enabled + " " + config.Channels.VfEmail.Url
                                + ", re-check every " + config.ConfigCheckSeconds + " s");
                        }
                    }
                }
                catch (Exception ex)
                {
                    // keep the last good config; not retried until the file is saved again
                    Log.Writerrorlog(DateTime.Now.ToString() + " >> Alert config load Err (still using the previous rules) : " + configPath + " : " + ex.Message);
                }
                finally
                {
                    Interlocked.Exchange(ref nextCheckTicks, DateTime.UtcNow.AddSeconds(Math.Max(0, config.ConfigCheckSeconds)).Ticks);
                }
                return config;
            }
        }
    }

    /// <summary>Alert values for {tokens} and "when" conditions, read the same way handleClient reads them.</summary>
    internal sealed class AlertData
    {
        private static readonly Dictionary<string, FieldInfo> flagFields = typeof(clientMessage)
            .GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Where(f => f.FieldType == typeof(bool))
            .ToDictionary(f => f.Name, f => f, StringComparer.Ordinal);

        private readonly XmlDocument x;
        private readonly Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, bool> flagsAtStart = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private readonly DateTime? date;

        public string ClNo { get; }

        public static FieldInfo? FlagField(string name)
        {
            FieldInfo? f;
            return flagFields.TryGetValue(name, out f) ? f : null;
        }

        public AlertData(clientMessage m)
        {
            x = m.Data;
            ClNo = m.Counter.ToString();

            foreach (KeyValuePair<string, FieldInfo> f in flagFields)
                flagsAtStart[f.Key] = (bool)f.Value.GetValue(m)!;

            string currency = Node("Transaction_Currency");
            string amount = Node("Amount");
            if (currency == "356" && amount != "") amount = (Convert.ToDouble(amount) / 100).ToString();
            string balance = Node("Balance");
            if (balance != "") balance = (Convert.ToDouble(balance) / 100).ToString();
            string card = Node("CardNo");
            string fromAc = Node("From_Acc_No");
            string rawDate = Node("CurrentTranDateTime");

            DateTime d;
            if (DateTime.TryParseExact(rawDate, "dd/MM/yy HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) date = d;

            values["ClNo"] = ClNo;
            values["Amount"] = amount;
            values["Balance"] = balance;
            values["Currency"] = currency;
            values["Card4"] = card.Length > 4 ? card.Substring(card.Length - 4) : card;
            values["FromAc"] = fromAc;
            values["MaskedFromAc"] = fromAc.Length > 6 ? fromAc.Substring(0, 2) + "XX" + fromAc.Substring(fromAc.Length - 4) : fromAc;
            values["Date"] = rawDate;
            values["EmailDate"] = date.HasValue ? date.Value.ToString("dd/MMM", CultureInfo.InvariantCulture) : rawDate;
            values["TranTime"] = date.HasValue ? date.Value.ToString("HH:mm", CultureInfo.InvariantCulture) : "";
            values["Loc"] = Node("Loc");
            values["Product"] = Node("product");
            values["SetLimit"] = Node("Setlimit");
            values["Mobile"] = Node("MobNo");
            values["Email"] = Node("EmailId");
            values["CustId"] = Node("custID");
        }

        /// <summary>
        /// Named value above, otherwise the raw &lt;Alert&gt; node.
        /// Formats: any token :lower / :upper, e.g. {SetLimit:lower}; "Date" also takes a date format, e.g. {Date:dd-MM-yy}.
        /// </summary>
        public string Value(string name, string? format)
        {
            if (string.Equals(format, "lower", StringComparison.OrdinalIgnoreCase)) return Value(name, null).ToLowerInvariant();
            if (string.Equals(format, "upper", StringComparison.OrdinalIgnoreCase)) return Value(name, null).ToUpperInvariant();

            if (format != null && string.Equals(name, "Date", StringComparison.OrdinalIgnoreCase))
                return date.HasValue ? date.Value.ToString(format, CultureInfo.InvariantCulture) : values["Date"];

            string? v;
            return values.TryGetValue(name, out v) ? v : Node(name);
        }

        public IEnumerable<string> FlagsSet => flagsAtStart.Where(f => f.Value).Select(f => f.Key);

        public string FlagAtStart(string name)
        {
            bool b;
            return flagsAtStart.TryGetValue(name, out b) ? (b ? "true" : "false") : "";
        }

        private string Node(string name)
        {
            return x.SelectSingleNode("Alert/" + name)?.InnerText.Trim() ?? "";
        }
    }
}
