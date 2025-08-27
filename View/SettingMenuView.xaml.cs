using System;
using System.Globalization;
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
                AppTypographySettings.Save();
                MessageBox.Show(
                    "Saved. This text size will be used for all users on this machine. (모든 사용자에게 적용됩니다.)",
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
