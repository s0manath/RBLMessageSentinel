using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RBLMessageSentinel
{
    public class clsLog
    {
        public string trantype { get; set; }
        public string MobNo { get; set; }
        public string RespCode { get; set; }
        public string clNum { get; set; }
        public string sSendMsg { get; set; }
        public string sResponse { get; set; }
        public string logtype { get; set; }
        public string ErrorMsg { get; set; }
        public DateTime ReqDatetime { get; set; }
        public DateTime ResDatetime { get; set; }
        public string Reserv2 { get; set; }
        public string ExtendedTranType { get; set; }
        public string EmailId { get; set; }
        public string CustId { get; set; }
    }
}
