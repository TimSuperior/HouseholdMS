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
                bt_TestProcedure.Visibility = Visibility.Visible;
                bt_SwitchTest.Visibility = Visibility.Visible;
                bt_TestReports.Visibility = Visibility.Visible;

                if (isAdmin)
                {
                    bt_SettingMenu.Visibility = Visibility.Visible;   // top-nav settings button
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
                bt_TestProcedure.Visibility = Visibility.Collapsed;
                bt_SwitchTest.Visibility = Visibility.Collapsed;
                bt_SettingMenu.Visibility = Visibility.Collapsed; // top-nav
                bt_TestReports.Visibility = Visibility.Collapsed;
                NavManageUsersBtn.Visibility = Visibility.Collapsed;
            }

            // Start with Dashboard panel expanded (others collapsed) and show Dashboard view
            ExpandOnly(Panel_Dashboard);
            MainContent.Content = new DashboardView(_currentUserRole);
        }

        /* --------- Accordion helpers --------- */
        private void ExpandOnly(UIElement targetPanel)
        {
            if (Panel_Dashboard != null) Panel_Dashboard.Visibility = targetPanel == Panel_Dashboard ? Visibility.Visible : Visibility.Collapsed;
            if (Panel_Monitoring != null) Panel_Monitoring.Visibility = targetPanel == Panel_Monitoring ? Visibility.Visible : Visibility.Collapsed;
            if (Panel_TestManuals != null) Panel_TestManuals.Visibility = targetPanel == Panel_TestManuals ? Visibility.Visible : Visibility.Collapsed;
        }

        /* --------- Header (section) clicks --------- */
        // Keep original behavior (navigate to Dashboard) AND expand its submenu
        private void bt_Dashboard_Click(object sender, RoutedEventArgs e)
        {
            ExpandOnly(Panel_Dashboard);
            MainContent.Content = new DashboardView(_currentUserRole);
        }

        private void Header_Monitoring_Click(object sender, RoutedEventArgs e)
        {
            ExpandOnly(Panel_Monitoring);
        }

        private void Header_TestManuals_Click(object sender, RoutedEventArgs e)
        {
            ExpandOnly(Panel_TestManuals);
        }

        /* --------- Submenu navigations (unchanged) --------- */
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
            var view = new AllTestMenuView();
            MainContent.Content = view;
        }

        private void bt_BatteryTest_Click(object sender, RoutedEventArgs e) => MainContent.Content = new HouseholdMS.View.UserControls.EpeverMonitorControl();
        private void bt_ControllerTest_Click(object sender, RoutedEventArgs e) => MainContent.Content = new ControllerTestMenuView();

        private void bt_SwitchTest_Click(object sender, RoutedEventArgs e) => MainContent.Content = new SwitchTestMenuView();
        private void bt_SettingMenu_Click(object sender, RoutedEventArgs e) => MainContent.Content = new SettingMenuView(_currentUserRole);
        private void bt_MeasurementMenu(object sender, RoutedEventArgs e) => MainContent.Content = new MeasurementView();
        private void bt_OscilloscopeMenu(object sender, RoutedEventArgs e) => MainContent.Content = new HouseholdMS.View.Measurement.Tbs1000cView();

        private void bt_Template_Click(object sender, RoutedEventArgs e) => MainContent.Content = new TemplateView();

        private void AllTest_CloseRequested(object sender, EventArgs e)
        {
            // Your original behavior (note: this replaces content twice, last wins)
            MainContent.Content = new TestReportsView(_currentUserRole);
            MainContent.Content = new SitesView(_currentUserRole);
        }

        private void Button_Click_ElectronicLoad(object sender, RoutedEventArgs e)
        {
            MainContent.Content = new It8615Control();
        }

        private void bt_TestProcedure_Click(object sender, RoutedEventArgs e)
        {
            MainContent.Content = new TestProcedure();
        }
    }
}
