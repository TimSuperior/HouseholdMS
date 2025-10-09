using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using HouseholdMS.Model;

namespace HouseholdMS.View
{
    public partial class SettingMenuView : UserControl
    {
        private readonly string _userRole;

        private static readonly string ProgramDataSettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "HouseholdMS", "settings.ini");

        private static readonly string UserTypographyPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HouseholdMS", "typography.user.ini");

        private static readonly string LangFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HouseholdMS", "ui.language");

        public SettingMenuView(string userRole)
        {
            InitializeComponent();
            _userRole = userRole;

            Loaded += SettingMenuView_Loaded;

            if (_userRole != "Admin")
            {
                FontPanel.IsEnabled = false;
                SaveButton.IsEnabled = false;
                if (LanguagePanel != null) LanguagePanel.IsEnabled = false;

                MessageBox.Show(
                    "Only administrators can modify settings. (관리자만 설정을 변경할 수 있습니다.)",
                    "Access Restricted (접근 제한)",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void SettingMenuView_Loaded(object sender, RoutedEventArgs e)
        {
            TryLoadPerUserTypography();

            // Initialize current scale into UI (no slider)
            UpdateScalePill(AppTypographySettings.FontScale);
            SelectPresetClosestTo(AppTypographySettings.FontScale);

            // Initialize language dropdown from saved value
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

        // ===== Language IO =====
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
            catch { }
            return "en";
        }

        private static void SaveLanguage(string code)
        {
            try
            {
                var dir = Path.GetDirectoryName(LangFilePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(LangFilePath, string.IsNullOrWhiteSpace(code) ? "en" : code.Trim().ToLowerInvariant());
            }
            catch { }
        }

        // ===== Handlers =====
        private void PresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            if (PresetCombo.SelectedItem is ComboBoxItem it &&
                double.TryParse(it.Tag?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var s))
            {
                ApplyScale(s);                 // live-apply
                UpdateScalePill(s);            // update pill text
                SelectPresetClosestTo(s);      // keep preset synced
            }
        }

        private void ResetDefaultBtn_Click(object sender, RoutedEventArgs e)
        {
            AppTypographySettings.Set(14.0, 1.00);
            UpdateScalePill(1.00);
            SelectPresetClosestTo(1.00);
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            bool savedForAllUsers = false;
            bool savedForCurrentUser = false;

            try
            {
                AppTypographySettings.Save(); // ProgramData (machine-wide)
                savedForAllUsers = true;
            }
            catch (UnauthorizedAccessException) { savedForCurrentUser = TrySavePerUserTypography(); }
            catch (IOException) { savedForCurrentUser = TrySavePerUserTypography(); }
            catch (CryptographicException) { savedForCurrentUser = TrySavePerUserTypography(); }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to save settings.\n" + ex.Message, "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var selectedLang = (LangCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "en";
            SaveLanguage(selectedLang);

            var msg = savedForAllUsers
                ? "Saved. Text size was persisted for all users on this PC. (모든 사용자에게 적용됩니다.)"
                : (savedForCurrentUser
                    ? "Saved for your Windows account (no admin rights on ProgramData)."
                    : "Saved language only. (Typography persistence skipped.)");

            MessageBox.Show(
                msg + Environment.NewLine +
                "Language saved. Please restart the app to apply the new language. (언어 적용을 위해 재시작하세요.)",
                "Saved",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        // ===== Helpers =====
        private void ApplyScale(double scale)
        {
            AppTypographySettings.Set(AppTypographySettings.BaseFontSize, scale);
        }

        private void UpdateScalePill(double scale)
        {
            if (ScaleValueText != null)
                ScaleValueText.Text = $"x{scale:F2}";
        }

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

        // ===== Per-user typography fallback =====
        private static string ComposeTypographyIni(double baseSize, double scale)
        {
            return $"BaseFontSize={baseSize.ToString(CultureInfo.InvariantCulture)}{Environment.NewLine}" +
                   $"FontScale={scale.ToString(CultureInfo.InvariantCulture)}{Environment.NewLine}";
        }

        private static bool TrySavePerUserTypography()
        {
            try
            {
                var dir = Path.GetDirectoryName(UserTypographyPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                var tmp = UserTypographyPath + ".tmp";
                File.WriteAllText(tmp, ComposeTypographyIni(AppTypographySettings.BaseFontSize, AppTypographySettings.FontScale), Encoding.UTF8);
                if (File.Exists(UserTypographyPath)) File.Delete(UserTypographyPath);
                File.Move(tmp, UserTypographyPath);
                return true;
            }
            catch { return false; }
        }

        private static void TryLoadPerUserTypography()
        {
            try
            {
                if (!File.Exists(UserTypographyPath)) return;

                double? baseSize = null;
                double? scale = null;

                foreach (var line in File.ReadAllLines(UserTypographyPath))
                {
                    var t = line.Trim();
                    if (string.IsNullOrWhiteSpace(t) || t.StartsWith("#") || t.StartsWith(";")) continue;
                    var idx = t.IndexOf('=');
                    if (idx <= 0) continue;

                    var key = t.Substring(0, idx).Trim();
                    var val = t.Substring(idx + 1).Trim();

                    if (key.Equals("BaseFontSize", StringComparison.OrdinalIgnoreCase) &&
                        double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var b))
                        baseSize = b;

                    if (key.Equals("FontScale", StringComparison.OrdinalIgnoreCase) &&
                        double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var s))
                        scale = s;
                }

                if (baseSize.HasValue || scale.HasValue)
                {
                    var b = baseSize ?? AppTypographySettings.BaseFontSize;
                    var s = scale ?? AppTypographySettings.FontScale;
                    AppTypographySettings.Set(b, s);
                }
            }
            catch { }
        }
    }
}
