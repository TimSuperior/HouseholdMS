using HouseholdMS.Model;     // AppTypographySettings
using Syncfusion.Licensing;
using System;
using System.Windows;
using System.Globalization;   // NEW
using System.IO;              // NEW
using System.Threading;       // NEW
using System.Windows.Markup;  // NEW
using System.Windows.Media;   // NEW (for FontFamily fallback)


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
            // Load saved base + scale; publishes AppUiScale and font resources
            AppTypographySettings.Load();

            /* ===== APPLY SAVED UI LANGUAGE (no helper files) ===== */
            string lang = "en";
            try
            {
                var path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "HouseholdMS", "ui.language");
                if (File.Exists(path))
                {
                    var tmp = (File.ReadAllText(path) ?? "").Trim().ToLowerInvariant();
                    if (tmp == "en" || tmp == "ko" || tmp == "es") lang = tmp;
                }
            }
            catch { /* ignore; fallback to en */ }

            var culture = new CultureInfo(lang);
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;

            // Ensure WPF binds correct language/formatting
            FrameworkElement.LanguageProperty.OverrideMetadata(
                typeof(FrameworkElement),
                new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(culture.IetfLanguageTag)));

            // Optional: Hangul font fallback for Korean
            if (lang == "ko")
            {
                // Your App.xaml defines DynamicResource AppFontFamily — override with KR-friendly family
                Resources["AppFontFamily"] = new FontFamily("Noto Sans KR, Malgun Gothic, Segoe UI");
            }
            /* ===== END language apply ===== */

            // (Your existing DB connection check and Login window code stay the same)


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
