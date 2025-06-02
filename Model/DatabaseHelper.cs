using System;
using System.Configuration;
using System.Data.SqlClient;

namespace HouseholdMS.Model // ✅ Adjust this if your actual namespace differs
{
    public static class DatabaseHelper
    {
        private static readonly string connectionString;

        static DatabaseHelper()
        {
            connectionString = ConfigurationManager.ConnectionStrings["AppDb"]?.ConnectionString;

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException("Connection string 'AppDb' not found in App.config.");
            }
        }

        public static SqlConnection GetConnection()
        {
            return new SqlConnection(connectionString);
        }

        public static string GetConnectionString()
        {
            return connectionString;
        }

        public static bool TestConnection()
        {
            try
            {
                using (var conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
