using ReflectionIT.Common.Data.Configuration;
using ReflectionIT.Common.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RBLMessageSentinel
{
    public static class Log
    {
        static string Logpath = System.Configuration.ConfigurationManager.AppSettings["Log"].ToString();

        public static bool isBlocked = false;

        // Two log files only: alertLog__ (everything) and Errorlog_ (errors). Lock: alerts are processed on several threads.
        private static readonly object logLock = new object();

        private static void Append(string prefix, string Text)
        {
            string sFileName = Logpath + "\\" + prefix + DateTime.Now.ToString("yyyyMMddHH") + ".log";
            try
            {
                lock (logLock)
                {
                    File.AppendAllText(sFileName, Text + Environment.NewLine);
                }
            }
            catch (Exception xObj)
            {
            }
        }

        public static void Write(string Text)
        {
            Append("alertLog__", Text);
        }

        public static void Writerrorlog(string Text)
        {
            Append("Errorlog_", Text);
        }

        // ---------------------------------------------------------------- masking (no customer data in clear text in log files)

        private static readonly System.Text.RegularExpressions.Regex sensitiveTags = new System.Text.RegularExpressions.Regex(
            @"<(MobNo|EmailId|From_Acc_No|To_Acc_No|custID|Balance|mobileNo|emailID|accountNo|customerID)>([^<]*)</\1>",
            System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        /// <summary>Mobile / account / id: only the last 4 characters shown. Email: first letter + domain.</summary>
        public static string Mask(string value)
        {
            if (string.IsNullOrEmpty(value)) return value ?? "";
            int at = value.IndexOf('@');
            if (at > 0) return value.Substring(0, 1) + "XXXX" + value.Substring(at);
            return value.Length <= 4 ? new string('X', value.Length) : new string('X', value.Length - 4) + value.Substring(value.Length - 4);
        }

        /// <summary>Raw &lt;Alert&gt; or &lt;sendMessage&gt; XML with mobile, email, account, customer id and balance masked.</summary>
        public static string MaskXml(string xml)
        {
            if (string.IsNullOrEmpty(xml)) return xml ?? "";
            return sensitiveTags.Replace(xml, m => "<" + m.Groups[1].Value + ">" + Mask(m.Groups[2].Value) + "</" + m.Groups[1].Value + ">");
        }

        public static void InsertRBLTransLog(clsLog objclsLog)
        {
            try
            {
                using (SqlStoredProcedure sspObj = new SqlStoredProcedure("dbo.usp_RBLSMSLog", ConfigManager.GetRBLALERTSQLDBConnection, System.Data.CommandType.StoredProcedure))
                {                    
                    sspObj.AddParameterWithValue("@clNo", SqlDbType.VarChar, 200, ParameterDirection.Input, objclsLog.clNum ?? "");
                    sspObj.AddParameterWithValue("@SendMsg", SqlDbType.VarChar, 0, ParameterDirection.Input, objclsLog.sSendMsg ?? "");
                    sspObj.AddParameterWithValue("@SMSResponse", SqlDbType.VarChar, 0, ParameterDirection.Input, objclsLog.sResponse ?? "");
                    sspObj.AddParameterWithValue("@logtype", SqlDbType.VarChar, 200, ParameterDirection.Input, objclsLog.logtype ?? "");
                    sspObj.AddParameterWithValue("@trantype", SqlDbType.VarChar, 200, ParameterDirection.Input, objclsLog.trantype ?? "");
                    sspObj.AddParameterWithValue("@RespCode", SqlDbType.VarChar, 200, ParameterDirection.Input, objclsLog.RespCode ?? "");
                    sspObj.AddParameterWithValue("@MobNo", SqlDbType.VarChar, 200, ParameterDirection.Input, objclsLog.MobNo+"|"+objclsLog.sSendMsg ?? "");
                    if (objclsLog.ReqDatetime > DateTime.Now.AddHours(-1))
                    {
                        sspObj.AddParameterWithValue("@ReqDateTime", SqlDbType.DateTime, 200, ParameterDirection.Input, objclsLog.ReqDatetime);
                    }
                    if (objclsLog.ResDatetime > DateTime.Now.AddHours(-1))
                    {
                        sspObj.AddParameterWithValue("@ResDateTime", SqlDbType.DateTime, 200, ParameterDirection.Input, objclsLog.ResDatetime);
                    }
                    sspObj.AddParameterWithValue("@Reserv2", SqlDbType.VarChar, 200, ParameterDirection.Input, objclsLog.Reserv2 ?? "");
                    sspObj.AddParameterWithValue("@CustId", SqlDbType.VarChar, 200, ParameterDirection.Input, objclsLog.CustId ?? "");
                    sspObj.AddParameterWithValue("@EmailId", SqlDbType.VarChar, 200, ParameterDirection.Input, objclsLog.EmailId ?? "");
                    sspObj.ExecuteNonQuery();
                    sspObj.Dispose();
                }
            }
            catch (Exception ex)
            {
                Log.Writerrorlog(DateTime.Now.ToString() + " >> DB log Err #" + objclsLog.clNum + " : usp_RBLSMSLog (" + objclsLog.logtype + ") : " + ex.Message);
                objclsLog.logtype = "ERROR";
                objclsLog.ErrorMsg = ex.ToString();
                InsertRBLTransErrorLog(objclsLog);
            }
        }

        public static void InsertRBLTransErrorLog(clsLog objclsLog)
        {
            try
            {
                using (SqlStoredProcedure sspObj = new SqlStoredProcedure("dbo.usp_RBLSMSLog", ConfigManager.GetRBLALERTSQLDBConnection, System.Data.CommandType.StoredProcedure))
                {
                    sspObj.AddParameterWithValue("@clNo", SqlDbType.VarChar, 200, ParameterDirection.Input, objclsLog.clNum ?? "");
                    sspObj.AddParameterWithValue("@SendMsg", SqlDbType.VarChar, 0, ParameterDirection.Input, objclsLog.sSendMsg ?? "");
                    // usp_RBLSMSLog has no error column: the error (first line, max 500 chars) goes into SMSResponse
                    string errorText = (objclsLog.ErrorMsg ?? "").Split('\n')[0].Trim();
                    if (errorText.Length > 500) errorText = errorText.Substring(0, 500);
                    sspObj.AddParameterWithValue("@SMSResponse", SqlDbType.VarChar, 0, ParameterDirection.Input, string.IsNullOrEmpty(objclsLog.sResponse) ? errorText : objclsLog.sResponse);
                    sspObj.AddParameterWithValue("@logtype", SqlDbType.VarChar, 200, ParameterDirection.Input, objclsLog.logtype ?? "");
                    sspObj.AddParameterWithValue("@trantype", SqlDbType.VarChar, 200, ParameterDirection.Input, objclsLog.trantype ?? "");
                    sspObj.AddParameterWithValue("@RespCode", SqlDbType.VarChar, 200, ParameterDirection.Input, objclsLog.RespCode ?? "");
                    sspObj.AddParameterWithValue("@MobNo", SqlDbType.VarChar, 200, ParameterDirection.Input, objclsLog.MobNo ?? "");

                    if (objclsLog.ReqDatetime > DateTime.Now.AddHours(-1))
                    {
                        sspObj.AddParameterWithValue("@ReqDateTime", SqlDbType.DateTime, 200, ParameterDirection.Input, objclsLog.ReqDatetime);
                    }
                    if (objclsLog.ResDatetime > DateTime.Now.AddHours(-1))
                    {
                        sspObj.AddParameterWithValue("@ResDateTime", SqlDbType.DateTime, 200, ParameterDirection.Input, objclsLog.ResDatetime);
                    }
                    sspObj.AddParameterWithValue("@Reserv2", SqlDbType.VarChar, 200, ParameterDirection.Input, objclsLog.Reserv2 ?? "");
                    sspObj.AddParameterWithValue("@CustId", SqlDbType.VarChar, 200, ParameterDirection.Input, objclsLog.CustId ?? "");
                    sspObj.AddParameterWithValue("@EmailId", SqlDbType.VarChar, 200, ParameterDirection.Input, objclsLog.EmailId ?? "");
                    sspObj.ExecuteNonQuery();
                    sspObj.Dispose();
                }
            }
            catch (Exception ex)
            {
                Log.Writerrorlog(DateTime.Now.ToString() + " >> DB log Err #" + objclsLog.clNum + " : usp_RBLSMSLog (ERROR row) : " + ex.Message);
            }
        }

        public static void InsertRBLSMSLISTENERREQUEST(string CLSNO, string TRANTYPE, string RSPCODE, string AMOUNT, string CARDNO, string DATETIME, string LOC, string MOBILENO, string EMAILID, string FDACC, string FDAMT, string ACCOUNTNO, string BALANCE, string INTLCUR, string INTLAMT, string Reserv1, string Reserv2, string Reserv3, string Reserv4, string RRN)
        {         
            try
            {
                using (SqlStoredProcedure sspObj = new SqlStoredProcedure("dbo.USP_LOG_RBLSMSLISTENERREQUEST", ConfigManager.GetRBLALERTSQLDBConnection, System.Data.CommandType.StoredProcedure))
                {
                    sspObj.AddParameterWithValue("@CLSNO", SqlDbType.VarChar, 200, ParameterDirection.Input, CLSNO);
                    sspObj.AddParameterWithValue("@TRANTYPE", SqlDbType.VarChar, 200, ParameterDirection.Input, TRANTYPE);
                    sspObj.AddParameterWithValue("@RSPCODE", SqlDbType.VarChar, 0, ParameterDirection.Input, RSPCODE);
                    sspObj.AddParameterWithValue("@AMOUNT", SqlDbType.VarChar, 0, ParameterDirection.Input, AMOUNT);
                    sspObj.AddParameterWithValue("@CARDNO", SqlDbType.VarChar, 200, ParameterDirection.Input, CARDNO);
                    sspObj.AddParameterWithValue("@DATETIME", SqlDbType.VarChar, 200, ParameterDirection.Input, DATETIME);
                    sspObj.AddParameterWithValue("@LOC", SqlDbType.VarChar, 200, ParameterDirection.Input, LOC);
                    sspObj.AddParameterWithValue("@MOBILENO", SqlDbType.VarChar, 200, ParameterDirection.Input, MOBILENO);
                    sspObj.AddParameterWithValue("@EMAILID", SqlDbType.VarChar, 200, ParameterDirection.Input, EMAILID);
                    sspObj.AddParameterWithValue("@FDACC", SqlDbType.VarChar, 0, ParameterDirection.Input, FDACC);
                    sspObj.AddParameterWithValue("@FDAMT", SqlDbType.VarChar, 0, ParameterDirection.Input, FDAMT);
                    sspObj.AddParameterWithValue("@ACCOUNTNO", SqlDbType.VarChar, 200, ParameterDirection.Input, ACCOUNTNO);
                    sspObj.AddParameterWithValue("@BALANCE", SqlDbType.VarChar, 200, ParameterDirection.Input, BALANCE);
                    sspObj.AddParameterWithValue("@INTLCUR", SqlDbType.VarChar, 200, ParameterDirection.Input, INTLCUR);
                    sspObj.AddParameterWithValue("@INTLAMT", SqlDbType.VarChar, 200, ParameterDirection.Input, INTLAMT);
                    sspObj.AddParameterWithValue("@Reserv1", SqlDbType.VarChar, 200, ParameterDirection.Input, Reserv1);
                    sspObj.AddParameterWithValue("@Reserv2", SqlDbType.VarChar, 0, ParameterDirection.Input, Reserv2);
                    sspObj.AddParameterWithValue("@Reserv3", SqlDbType.VarChar, 0, ParameterDirection.Input, Reserv3);
                    sspObj.AddParameterWithValue("@Reserv4", SqlDbType.VarChar, 200, ParameterDirection.Input, Reserv4);
                    sspObj.AddParameterWithValue("@RRN", SqlDbType.VarChar, 200, ParameterDirection.Input, RRN);
                    sspObj.ExecuteNonQuery();
                    sspObj.Dispose();
                }
            }
            catch (Exception ex)
            {
                Log.Writerrorlog(DateTime.Now.ToString() + " >> DB log Err #" + CLSNO + " : USP_LOG_RBLSMSLISTENERREQUEST : " + ex.Message);
            }
        }

        public static bool CheckMarkUpNotificationStatus(string CIFID, string MOBILENO, string TRANTYPE, string RSPCODE, string CLSNO)
        {
            bool status = false;
            try
            {
                using (SqlStoredProcedure sspObj = new SqlStoredProcedure("dbo.USP_CHECK_MARKUP_NOTIFICATION", ConfigManager.GetRBLALERTSQLDBConnection, System.Data.CommandType.StoredProcedure))
                {
                    sspObj.AddParameterWithValue("@CIFID", SqlDbType.VarChar, 200, ParameterDirection.Input, CIFID);
                    sspObj.AddParameterWithValue("@MOBILENO", SqlDbType.VarChar, 200, ParameterDirection.Input, MOBILENO);
                    sspObj.AddParameterWithValue("@TRANTYPE", SqlDbType.VarChar, 200, ParameterDirection.Input, TRANTYPE);
                    sspObj.AddParameterWithValue("@RSPCODE", SqlDbType.VarChar, 200, ParameterDirection.Input, RSPCODE);
                    status = Convert.ToBoolean(sspObj.ExecuteScalar());
                    sspObj.Dispose();
                }
            }
            catch (Exception ex)
            {
                status = false;
                Log.Writerrorlog(DateTime.Now.ToString() + " >> DB Err #" + CLSNO + " : USP_CHECK_MARKUP_NOTIFICATION : " + ex.Message);
            }
            return status;
        }
    }
}
