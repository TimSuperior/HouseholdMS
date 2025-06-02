using System.Windows;
using System.Windows.Controls;

namespace HouseholdMS.View
{
    /// <summary>
    /// Interaction logic for SettingMenuView.xaml
    /// </summary>
    public partial class SettingMenuView : UserControl
    {
        private string userRole;

        public SettingMenuView(string userRole)
        {
            InitializeComponent();
            this.userRole = userRole;

            // Optionally restrict fields if not admin
            if (userRole != "Admin")
            {
                // You could disable or hide some settings here
                MessageBox.Show("Only administrators can modify settings. (관리자만 설정을 변경할 수 있습니다.)",
                                "Access Restricted (접근 제한)",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
            }
        }
    }
}