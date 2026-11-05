namespace RBLMessageSentinel
{
    /// <summary>Appconfig.json</summary>
    public class DCMSConfig
    {
        public ChannelsConfig Channels { get; set; } = new ChannelsConfig();
        public bool TraceLog { get; set; } = true;   // true = full XML in alertLog for every alert (UAT); false = short block
        public int ConfigCheckSeconds { get; set; } = 5;   // how often Appconfig.json is checked for changes (0 = every alert)
        public Dictionary<string, string> OldApp { get; set; } = new Dictionary<string, string>();
        public Dictionary<string, string> EmailTemplates { get; set; } = new Dictionary<string, string>();   // HTML bodies for VF email, by name
        public List<DCMSRule> Rules { get; set; } = new List<DCMSRule>();
    }

    public class ChannelsConfig
    {
        public XmlChannelConfig Xml { get; set; } = new XmlChannelConfig();
        public AclChannelConfig Acl { get; set; } = new AclChannelConfig();
        public VfEmailChannelConfig VfEmail { get; set; } = new VfEmailChannelConfig();
    }

    /// <summary>VF (Infinito) email API, used exactly like the old sendMailVFAsync() for rules with "emailTemplate"</summary>
    public class VfEmailChannelConfig
    {
        public bool Enabled { get; set; } = false;
        public string Url { get; set; } = "";            // old App.config VFurl
        public string User { get; set; } = "";           // VFUName (Basic auth)
        public string Password { get; set; } = "";       // VFUPass
        public string Subject { get; set; } = "Transaction Notification";
        public string FromEmail { get; set; } = "alerts@rbl.bank.in";
        public string FromName { get; set; } = "RBL bank transaction alert";
        public string ReplyToEmail { get; set; } = "no-reply@rbl.bank.in";
        public string ReplyToName { get; set; } = "RBL bank transaction alert";
        public int TimeoutSeconds { get; set; } = 30;
    }

    /// <summary>ACL push SMS gateway, used exactly like the old sendSMS() (only for rules with "channel": "acl")</summary>
    public class AclChannelConfig
    {
        public bool Enabled { get; set; } = false;
        public string Url { get; set; } = "";            // old App.config SMSURL (ends with '?')
        public string User { get; set; } = "";           // userId / usr
        public string Password { get; set; } = "";       // pass / pwd
        public string AppId { get; set; } = "";          // appid
        public string SubAppId { get; set; } = "";       // subappid
        public string From { get; set; } = "RBLBNK";     // sender id
        public int TimeoutSeconds { get; set; } = 30;
    }

    /// <summary>Alert gateway taking the &lt;sendMessage&gt; XML (topicName + mergeVars), POSTed to Url</summary>
    public class XmlChannelConfig
    {
        public bool Enabled { get; set; } = false;
        public string Url { get; set; } = "";
        public string AppId { get; set; } = "DCMSDEBITCARD";
        public string Version { get; set; } = "1.0";
        public string ContentType { get; set; } = "application/xml";
        public string User { get; set; } = "";
        public string Password { get; set; } = "";
        public int TimeoutSeconds { get; set; } = 30;
        public bool SendCustomerIdAndAccountNo { get; set; } = true;

        // Client certificate for HTTPS (mutual TLS). Empty = none. Relative paths are relative to the exe folder.
        //   .pfx / .p12 : used with the private key inside it (password = CertificatePassword)
        //   .cer / .crt : the same certificate with its private key must be installed in Windows (Personal store)
        public string CertificatePath { get; set; } = "";
        public string CertificatePassword { get; set; } = "";

        // Server / CA certificate (.cer / .crt) to trust for the endpoint's HTTPS certificate,
        // e.g. when it is issued by an internal bank CA. Empty = normal Windows trust only.
        public string ServerCertificatePath { get; set; } = "";
    }

    public class DCMSRule
    {
        public string Name { get; set; } = "";
        public bool Enabled { get; set; } = true;
        public string? Flag { get; set; }
        public Dictionary<string, string>? When { get; set; }
        public string Channel { get; set; } = "xml";     // "xml" = <sendMessage> to channels.xml; "acl" = plain SMS to channels.acl (no topic)
        public string Topic { get; set; } = "";
        public bool Mobile { get; set; } = true;        // <mobileNo>
        public bool Email { get; set; } = false;        // <emailID>
        // name in "emailTemplates": the email goes via channels.vfEmail with this HTML (like the old app) and the XML
        // then has no <emailID>. Empty, or vfEmail disabled = the email goes inside the XML (<emailID>, DCMS template).
        public string? EmailTemplate { get; set; }
        public string? Sms { get; set; }                // mergeVar SMSBODY
        public Dictionary<string, string>? MergeVars { get; set; }
        public bool Drop { get; set; } = false;
    }
}
