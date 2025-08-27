using HouseholdMS.Model;     // AppTypographySettings
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
            // Load saved base + scale; publishes AppUiScale and font resources
            AppTypographySettings.Load();

            try
            {
                using (var conn = DatabaseHelper.GetConnection()) { conn.Open(); }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "❌ Cannot open the SQLite database.\n\n" +
                    $"Connection: {DatabaseHelper.GetConnectionString()}\n\n" +
                    ex.ToString(),
                    "DB Connection Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);

                Shutdown();
                return;
            }

            var loginWindow = new View.Login();
            loginWindow.Show();
        }
    }
}
