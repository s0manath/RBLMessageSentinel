using System.Data.SqlClient;
using System;
using Microsoft.Data.SqlClient;

namespace ReflectionIT.Common.Data.Configuration
{
	/// <summary>
	/// Summary description for ConfigManager.
	/// </summary>
	class ConfigManager {

        public static SqlConnection GetPortalSQLDBConnection
        {
            get { return new SqlConnection("Server=172.16.23.38,4759; Database=ismart;Integrated Security=True;TrustServerCertificate=true;"); }
            //get { return new SqlConnection(System.Configuration.ConfigurationManager.ConnectionStrings["GetPortalSQLDBConnection"].ToString()); }
        }

        public static SqlConnection GetRTSQLDBConnection
        {
            get { return new SqlConnection("Server=172.16.23.38,4759; Database=ismart;Integrated Security=True;TrustServerCertificate=true;"); }

            //get { return new SqlConnection(System.Configuration.ConfigurationManager.ConnectionStrings["GetRTSQLDBConnection"].ToString()); }
        }

        public static SqlConnection GetRBLALERTSQLDBConnection
        {   //SIT
            //get { return new SqlConnection("Server=172.16.26.38,4759; Database=ismart;User Id=rblcontroller; Password=Rbl@admin@2024;TrustServerCertificate=true;"); }

            //DC
            //get { return new SqlConnection("Server=172.16.29.103,4760; Database=ismart;User Id=rblappuser; Password=VV_Aq~ywQz35;TrustServerCertificate=true;"); }

            //DR
            //get { return new SqlConnection("Server=172.16.25.103,4760; Database=ismart;User Id=rblappuser; Password=VV_Aq~ywQz35;TrustServerCertificate=true;"); }

            get { return new SqlConnection(System.Configuration.ConfigurationManager.ConnectionStrings["RBLALERTSQLDBConnection"].ToString()); }
        }
    }
}
