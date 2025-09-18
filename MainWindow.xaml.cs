// MainWindow.xaml.cs
using HouseholdMS.Controls;
using HouseholdMS.View;
using HouseholdMS.View.Dashboard;
using HouseholdMS.View.EqTesting;
using HouseholdMS.View.Measurement;
using System;
using System.Windows;
using System.Windows.Controls;

namespace HouseholdMS
{
    public partial class MainWindow : Window
    {
        private readonly string _currentUserRole;
        private readonly string _currentUsername;

        // Expose for navbar bindings
        public string CurrentUserRole => _currentUserRole;
        public string CurrentUsername => string.IsNullOrWhiteSpace(_currentUsername) ? "Guest" : _currentUsername;

        public MainWindow(string userRole, string username)
        {
            InitializeComponent();

            _currentUserRole = (userRole ?? string.Empty).Trim();
            _currentUsername = username ?? string.Empty;

            // Bind navbar text to these properties
            DataContext = this;

            bool isAdmin = string.Equals(_currentUserRole, "Admin", StringComparison.OrdinalIgnoreCase);
            bool isTech = string.Equals(_currentUserRole, "Technician", StringComparison.OrdinalIgnoreCase);

            if (isAdmin || isTech)
            {
                bt_AllTest.Visibility = Visibility.Visible;
                bt_BatteryTest.Visibility = Visibility.Visible;
                bt_ControllerTest.Visibility = Visibility.Visible;
                bt_InverterTest.Visibility = Visibility.Visible;
                bt_SwitchTest.Visibility = Visibility.Visible;
                bt_TestReports.Visibility = Visibility.Visible;

                if (isAdmin)
                {
                    bt_SettingMenu.Visibility = Visibility.Visible;   // now top-nav settings button
                    NavManageUsersBtn.Visibility = Visibility.Visible;
                }
                else
                {
                    bt_SettingMenu.Visibility = Visibility.Collapsed;
                    NavManageUsersBtn.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                bt_AllTest.Visibility = Visibility.Collapsed;
                bt_BatteryTest.Visibility = Visibility.Collapsed;
                bt_ControllerTest.Visibility = Visibility.Collapsed;
                bt_InverterTest.Visibility = Visibility.Collapsed;
                bt_SwitchTest.Visibility = Visibility.Collapsed;
                bt_SettingMenu.Visibility = Visibility.Collapsed; // top-nav
                bt_TestReports.Visibility = Visibility.Collapsed;
                NavManageUsersBtn.Visibility = Visibility.Collapsed;
            }

            MainContent.Content = new DashboardView(_currentUserRole);
        }

        public void NavigateTo(UserControl view) => MainContent.Content = view;

        private void bt_SiteMenu(object sender, RoutedEventArgs e) => MainContent.Content = new SitesView(_currentUserRole);
        private void bt_InventoryMenu(object sender, RoutedEventArgs e) => MainContent.Content = new InventoryView(_currentUserRole);
        private void bt_ServiceMenu(object sender, RoutedEventArgs e) => MainContent.Content = new ServiceRecordsView(_currentUserRole);
        private void bt_TestReports_Click(object sender, RoutedEventArgs e) => MainContent.Content = new TestReportsView(_currentUserRole);

        private void bt_ManageUsers(object sender, RoutedEventArgs e)
        {
            MainContent.Content = new HouseholdMS.View.UserManagementView(_currentUserRole, _currentUsername);
        }

        private void Logout_Click(object sender, RoutedEventArgs e)
        {
            var loginWindow = new Login();
            loginWindow.Show();
            Close();
        }

        private void bt_AllTest_Click(object sender, RoutedEventArgs e)
        {
            var view = new AllTestMenuView(_currentUserRole);
            view.CloseRequested += AllTest_CloseRequested;
            MainContent.Content = view;
        }

        private void bt_BatteryTest_Click(object sender, RoutedEventArgs e) => MainContent.Content = new HouseholdMS.View.UserControls.EpeverMonitorControl();
        private void bt_ControllerTest_Click(object sender, RoutedEventArgs e) => MainContent.Content = new ControllerTestMenuView(_currentUserRole);
        private void bt_InverterTest_Click(object sender, RoutedEventArgs e) => MainContent.Content = new InverterTestMenuView(_currentUserRole);
        private void bt_SwitchTest_Click(object sender, RoutedEventArgs e) => MainContent.Content = new SwitchTestMenuView(_currentUserRole);
        private void bt_SettingMenu_Click(object sender, RoutedEventArgs e) => MainContent.Content = new SettingMenuView(_currentUserRole);
        private void bt_MeasurementMenu(object sender, RoutedEventArgs e) => MainContent.Content = new MeasurementView();
        private void bt_OscilloscopeMenu(object sender, RoutedEventArgs e) => MainContent.Content = new HouseholdMS.View.Measurement.Tbs1000cView();

        private void bt_ElectronicLoadMenu(object sender, RoutedEventArgs e) => MainContent.Content = new ElectronicLoadIT8615View();

        private void bt_Template_Click(object sender, RoutedEventArgs e) => MainContent.Content = new TemplateView();
        private void bt_Dashboard_Click(object sender, RoutedEventArgs e) => MainContent.Content = new DashboardView(_currentUserRole);

        private void AllTest_CloseRequested(object sender, EventArgs e)
        {
            MainContent.Content = new TestReportsView(_currentUserRole);
            MainContent.Content = new SitesView(_currentUserRole);
        }

        private void Button_Click_ElectronicLoad(object sender, RoutedEventArgs e)
        {
            MainContent.Content = new It8615Control();
        }
    }
}
