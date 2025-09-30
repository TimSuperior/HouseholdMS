using System;
using System.Globalization;
using System.IO;              // <-- NEW
using System.Windows;
using System.Windows.Controls;
using HouseholdMS.Model; // <-- use the one-file settings

namespace HouseholdMS.View
{
    /// <summary>
    /// Interaction logic for SettingMenuView.xaml
    /// </summary>
    public partial class SettingMenuView : UserControl
    {
        private readonly string _userRole;

        // ===== Language persistence (simple text file; no extra classes) =====
        private static readonly string LangFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HouseholdMS", "ui.language");

        private static string LoadLanguageOrDefault()
        {
            try
            {
                if (File.Exists(LangFilePath))
                {
                    var code = (File.ReadAllText(LangFilePath) ?? "").Trim().ToLowerInvariant();
                    if (code == "en" || code == "ko" || code == "es") return code;
                }
            }
            catch { /* ignore */ }
            return "en";
        }

        private static void SaveLanguage(string code)
        {
            try
            {
                var langDir = Path.GetDirectoryName(LangFilePath);
                if (!string.IsNullOrEmpty(langDir))
                {
                    Directory.CreateDirectory(langDir);
                }

                File.WriteAllText(LangFilePath, string.IsNullOrWhiteSpace(code) ? "en" : code.Trim().ToLowerInvariant());
            }
            catch { /* ignore */ }
        }
        // ====================================================================

        public SettingMenuView(string userRole)
        {
            InitializeComponent();
            _userRole = userRole;

            Loaded += SettingMenuView_Loaded;

            // Optional: restrict non-admins
            if (_userRole != "Admin")
            {
                FontPanel.IsEnabled = false;
                SaveButton.IsEnabled = false;
                if (LanguagePanel != null) LanguagePanel.IsEnabled = false; // <-- NEW (lock language too)

                MessageBox.Show(
                    "Only administrators can modify settings. (관리자만 설정을 변경할 수 있습니다.)",
                    "Access Restricted (접근 제한)",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void SettingMenuView_Loaded(object sender, RoutedEventArgs e)
        {
            // Initialize current scale into UI
            ScaleSlider.Value = AppTypographySettings.FontScale;
            SelectPresetClosestTo(AppTypographySettings.FontScale);

            // NEW: initialize language dropdown from saved value
            var current = LoadLanguageOrDefault();
            for (int i = 0; i < LangCombo.Items.Count; i++)
            {
                if (LangCombo.Items[i] is ComboBoxItem it &&
                    string.Equals(it.Tag?.ToString(), current, StringComparison.OrdinalIgnoreCase))
                {
                    LangCombo.SelectedIndex = i;
                    break;
                }
            }
        }

        // ======== Handlers ========

        private void PresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            if (PresetCombo.SelectedItem is ComboBoxItem it &&
                double.TryParse(it.Tag?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var s))
            {
                AppTypographySettings.Set(AppTypographySettings.BaseFontSize, s); // live-apply
                ScaleSlider.Value = s;                                            // reflect in slider
            }
        }

        private void ScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!IsLoaded) return;
            var s = Math.Round(e.NewValue, 2);
            AppTypographySettings.Set(AppTypographySettings.BaseFontSize, s); // live-apply
            SelectPresetClosestTo(s);
        }

        private void ResetDefaultBtn_Click(object sender, RoutedEventArgs e)
        {
            AppTypographySettings.Set(14.0, 1.00);
            ScaleSlider.Value = 1.00;
            SelectPresetClosestTo(1.00);
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 1) Save typography (existing)
                AppTypographySettings.Save();

                // 2) Save language (NEW)
                var selectedLang = (LangCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "en";
                SaveLanguage(selectedLang);

                // 3) Notify (slightly expanded text; behavior unchanged)
                MessageBox.Show(
                    "Saved. This text size will be used for all users on this machine. (모든 사용자에게 적용됩니다.)\n" +
                    "Language saved. Please restart the app to apply the new language. (언어 적용을 위해 재시작하세요.)",
                    "Saved",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Failed to save settings.\n" + ex.Message,
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        // ======== Helpers ========

        private void SelectPresetClosestTo(double scale)
        {
            int matchIndex = -1;
            for (int i = 0; i < PresetCombo.Items.Count; i++)
            {
                if (PresetCombo.Items[i] is ComboBoxItem it &&
                    double.TryParse(it.Tag?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var s) &&
                    Math.Abs(s - scale) < 0.01)
                {
                    matchIndex = i;
                    break;
                }
            }
            PresetCombo.SelectedIndex = matchIndex;
        }
    }
}
