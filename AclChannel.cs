using System.Collections.Concurrent;
using System.Text;

namespace RBLMessageSentinel
{
    /// <summary>
    /// Plain SMS to the ACL push gateway, built exactly like the old handleClient.sendSMS():
    /// POST to channels.acl.url + "userId=..&amp;pass=..&amp;to=..&amp;text=..&amp;intflag=.." with the same form body.
    /// Used only by rules with "channel": "acl" (alerts the old app sent via ACL and that have no DCMS topic).
    /// </summary>
    internal static class AclChannel
    {
        private static readonly ConcurrentDictionary<int, HttpClient> clients = new ConcurrentDictionary<int, HttpClient>();

        public static bool IsEnabled(DCMSConfig cfg)
        {
            return cfg.Channels.Acl.Enabled && !string.IsNullOrWhiteSpace(cfg.Channels.Acl.Url);
        }

        public static void Send(DCMSConfig cfg, OutgoingAlert msg)
        {
            AclChannelConfig c = cfg.Channels.Acl;
            AlertData a = msg.Alert;
            clsLog objclsLog = new clsLog
            {
                logtype = "ACL",
                clNum = a.ClNo,
                trantype = a.Value("TranType", null),
                RespCode = a.Value("RespCode", null),
                ExtendedTranType = a.Value("ExtendedTranType", null),
                MobNo = msg.Mobile,
                EmailId = a.Value("Email", null),
                CustId = a.Value("CustId", null),
                sSendMsg = msg.SmsBody
            };

            System.Diagnostics.Stopwatch timer = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                string intflag = IntFlag(msg.Mobile);
                string postData = "usr=" + c.User + "&pwd=" + c.Password + "&from=" + c.From + "&to=" + msg.Mobile + "&type=text" + msg.SmsBody;
                string url = c.Url + "userId=" + c.User + "&pass=" + c.Password + "&appid=" + c.AppId + "&subappid=" + c.SubAppId
                    + "&contenttype=1&to=" + msg.Mobile + "&from=" + c.From + "&text=" + msg.SmsBody
                    + "&selfid=true&alert=1&dlrreq=true&intflag=" + intflag;

                using HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(postData, Encoding.UTF8, "application/x-www-form-urlencoded")
                };

                objclsLog.ReqDatetime = DateTime.Now;
                using HttpResponseMessage resp = Http(c).Send(req);
                msg.HttpStatus = (int)resp.StatusCode;
                using StreamReader reader = new StreamReader(resp.Content.ReadAsStream());
                string body = reader.ReadToEnd();
                objclsLog.ResDatetime = DateTime.Now;
                objclsLog.sResponse = body;

                if (!resp.IsSuccessStatusCode) throw new Exception("HTTP " + (int)resp.StatusCode + " : " + body);
                if (body.IndexOf("ERROR") != -1) throw new Exception(body);   // same check as the old sendSMS

                msg.Result = "SENT";
                msg.ResultDetail = body;
                msg.ElapsedMs = timer.ElapsedMilliseconds;
                Log.InsertRBLTransLog(objclsLog);
            }
            catch (Exception ex)
            {
                string error = ex.InnerException == null ? ex.Message : ex.Message + " -> " + ex.InnerException.Message;
                msg.Result = "FAILED";
                msg.ResultDetail = error;
                msg.ElapsedMs = timer.ElapsedMilliseconds;
                Console.WriteLine(DateTime.Now.ToString() + " >> ACL Err #" + a.ClNo + " : " + msg.RuleName + " : " + error);
                Log.Writerrorlog(DateTime.Now.ToString() + " >> ACL Err #" + a.ClNo + " : " + msg.RuleName + " : " + error);
                objclsLog.logtype = "ERROR";
                objclsLog.ErrorMsg = ex.ToString();
                Log.InsertRBLTransErrorLog(objclsLog);
            }
        }

        // old IsValidIndianMobileNumber: "false" for 12 digits starting 91 (or empty), "true" otherwise
        private static string IntFlag(string mobile)
        {
            if (string.IsNullOrWhiteSpace(mobile)) return "false";
            string digits = new string(mobile.Where(char.IsDigit).ToArray());
            return digits.Length == 12 && digits.StartsWith("91") ? "false" : "true";
        }

        private static HttpClient Http(AclChannelConfig c)
        {
            int timeout = c.TimeoutSeconds > 0 ? c.TimeoutSeconds : 30;
            return clients.GetOrAdd(timeout, t => new HttpClient { Timeout = TimeSpan.FromSeconds(t) });
        }
    }
}
