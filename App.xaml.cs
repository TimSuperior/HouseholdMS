using System;
using System.Windows;
using HouseholdMS.Model;


namespace HouseholdMS
{
    public partial class App : Application
    {
        private void Application_Startup(object sender, StartupEventArgs e)
        {
            // ✅ Optional: Test connection to SQL Server
            try
            {
                if (!DatabaseHelper.TestConnection())
                {
                    MessageBox.Show("❌ Cannot connect to the database. Please check SQL Server configuration.", "DB Connection Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    Shutdown(); // Exit app
                    return;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Unexpected error during DB connection:\n" + ex.Message);
                Shutdown();
                return;
            }

            // ✅ Start app
            var loginWindow = new View.Login();
            loginWindow.Show();
        }
    }
}
