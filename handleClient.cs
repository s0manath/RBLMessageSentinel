using System.Xml;

namespace RBLMessageSentinel
{
    public class handleClient
    {
        public XmlDocument sData = null;
        public string sTranAmount = "";
        public string sFromAc = "";
        public string sTranDate = "";
        public string sTranCard = "";
        public string sTranLoc = "";
        public string sCurBal = "";
        public string sSMSTo = "";
        public string sEMailTo = "";
        public string sFDAmount = "";
        public string sFDAccount = "";
        public string sIntlTranCur = "";
        public string sIntlTranAmount = "";
        public string sReserve1 = "";
        public string sReserve2 = "";
        public string sReserve3 = "";
        public string sReserve4 = "";
        public string sRRN = "";
        public string TransactionCurrency = "";

        public long clNo = 0;

        public void startProcessingTCPMessage(object Message)
        {
            try
            {
                sData = ((clientMessage)Message).Data;
                clNo = ((clientMessage)Message).Counter;

                // All configured alerts (Appconfig.json rules): <sendMessage> XML to channels.xml.url
                DCMSAlertSender.Handle((clientMessage)Message);

                // International I0 / I1: logged to DB and sent only when the markup notification check allows it
                if (((clientMessage)Message).IntlZero || ((clientMessage)Message).IntlOne)
                {
                    ProcessInternational((clientMessage)Message);
                }
            }
            catch (Exception xObj)
            {
                Console.WriteLine(DateTime.Now.ToString() + " >> Message Format Err #" + clNo.ToString() + " : " + xObj.Message);
                Log.Write(DateTime.Now.ToString() + " >> PROCESS FAILED #" + clNo + " : " + xObj.Message);
                Log.Writerrorlog(DateTime.Now.ToString() + " >> Message Format Err #" + clNo.ToString() + " : " + xObj.ToString());
            }
        }

        private void ProcessInternational(clientMessage Message)
        {
            TransactionCurrency = sData.SelectSingleNode("Alert/Transaction_Currency")?.InnerText.ToString().Trim()??"";
            sTranAmount = sData.SelectSingleNode("Alert/Amount")?.InnerText.ToString().Trim() ?? "";
            if (TransactionCurrency == "356")
            {
                if (sTranAmount != "") sTranAmount = (Convert.ToDouble(sTranAmount) / 100).ToString();
            }

            sTranCard = sData.SelectSingleNode("Alert/CardNo")?.InnerText.ToString().Trim() ?? "";
            sTranDate = sData.SelectSingleNode("Alert/CurrentTranDateTime")?.InnerText.ToString().Trim() ?? "";
            sTranLoc = sData.SelectSingleNode("Alert/Loc")?.InnerText.ToString().Trim() ?? "";
            sSMSTo = sData.SelectSingleNode("Alert/MobNo")?.InnerText.ToString().Trim() ?? "";
            sEMailTo = sData.SelectSingleNode("Alert/EmailId")?.InnerText.ToString().Trim() ?? "";
            sFromAc = sData.SelectSingleNode("Alert/From_Acc_No")?.InnerText.ToString().Trim() ?? "";
            sCurBal = sData.SelectSingleNode("Alert/Balance")?.InnerText.ToString().Trim() ?? "";
            if (sCurBal != "") sCurBal = (Convert.ToDouble(sCurBal) / 100).ToString();
            sRRN = sData.SelectSingleNode("Alert/RRN")?.InnerText.ToString().Trim() ?? "";

            string trantype = sData.SelectSingleNode("Alert/TranType")?.InnerText.Trim() ?? "";
            string respCode = sData.SelectSingleNode("Alert/RespCode")?.InnerText.Trim() ?? "";

            if (sIntlTranAmount != "") sIntlTranAmount = (Convert.ToDouble(sIntlTranAmount) / 100).ToString();

            string tranCode = Message.IntlZero ? "I0" : "I1";
            string textKey = Message.IntlZero ? "INTLAdvertisementI0" : "INTLAdvertisementI1";

            Log.InsertRBLSMSLISTENERREQUEST(Convert.ToString(clNo), trantype, respCode, sTranAmount, sTranCard, sTranDate, sTranLoc,
                sSMSTo, sEMailTo, sFDAccount, sFDAmount, sFromAc, sCurBal, sIntlTranCur
                , sIntlTranAmount, sReserve1, sReserve2, sReserve3, sReserve4, sRRN);

            bool sendIntl = Log.CheckMarkUpNotificationStatus(sReserve2, sSMSTo, trantype, respCode, Convert.ToString(clNo));
            string MsgText = sendIntl ? DCMSAlertSender.SmsText(textKey) : "";
            Log.Write(DateTime.Now.ToString() + " >> INTL #" + clNo + " " + tranCode + " : markup check = " + sendIntl
                + (sendIntl && string.IsNullOrEmpty(MsgText) ? " - no text for " + textKey + ", not sent" : sendIntl ? "" : " - not sent"));
            if (!string.IsNullOrEmpty(MsgText))
            {
                MsgText = MsgText.Replace("#date#", sTranDate).Replace("#RRN#", sRRN).Replace("#Reserv1#", sReserve1).Replace("#Reserv2#", sReserve2).Replace("#Reserv3#", sReserve3).Replace("#Reserv4#", sReserve4);
                DCMSAlertSender.SendInternational(Message, textKey, MsgText);
            }
        }
    }
}
