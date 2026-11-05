using JamaaTech.Smpp.Net.Lib;
using JamaaTech.Smpp.Net.Lib.Protocol;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace RBLMessageSentinel
{
    public class clientMessage
    {
        public string Message = "";
        public long Counter = 0;
        public Boolean PINChange = false;
        public Boolean CreditAlert = false;
        public Boolean C2CAlert = false;
        public Boolean FDAlert = false;
        public Boolean WrongPIN = false;
        public Boolean IntlZero = false;
        public Boolean IntlOne = false;
        public Boolean DoNotHonor = false;
        public Boolean FstPinTran01 = false;
        public Boolean TranNotPermited57 = false;
        public Boolean bTransactionpreference = false;
        public Boolean bnon_metro_alert_triger = false;
        public Boolean bnon_metro_alert_triger_2 = false;
        public Boolean bmetro_alert_triger = false;
        public Boolean bmetro_alert_triger_2 = false;
        public Boolean bDebitCardDisableATMPOSECOMTran = false;
        public Boolean bDebitCard_ExpiredCard = false;
        public Boolean bDebitCard_txn_timout =false;
        public Boolean bDebitCard_exceedLimit = false;
        public Boolean bDebitCard_temblock=false;   
        public Boolean bDebitCard_InsufficientFund = false;
        public Boolean bDebitCard_BSBDA_FI_001 = false;
        public Boolean bDebitCard_BSBDA_FI_002 = false;
        public Boolean bDebitCard_BSBDA_FI_003 = false;
        public Boolean bDebitCard_BSBDA_FI_004 = false;
        public Boolean bDebitCard_mATM_Warning = false;
        public Boolean DebitCard_mATM_Final = false;
        public Boolean bDebitCard_Offus_Highuse = false;
        public Boolean bReversalTran=false;
        public Boolean bDebitCard_Incorrect_CVV = false;
        public Boolean bDebitCard_Merchant_Non_Complient = false;
        public Boolean bDebitCard_ExpiredCardECOM = false;
        public Boolean SIMotoTransactionAlert = false;
        public Boolean bDebitCardTxnContactless=false;
        public Boolean bMDES = false;
        public Boolean bGPatATM = false;
        public Boolean bGPG = false;
        public Boolean bGPF = false;
        public Boolean bGPOTPF = false;
        public Boolean bGPOTPE = false;
        public Boolean bNCMC1 = false;
        public Boolean bNCMC2 = false;
        public Boolean bNCMC3 = false;
        public Boolean bNCMC4 = false;
        public Boolean bNCMC5 = false;
        public Boolean bACSOTPexceeded = false;


        public SmppClientSession smppClientSession = null;
        public XmlDocument Data = null;

        public clientMessage(XmlDocument msg, long _c, Boolean _p, Boolean _d, Boolean _c2c, Boolean _FD, Boolean _WPIN, Boolean _IntZero, Boolean _IntOne, 
            Boolean _DoNotHonor, Boolean _FstPinTran01, Boolean _TranNotPermited57, Boolean _Transactionpreference, Boolean _bnon_metro_alert_triger, 
            Boolean _bnon_metro_alert_triger_2, Boolean _bmetro_alert_triger, Boolean _bmetro_alert_triger_2,Boolean _DebitCardDisableATMPOSECOMTran,
            Boolean _DebitCard_ExpiredCard,Boolean _DebitCard_txn_timout,Boolean _DebitCard_exceedLimit,Boolean _DebitCard_temblock, 
            Boolean _DebitCard_InsufficientFund, Boolean _DebitCard_BSBDA_FI_001, Boolean _DebitCard_BSBDA_FI_002, Boolean _DebitCard_BSBDA_FI_003, 
            Boolean _DebitCard_BSBDA_FI_004, Boolean _DebitCard_mATM_Warning, Boolean _DebitCard_mATM_Final,Boolean _DebitCard_Offus_Highuse,Boolean _ReversalTran,
            Boolean _DebitCard_Incorrect_CVV,Boolean _bDebitCard_Merchant_Non_Complient,Boolean _bDebitCard_ExpiredCardECOM,Boolean _SIMotoTransactionAlert,Boolean _bDebitCardTxnContactless,Boolean _bMDES,
            Boolean _GPatATM,Boolean _GPG,Boolean _GPF,Boolean _GPOTPF,Boolean _GPOTPE, Boolean _NCMC1, Boolean _NCMC2, Boolean _NCMC3, Boolean _NCMC4, Boolean _NCMC5,Boolean _ACSOTPexceeded,
            ref SmppClientSession _s)
        {

            Data = msg;
            Counter = _c;
            PINChange = _p;
            CreditAlert = _d;
            smppClientSession = _s;
            C2CAlert = _c2c;
            FDAlert = _FD;
            WrongPIN = _WPIN;
            IntlZero = _IntZero;
            IntlOne = _IntOne;
            DoNotHonor = _DoNotHonor;
            FstPinTran01 = _FstPinTran01;
            TranNotPermited57 = _TranNotPermited57;
            bTransactionpreference = _Transactionpreference;
            bnon_metro_alert_triger = _bnon_metro_alert_triger;
            bnon_metro_alert_triger_2 = _bnon_metro_alert_triger_2;
            bmetro_alert_triger = _bmetro_alert_triger;
            bmetro_alert_triger_2 = _bmetro_alert_triger_2;
            bDebitCardDisableATMPOSECOMTran=_DebitCardDisableATMPOSECOMTran;
            bDebitCard_ExpiredCard = _DebitCard_ExpiredCard;
            bDebitCard_txn_timout=_DebitCard_txn_timout;
            bDebitCard_exceedLimit=_DebitCard_exceedLimit;
            bDebitCard_temblock=_DebitCard_temblock;
            bDebitCard_InsufficientFund = _DebitCard_InsufficientFund;
            bDebitCard_BSBDA_FI_001 = _DebitCard_BSBDA_FI_001;
            bDebitCard_BSBDA_FI_002 = _DebitCard_BSBDA_FI_002;
            bDebitCard_BSBDA_FI_003 = _DebitCard_BSBDA_FI_003;
            bDebitCard_BSBDA_FI_004 = _DebitCard_BSBDA_FI_004;
            bDebitCard_mATM_Warning = _DebitCard_mATM_Warning;
            DebitCard_mATM_Final = _DebitCard_mATM_Final;
            bDebitCard_Offus_Highuse = _DebitCard_Offus_Highuse;
            bReversalTran = _ReversalTran;
            bDebitCard_Incorrect_CVV=_DebitCard_Incorrect_CVV;
            bDebitCard_Merchant_Non_Complient = _bDebitCard_Merchant_Non_Complient;
            bDebitCard_ExpiredCardECOM = _bDebitCard_ExpiredCardECOM;
            SIMotoTransactionAlert = _SIMotoTransactionAlert;
            bDebitCardTxnContactless= _bDebitCardTxnContactless;
            bMDES= _bMDES;
            bGPatATM = _GPatATM;
            bGPG = _GPG;
            bGPF = _GPF;
            bGPOTPF = _GPOTPF;
            bGPOTPE = _GPOTPE;
            bNCMC1=_NCMC1;
            bNCMC2=_NCMC2;
            bNCMC3=_NCMC3;
            bNCMC4=_NCMC4;
            bNCMC5 = _NCMC5;
            bACSOTPexceeded= _ACSOTPexceeded;
        }

    }
}
