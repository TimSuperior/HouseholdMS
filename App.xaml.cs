using System.Windows;
using HouseholdMS.Database; // 👈 Import your DatabaseInitializer namespace

namespace HouseholdMS
{
    public partial class App : Application
    {
        private void Application_Startup(object sender, StartupEventArgs e)
        {
            // ✅ Initialize the database on app startup
            DatabaseInitializer.Initialize();

            // ✅ After DB is ready, open login window
            var loginWindow = new View.Login();
            loginWindow.Show();
        }
    }
}
