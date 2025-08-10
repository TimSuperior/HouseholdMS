using HouseholdMS.Model;
using Syncfusion.Licensing;
using System;
using System.Windows;


namespace HouseholdMS
{
    public partial class App : Application
    {

        public App()
        {
            SyncfusionLicenseProvider.RegisterLicense("Ngo9BigBOggjHTQxAR8/V1JEaF5cWWFCf1FpRmJGdld5fUVHYVZUTXxaS00DNHVRdkdmWXdednZUR2dYVEByWUZWYEk=");
        }
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
