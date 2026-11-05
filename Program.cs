using JamaaTech.Smpp.Net.Lib;
using JamaaTech.Smpp.Net.Lib.Protocol;
using System;
using System.Data;
using System.Data.SqlClient;
using Microsoft.Data.SqlClient;
using System.Net.Sockets;
using System.Text.Json;
using System.Xml;
using static System.Runtime.InteropServices.JavaScript.JSType;
using JamaaTech.Smpp.Net.Lib.Protocol.Tlv;
using System.Transactions;
using System.Diagnostics;

namespace RBLMessageSentinel
{
    internal class Program
    {
        //TcpListener serverSocket = new TcpListener(48000);
        TcpListener serverSocket = new TcpListener(Convert.ToInt32(System.Configuration.ConfigurationManager.AppSettings["ListeningPort"].ToString()));
     
        TcpClient clientSocket = default(TcpClient);
        int counter = 0;
        int OrgCount = 0;
        SmppClientSession smppClientSession = null;
        SessionBindInfo smppSessionBindInfo = null;
        DataTable dtPINTryLimitBreached;
        private clsLog objclsLog;

        static void Main(string[] args)
        {
            Console.WriteLine("Start RBL Message Sentinel Application!");
            //new handleClient().sendMail("Dear Customer, This is test message.");
            Log.isBlocked = false;
            Program oProgram = new Program();
            oProgram.startListening();            
        }

        public void startListening()
        {
            serverSocket.Start();
            Console.WriteLine(DateTime.Now.ToString() + " >> Server Started.....");
            Log.Write(DateTime.Now.ToString() + " >> Server Started on port " + System.Configuration.ConfigurationManager.AppSettings["ListeningPort"]);

            try
            {
                //System.Timers.Timer oTimer = new System.Timers.Timer(60000);
                System.Timers.Timer oTimer = new System.Timers.Timer(Convert.ToInt32(System.Configuration.ConfigurationManager.AppSettings["InvalidPINTryExceedTimer"].ToString()));
                oTimer.Start();

                while (true)
                {
                    if (serverSocket.Pending())
                    {
                        clientSocket = serverSocket.AcceptTcpClient();
                        Thread oThread = new Thread(new ThreadStart(startAcceptingMessages));
                        oThread.Start();
                    }
                    Thread.Sleep(Convert.ToInt32(System.Configuration.ConfigurationManager.AppSettings["MainThreadSleepInterval"].ToString()));
                }
            }
            catch (Exception xObj)
            {
                Console.WriteLine("FATAL ERROR: " + xObj.Message);
                Log.Writerrorlog(DateTime.Now.ToString() + " >> FATAL ERROR : " + xObj.ToString());
            }
        }

        private void startAcceptingMessages()
        {
            System.Boolean bClientDisconnected = false;
            System.Boolean bPINChangeAlert = false;
            System.Boolean bCreditAlert = false;
            System.Boolean bC2CAlert = false;
            System.Boolean bFDAlert = false;
            System.Boolean bWrongPIN = false;            
            System.Boolean bInternationalZero = false;
            System.Boolean bInternationalOne = false;
            System.Boolean bDoNotHonor = false;
            System.Boolean bFstPinTran01 = false;
            System.Boolean bTranNotPermited57 = false;
            System.Boolean bTransactionpreference = false;
            System.Boolean bnon_metro_alert_triger = false;
            System.Boolean bnon_metro_alert_triger_2 = false;
            System.Boolean bmetro_alert_triger = false;
            System.Boolean bmetro_alert_triger_2 = false;
            System.Boolean bDebitCardDisableATMPOSECOMTran = false;
            System.Boolean bDebitCard_ExpiredCard = false;
            System.Boolean bDebitCard_txn_timout = false;
            System.Boolean bDebitCard_exceedLimit=false;
            System.Boolean bDebitCard_temblock = false;
            System.Boolean bDebitCard_InsufficientFund=false;
            System.Boolean bDebitCard_BSBDA_FI_001 = false;
            System.Boolean bDebitCard_BSBDA_FI_002 = false;
            System.Boolean bDebitCard_BSBDA_FI_003 = false;
            System.Boolean bDebitCard_BSBDA_FI_004 = false;
            System.Boolean bDebitCard_mATM_Warning = false;
            System.Boolean DebitCard_mATM_Final = false;
            System.Boolean DebitCard_Offus_Highuse = false;
            System.Boolean ReversalTran = false;
            System.Boolean DebitCard_Incorrect_CVV = false;
            System.Boolean bDebitCard_Merchant_Non_Complient = false;
            System.Boolean bDebitCard_ExpiredCardECOM = false;
            System.Boolean SIMotoTransactionAlert = false;
            System.Boolean bDebitCardTxnContactless = false;
            System.Boolean bMDES = false;
            System.Boolean bGPatATM = false;
            System.Boolean bGPG = false;
            System.Boolean bGPF = false;
            System.Boolean bGPOTPF = false;
            System.Boolean bGPOTPE = false;

            System.Boolean bNCMC1 = false;
            System.Boolean bNCMC2 = false;
            System.Boolean bNCMC3 = false;
            System.Boolean bNCMC4 = false;
            System.Boolean bNCMC5 = false;
            
            System.Boolean bACSOTPexceeded = false;

            byte[] bytesHead = new byte[4];
            byte[] bytesFrom = new byte[234];
            byte[] bytesFrom2 = new byte[178];
            string dataFromClient = null, dataHeader = "";

            Int32 messageLength = 0;

            NetworkStream networkStream = clientSocket.GetStream();
            networkStream.ReadTimeout = 30000; // a message that stops half way fails after 30 sec instead of blocking for ever

            Console.WriteLine(DateTime.Now.ToString() + " >> Listening Started..........");
            Log.Write(DateTime.Now.ToString() + " >> Client connected, listening started");

            //while ((true))
            while (!bClientDisconnected)
            {
                dataFromClient = "";

                try
                {
                    if (clientSocket.Connected)
                    {
                        if (networkStream.DataAvailable)
                        {
                            bClientDisconnected = false;
                            readFully(networkStream, bytesHead, bytesHead.Length);
                            dataFromClient = System.Text.Encoding.ASCII.GetString(bytesHead);
                            dataHeader = dataFromClient.Substring(2, dataFromClient.Length - 2).Replace("\0", "");
                            messageLength = Convert.ToInt32(byteArrayToHexString(bytesHead.Take(2).ToArray()), 16) - 2;
                            bytesFrom = new byte[messageLength];
                            // a message can arrive in several TCP packets: read until the whole length is in
                            int bytesRead = readFully(networkStream, bytesFrom, messageLength);
                            dataFromClient = System.Text.Encoding.ASCII.GetString(bytesFrom, 0, bytesRead).Trim('\0', ' ', '\r', '\n');
                            if (bytesRead < messageLength)
                            {
                                Log.Write(DateTime.Now.ToString() + " >> Incomplete message : expected " + messageLength + " bytes, received " + bytesRead);
                            }

                            //// Send confirmation back to client 17-05-2025 $@chin
                            string confirmationMessage = "Data Recived";
                            byte[] confirmationBytes = System.Text.Encoding.ASCII.GetBytes(confirmationMessage);
                            networkStream.Write(confirmationBytes, 0, confirmationBytes.Length);

                            ++OrgCount;
                            Log.Write(DateTime.Now.ToString() + " >> RECEIVED " + OrgCount + " : " + Log.MaskXml(dataFromClient.Trim()));

                            //dataFromClient = "<Alert><TranType>01</TranType><Amount>000000020000</Amount><settelment_amount></settelment_amount><DateTime>0910131519</DateTime><stan>752520</stan><time>184519</time><date>0910</date><RRN>625313752520</RRN><Auth_Code>752520</Auth_Code><RespCode>00</RespCode><Terminal_ID>61671753</Terminal_ID><Merchant_code>RANAAM HOSPITAL</Merchant_code><Transaction_Currency>356</Transaction_Currency><From_Acc_No>309016332940</From_Acc_No><To_Acc_No></To_Acc_No><switch_key>0041286006</switch_key><Loc>PRANAAM HOSPITALS PR</Loc><CardNo>4020********3201</CardNo><custID>103381201</custID><MobNo>919970297123</MobNo><EmailId>sachin.parle@paysis.co.in</EmailId><Balance>000010279733</Balance><product>POS</product><CurrentTranDateTime>10/09/26 18:45</CurrentTranDateTime></Alert>";
                           // dataFromClient = "<Alert><TranType>01</TranType><Amount>000000500000</Amount><settelment_amount></settelment_amount><DateTime>2610113000</DateTime><stan>752530</stan><time>113000</time><date>2610</date><RRN>629911752530</RRN><Auth_Code>752530</Auth_Code><RespCode>00</RespCode><Terminal_ID>RBLA0123</Terminal_ID><Merchant_code>RBL BANK ATM</Merchant_code><Transaction_Currency>356</Transaction_Currency><From_Acc_No>309016332940</From_Acc_No><To_Acc_No></To_Acc_No><switch_key>0041286016</switch_key><Loc>RBL BANK ATM ANDHERI MUMBAI</Loc><CardNo>4020********3201</CardNo><custID>103381201</custID><MobNo>919970297123</MobNo><EmailId>sachin.parle@paysis.co.in</EmailId><Balance>000010279733</Balance><product>ATM</product><CurrentTranDateTime>26/10/26 11:30</CurrentTranDateTime></Alert>";


                            if (dataFromClient.Contains('&'))
                            {
                                dataFromClient = dataFromClient.ToString().Replace('&', ' ');
                            }

                            bInternationalOne = false;
                            bInternationalZero = false;
                            bPINChangeAlert = false;
                            bCreditAlert = false;
                            bC2CAlert = false;
                            bFDAlert = false;
                            bWrongPIN = false;
                            bDoNotHonor = false;
                            bFstPinTran01 = false;
                            bTranNotPermited57 = false;
                            bTransactionpreference = false;
                            bnon_metro_alert_triger = false;
                            bnon_metro_alert_triger_2 = false;
                            bmetro_alert_triger = false;
                            bmetro_alert_triger_2 = false;
                            bDebitCardDisableATMPOSECOMTran = false;
                            bDebitCard_ExpiredCard = false;
                            bDebitCard_txn_timout = false;
                            bDebitCard_exceedLimit = false;
                            bDebitCard_temblock = false;
                            bDebitCard_InsufficientFund = false;
                            bDebitCard_BSBDA_FI_001 = false;
                            bDebitCard_BSBDA_FI_002 = false;
                            bDebitCard_BSBDA_FI_003 = false;
                            bDebitCard_BSBDA_FI_004 = false;
                            bDebitCard_mATM_Warning = false;
                            DebitCard_mATM_Final = false;
                            DebitCard_Offus_Highuse = false;
                            ReversalTran = false;
                            DebitCard_Incorrect_CVV = false;
                            SIMotoTransactionAlert = false;
                            bDebitCardTxnContactless = false;
                            bMDES=false;
                            bDebitCard_Merchant_Non_Complient = false;
                            bDebitCard_ExpiredCardECOM = false;

                            bGPatATM = false;
                            bGPG = false;
                            bGPF = false;
                            bGPOTPF = false;
                            bGPOTPE = false;

                            bNCMC1 = false;
                            bNCMC2 = false;
                            bNCMC3 = false;
                            bNCMC4 = false; 
                            bNCMC5 = false;

                            bACSOTPexceeded = false;

                            if (dataFromClient != "")
                            {
                                XmlDocument xdoc = new XmlDocument();
                                xdoc.LoadXml(dataFromClient.Trim());
                                XmlNode TranType, xRespCode, xextendedTranType, xnon_metro_alert_triger, xnon_metro_alert_triger_2, xmetro_alert_triger, xmetro_alert_triger_2, xPvt_Fd37, xproduct, xBSBDA_alert_triger_2_Fin, xBSBDA_alert_triger_Fin, xBSBDA_alert_triger_2_NonFin, xBSBDA_alert_triger_NonFin, xMicroATM_alert_triger_2, xMicroATM_alert_triger, xIsReversalTran, xSubTranType, xDescription;
                                string trantype = "";
                                string RespCode = "";
                                string SubTranType = "", Description = "";
                                string ExtendedTranType = "", non_metro_alert_triger="", non_metro_alert_triger_2="", metro_alert_triger="", metro_alert_triger_2= "", Pvt_Fd37="", product="", BSBDA_alert_triger_2_Fin="", BSBDA_alert_triger_Fin="", BSBDA_alert_triger_2_NonFin="", BSBDA_alert_triger_NonFin="", MicroATM_alert_triger_2="", MicroATM_alert_triger="", IsReversalTran="";                                

                                TranType = xdoc.SelectSingleNode("Alert/TranType");
                                xRespCode = xdoc.SelectSingleNode("Alert/RespCode");

                                xextendedTranType = xdoc.SelectSingleNode("Alert/ExtendedTranType");
                                xnon_metro_alert_triger = xdoc.SelectSingleNode("Alert/non_metro_alert_triger");
                                xnon_metro_alert_triger_2 = xdoc.SelectSingleNode("Alert/non_metro_alert_triger_2");
                                xmetro_alert_triger = xdoc.SelectSingleNode("Alert/metro_alert_triger");
                                xmetro_alert_triger_2 = xdoc.SelectSingleNode("Alert/metro_alert_triger_2");
                                xPvt_Fd37 = xdoc.SelectSingleNode("Alert/Pvt_Fd.37");
                                xproduct = xdoc.SelectSingleNode("Alert/product");
                                xBSBDA_alert_triger_2_Fin = xdoc.SelectSingleNode("Alert/BSBDA_alert_triger_Fin_2");
                                xBSBDA_alert_triger_Fin = xdoc.SelectSingleNode("Alert/BSBDA_alert_triger_Fin");
                                xBSBDA_alert_triger_2_NonFin = xdoc.SelectSingleNode("Alert/BSBDA_alert_triger_NonFin_2");
                                xBSBDA_alert_triger_NonFin = xdoc.SelectSingleNode("Alert/BSBDA_alert_triger_NonFin");
                                xMicroATM_alert_triger_2 = xdoc.SelectSingleNode("Alert/Micro-ATM_alert_triger_2");
                                xMicroATM_alert_triger = xdoc.SelectSingleNode("Alert/Micro-ATM_alert_triger");
                                //xSetlimit = xdoc.SelectSingleNode("Alert/Setlimit");
                                xIsReversalTran = xdoc.SelectSingleNode("Alert/IsReversalTran");
                                xSubTranType = xdoc.SelectSingleNode("Alert/SubTranType");
                                xDescription = xdoc.SelectSingleNode("Alert/Description");

                                var Transactionpreference = new[] { "5060", "5061", "5062", "5063", "5064", "5065", "5066", "5067", "5068", "5069", "5070", "5071", "5072", "5073", "5074", "5075", "5076", "5077", "5078", "5079", "5080", "5081", "5082", "5083" };
                                var PvtFd = new[] {"9999", "K401"};

                                if (TranType != null && xRespCode != null)
                                {
                                    trantype = TranType.InnerText.ToString().Trim();
                                    RespCode = xRespCode.InnerText.ToString().Trim();

                                    ExtendedTranType = xextendedTranType?.InnerText.Trim() ?? "Unknown";
                                    non_metro_alert_triger = xnon_metro_alert_triger?.InnerText.Trim() ?? "Unknown";
                                    non_metro_alert_triger_2 = xnon_metro_alert_triger_2?.InnerText.Trim() ?? "Unknown";
                                    metro_alert_triger = xmetro_alert_triger?.InnerText.Trim() ?? "Unknown";
                                    metro_alert_triger_2 = xmetro_alert_triger_2?.InnerText.Trim() ?? "Unknown";
                                    Pvt_Fd37 = xPvt_Fd37?.InnerText.Trim() ?? "Unknown";
                                    product = xproduct?.InnerText.Trim() ?? "Unknown";
                                    BSBDA_alert_triger_2_Fin = xBSBDA_alert_triger_2_Fin?.InnerText.Trim() ?? "Unknown";
                                    BSBDA_alert_triger_Fin = xBSBDA_alert_triger_Fin?.InnerText.Trim() ?? "Unknown";
                                    BSBDA_alert_triger_2_NonFin = xBSBDA_alert_triger_2_NonFin?.InnerText.Trim() ?? "Unknown";
                                    BSBDA_alert_triger_NonFin = xBSBDA_alert_triger_NonFin?.InnerText.Trim() ?? "Unknown";
                                    MicroATM_alert_triger_2 = xMicroATM_alert_triger_2?.InnerText.Trim() ?? "Unknown";
                                    MicroATM_alert_triger = xMicroATM_alert_triger?.InnerText.Trim() ?? "Unknown";
                                    IsReversalTran = xIsReversalTran?.InnerText.Trim() ?? "Unknown";
                                    //Setlimit = xSetlimit?.InnerText.Trim() ?? "Unknown";
                                    SubTranType = xSubTranType?.InnerText.Trim() ?? "Unknown";
                                    Description = xDescription?.InnerText.Trim() ?? "Unknown";
                                }

                                //if (RespCode == "05") //Do Not honor 05 (Due to MCC Restriction) 
                                if (ExtendedTranType == "21122222") //Do Not honor 05 (Due to MCC Restriction) 2112
                                {
                                    bInternationalOne = false;
                                    bInternationalZero = false;
                                    bPINChangeAlert = false;
                                    bC2CAlert = false;
                                    bFDAlert = false;
                                    bWrongPIN = false;
                                    bDoNotHonor = true;
                                    bFstPinTran01 = false;
                                    bTranNotPermited57 = false;

                                    bTransactionpreference = false;
                                    bnon_metro_alert_triger = false;
                                    bnon_metro_alert_triger_2 = false;
                                    bmetro_alert_triger = false;
                                    bmetro_alert_triger_2 = false;
                                    bDebitCardDisableATMPOSECOMTran = false;
                                    bDebitCard_ExpiredCard = false;
                                    bDebitCard_txn_timout = false;
                                    bDebitCard_exceedLimit = false;
                                    bDebitCard_temblock = false;
                                    bDebitCard_InsufficientFund = false;
                                    bDebitCard_BSBDA_FI_001 = false;
                                    bDebitCard_BSBDA_FI_002 = false;
                                    bDebitCard_BSBDA_FI_003 = false;
                                    bDebitCard_BSBDA_FI_004 = false;
                                    bDebitCard_mATM_Warning = false;
                                    DebitCard_mATM_Final = false;
                                    DebitCard_Offus_Highuse = false;
                                    bMDES=false;    
                                }
                                else if (RespCode == "01" && ExtendedTranType == "2111")
                                {
                                    bInternationalOne = false;
                                    bInternationalZero = false;
                                    bPINChangeAlert = false;
                                    bC2CAlert = false;
                                    bFDAlert = false;
                                    bWrongPIN = false;
                                    bDoNotHonor = false;
                                    bFstPinTran01 = true;
                                    bTranNotPermited57 = false;

                                    bTransactionpreference = false;
                                    bnon_metro_alert_triger = false;
                                    bnon_metro_alert_triger_2 = false;
                                    bmetro_alert_triger = false;
                                    bmetro_alert_triger_2 = false;
                                    bDebitCardDisableATMPOSECOMTran = false;
                                    bDebitCard_ExpiredCard = false;
                                    bDebitCard_txn_timout = false;
                                    bDebitCard_exceedLimit = false;
                                    bDebitCard_temblock = false;
                                    bDebitCard_InsufficientFund = false;
                                    bDebitCard_BSBDA_FI_001 = false;
                                    bDebitCard_BSBDA_FI_002 = false;
                                    bDebitCard_BSBDA_FI_003 = false;
                                    bDebitCard_BSBDA_FI_004 = false;
                                    bDebitCard_mATM_Warning = false;
                                    DebitCard_mATM_Final = false;
                                    DebitCard_Offus_Highuse = false;
                                }
                                else if (trantype == "92" && (RespCode != "55" || RespCode != "75"))
                                {
                                    //pin change
                                    bInternationalOne = false;
                                    bInternationalZero = false;
                                    bPINChangeAlert = true;
                                    bC2CAlert = false;
                                    bFDAlert = false;
                                    bWrongPIN = false;
                                    bDoNotHonor = false;
                                    bFstPinTran01 = false;
                                    bTranNotPermited57 = false;
                                }
                                else if (trantype == "91" && (RespCode != "55" || RespCode != "75") 
                                    && !Transactionpreference.Contains(ExtendedTranType) 
                                    && Pvt_Fd37!="9999" && ExtendedTranType != "5922" 
                                    && !string.Equals(ExtendedTranType, "9924", StringComparison.Ordinal) 
                                    && !string.Equals(ExtendedTranType, "5679", StringComparison.Ordinal))
                                {
                                    //FD
                                    bInternationalOne = false;
                                    bInternationalZero = false;
                                    bFDAlert = true;
                                    bPINChangeAlert = false;
                                    bC2CAlert = false;
                                    bCreditAlert = false;
                                    bWrongPIN = false;
                                    bDoNotHonor = false;
                                    bFstPinTran01 = false;
                                    bTranNotPermited57 = false;
                                }                               
                                else if (trantype == "40"  && ExtendedTranType=="9010" && RespCode == "00")
                                {
                                    // C2C Alert
                                    bInternationalOne = false;
                                    bInternationalZero = false;
                                    bFDAlert = false;
                                    bPINChangeAlert = false;
                                    bC2CAlert = true;
                                    bWrongPIN = false;
                                    bDoNotHonor = false;
                                    bFstPinTran01 = false;
                                    bTranNotPermited57 = false;
                                }
                                else if ((trantype == "42") && (RespCode != "55" || RespCode != "75"))
                                {
                                    bInternationalOne = false;
                                    bInternationalZero = false;
                                    bCreditAlert = true;
                                    bFDAlert = false;
                                    bPINChangeAlert = false;
                                    bC2CAlert = false;
                                    bWrongPIN = false;

                                    bDoNotHonor = false;
                                    bFstPinTran01 = false;
                                    bTranNotPermited57 = false;
                                }
                                else if (trantype == "I0")
                                {
                                    bInternationalOne = false;
                                    bInternationalZero = true;
                                    bFDAlert = false;
                                    bPINChangeAlert = false;
                                    bC2CAlert = false;
                                    bCreditAlert = false;
                                    bWrongPIN = false;
                                    bDoNotHonor = false;
                                    bFstPinTran01 = false;
                                    bTranNotPermited57 = false;

                                }
                                else if (trantype == "I1")
                                {
                                    bInternationalOne = true;
                                    bInternationalZero = false;
                                    bFDAlert = false;
                                    bPINChangeAlert = false;
                                    bC2CAlert = false;
                                    bCreditAlert = false;
                                    bWrongPIN = false;
                                    bDoNotHonor = false;
                                    bFstPinTran01 = false;
                                    bTranNotPermited57 = false;

                                }
                                else if (trantype == "91" && Transactionpreference.Any(x => x == ExtendedTranType) && RespCode == "00")
                                {
                                    bTransactionpreference = true;
                                }
                                else if (non_metro_alert_triger == "true" && RespCode == "00")
                                {                                   
                                    bnon_metro_alert_triger = true;
                                }
                                else if (non_metro_alert_triger_2 == "true" && RespCode == "00")
                                {                                    
                                    bnon_metro_alert_triger_2 = true;
                                }
                                else if (metro_alert_triger == "true" && RespCode == "00")
                                {
                                    bmetro_alert_triger = true;
                                }
                                else if (metro_alert_triger_2 == "true" && RespCode == "00")
                                {
                                    bmetro_alert_triger_2 = true;
                                }
                                else if (PvtFd.Any(x => x == Pvt_Fd37))
                                {                                    
                                    bDebitCardDisableATMPOSECOMTran = true;
                                }
                                else if (RespCode=="54")
                                {
                                    bDebitCard_ExpiredCard = true;
                                }
                                else if (RespCode == "91" && (trantype=="01" || trantype == "00"))
                                {
                                    bDebitCard_txn_timout = true;
                                }
                                else if (RespCode == "61")
                                {
                                    bDebitCard_exceedLimit = true;
                                }
                                else if (ExtendedTranType == "2114")//RespCode == "41" //ExtendedTranType=="2114"
                                {
                                    bDebitCard_temblock = true;
                                }
                                else if (RespCode == "51")
                                {
                                    bDebitCard_InsufficientFund = true;
                                }

                                else if (RespCode == "00" && BSBDA_alert_triger_2_Fin=="true")
                                {
                                    bDebitCard_BSBDA_FI_001 = true;
                                }
                                else if (RespCode == "00" && BSBDA_alert_triger_Fin=="true")
                                {
                                    bDebitCard_BSBDA_FI_002 = true;
                                }
                                else if (RespCode == "00" && BSBDA_alert_triger_2_NonFin=="true")
                                {
                                    bDebitCard_BSBDA_FI_003 = true;
                                }
                                else if (RespCode == "00" && BSBDA_alert_triger_NonFin=="true")
                                {
                                    bDebitCard_BSBDA_FI_004 = true;
                                }
                                else if (RespCode == "00" && MicroATM_alert_triger_2=="true")
                                {
                                    bDebitCard_mATM_Warning = true;
                                }
                                else if (RespCode == "00" && MicroATM_alert_triger=="true")
                                {
                                    DebitCard_mATM_Final = true;
                                }
                                else if (RespCode == "65")
                                {
                                    DebitCard_Offus_Highuse = true;
                                }
                                else if (IsReversalTran== "ReversalTran" && trantype != "25")
                                {
                                    ReversalTran = true;
                                }
                                else if (RespCode == "59")
                                {
                                    DebitCard_Incorrect_CVV = true;
                                }
                                else if (RespCode == "34" && ExtendedTranType=="2112")
                                {
                                    bDebitCard_Merchant_Non_Complient = true;
                                }
                                else if (ExtendedTranType=="2113")
                                {
                                    bDebitCard_ExpiredCardECOM = true;
                                }
                                else if (trantype == "58" || trantype == "59")
                                {
                                    SIMotoTransactionAlert = true;
                                }
                                else if (ExtendedTranType=="2115")
                                {
                                    bDebitCardTxnContactless = true;
                                }
                                else if (trantype == "SP")
                                {
                                    bMDES=true;
                                }
                                else if (trantype == "GP" && SubTranType== "GenOTP")
                                {
                                    bGPatATM = true;
                                }
                                else if (trantype == "GP" && SubTranType == "ValOTP")
                                {
                                    bGPG = true;
                                }
                                else if (trantype == "GP" && SubTranType == "IncorrectOTP")
                                {
                                    bGPF = true;
                                }
                                else if (trantype == "GP" && SubTranType == "OTPAttemptExceeded")
                                {
                                    bGPOTPF = true;
                                }
                                else if (trantype == "GP" && SubTranType == "OTPExpired")
                                {
                                    bGPOTPE = true;
                                }
                                else if(trantype=="25" && ExtendedTranType=="9021" && IsReversalTran != "ReversalTran" && RespCode=="00")
                                {
                                    bNCMC1 = true;
                                }
                                else if (trantype == "25" && ExtendedTranType == "9020" && RespCode == "00")
                                {
                                    bNCMC2 = true;
                                }
                                else if (trantype == "00" && ExtendedTranType == "9924" && RespCode == "00")
                                {
                                    bNCMC3 = true;
                                }
                                else if (trantype == "91" && ExtendedTranType == "5922" && RespCode == "00")
                                {
                                    bNCMC4 = true;
                                }
                                else if (IsReversalTran == "ReversalTran" && (ExtendedTranType == "9020" || ExtendedTranType == "9021") && trantype=="25")
                                {
                                    bNCMC5 = true;
                                }
                                else if (Description == "OTP attempt limit exceeded ACS")
                                {
                                    bACSOTPexceeded = true;
                                }
                                if (RespCode == "55" || RespCode == "75")
                                //if (RespCode == "55")
                                {
                                    //invalid pin
                                    bInternationalOne = false;
                                    bInternationalZero = false;
                                    bPINChangeAlert = false;
                                    bC2CAlert = false;
                                    bFDAlert = false;
                                    bWrongPIN = true;
                                    bDoNotHonor = false;
                                    bFstPinTran01 = false;
                                    bTranNotPermited57 = false;
                                }


                                #region not used code
                                //if (dataHeader.Equals("92"))
                                //{
                                //    bPINChangeAlert = true;
                                //    bC2CAlert = false;
                                //    bFDAlert = false;
                                //    bWrongPIN = false;
                                //}
                                //else if (dataHeader.Equals("21") || dataHeader.Equals("54"))
                                //{
                                //    bCreditAlert = dataHeader.Equals("21");

                                //    if (dataHeader.Equals("21"))
                                //    {
                                //        if (!dataFromClient.Substring(dataFromClient.Length - 4).Equals("9501"))
                                //        {
                                //            dataFromClient = "";
                                //        }
                                //    }
                                //    bFDAlert = false;
                                //    bPINChangeAlert = false;
                                //    bC2CAlert = true;
                                //    bWrongPIN=false;
                                //}
                                //else if (dataHeader.Equals("91"))
                                //{

                                //    bFDAlert = true;
                                //    bPINChangeAlert = false;
                                //    bC2CAlert = false;
                                //    bCreditAlert = false;
                                //    bWrongPIN=false;
                                //}
                                //else if ((dataFromClient.Substring(260, 2) == "55" || dataFromClient.Substring(260, 2) == "75" || dataFromClient.Substring(258, 2) == "55" || dataFromClient.Substring(258, 2) == "75") && dataFromClient.Length!=264)
                                //{
                                //    bPINChangeAlert = false;
                                //    bC2CAlert = false;
                                //    bFDAlert = false;
                                //    bWrongPIN = true;
                                //    Console.WriteLine(DateTime.Now.ToString() + " >> Message #" + counter.ToString() + " : " + dataFromClient.Trim());
                                //    Log.Writerrorlog(DateTime.Now.ToString() + " >> Message #" + counter.ToString() + " : " + dataFromClient.Trim());
                                //    Log.Writerrorlog(DateTime.Now.ToString() + " >> Message #" + counter.ToString() + " : " + dataFromClient.ToString().Length);

                                //}

                                //else
                                //{
                                //    bCreditAlert = dataHeader.Equals("42");
                                //    bFDAlert = false;
                                //    bPINChangeAlert = false;
                                //    bC2CAlert = false;
                                //    bWrongPIN=false;
                                //}
                                #endregion

                                if (dataFromClient != "")
                                {
                                    counter++;

                                    Console.WriteLine(DateTime.Now.ToString() + " >> Message #" + counter.ToString() + " : " + dataFromClient.Trim());

                                    (new Thread(new ParameterizedThreadStart((new handleClient()).startProcessingTCPMessage))).Start((object)(new clientMessage(xdoc, counter, bPINChangeAlert, bCreditAlert, bC2CAlert, bFDAlert, bWrongPIN, bInternationalZero, 
                                        bInternationalOne, bDoNotHonor, bFstPinTran01, bTranNotPermited57, bTransactionpreference, bnon_metro_alert_triger, bnon_metro_alert_triger_2, 
                                        bmetro_alert_triger, bmetro_alert_triger_2, bDebitCardDisableATMPOSECOMTran, bDebitCard_ExpiredCard, bDebitCard_txn_timout, 
                                        bDebitCard_exceedLimit, bDebitCard_temblock, bDebitCard_InsufficientFund,bDebitCard_BSBDA_FI_001,bDebitCard_BSBDA_FI_002,bDebitCard_BSBDA_FI_003,
                                        bDebitCard_BSBDA_FI_004,bDebitCard_mATM_Warning,DebitCard_mATM_Final, DebitCard_Offus_Highuse, ReversalTran, DebitCard_Incorrect_CVV, 
                                        bDebitCard_Merchant_Non_Complient, bDebitCard_ExpiredCardECOM, SIMotoTransactionAlert, bDebitCardTxnContactless, bMDES,
                                        bGPatATM,bGPG,bGPF,bGPOTPF,bGPOTPE,bNCMC1,bNCMC2,bNCMC3,bNCMC4,bNCMC5, bACSOTPexceeded,
                                        ref smppClientSession)));
                                }
                            }
                        }
                    }
                    else
                    {
                        if (!bClientDisconnected)
                        {
                            Console.WriteLine(DateTime.Now.ToString() + " >> Client Disconnected");                            
                            bClientDisconnected = true;
                            try { if (clientSocket != null) clientSocket.Close(); }
                            catch (Exception ex2) { }

                            //goto startListening;
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (clientSocket.Connected)
                    {
                        Console.WriteLine(DateTime.Now.ToString() + " >> Message Err #" + counter.ToString() + " : " + ex.Message);
                        Log.Write(DateTime.Now.ToString() + " >> Message Err #" + counter.ToString() + " : " + ex.Message);
                        Log.Writerrorlog(DateTime.Now.ToString() + " >> Message Err #" + counter.ToString() + " : " + ex.ToString());
                    }
                    else
                    {
                        bClientDisconnected = true;

                        Console.WriteLine(DateTime.Now.ToString() + " >> Client Disconnected");
                        try { if (clientSocket != null) clientSocket.Close(); }
                        catch (Exception ex2) { }

                        //goto startListening;
                    }
                }

                //Thread.Sleep(500);    
                Thread.Sleep(Convert.ToInt32(System.Configuration.ConfigurationManager.AppSettings["AcceptingMessagesThreadSleepInterval"].ToString()));
            }
        }

        // Reads until 'count' bytes are in or the client closes the connection; returns the bytes read
        private static int readFully(NetworkStream stream, byte[] buffer, int count)
        {
            int total = 0;
            while (total < count)
            {
                int n = stream.Read(buffer, total, count - total);
                if (n == 0) break; // connection closed
                total += n;
            }
            return total;
        }

        public static string byteArrayToHexString(byte[] ba)
        {
            try
            {
                return BitConverter.ToString(ba).Replace("-", "");
            }
            catch (Exception xObj)
            {
                return "";
            }
        }
    }
}
