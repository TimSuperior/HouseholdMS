using HouseholdMS.View;
using HouseholdMS.Properties;
using System;
using System.Windows;
using System.Windows.Controls;
using HouseholdMS.View.EqTesting;
using HouseholdMS.View.Measurement;
using HouseholdMS.View.Dashboard;

namespace HouseholdMS
{
    public partial class MainWindow : Window
    {
        private readonly string _currentUserRole;

        public MainWindow(string userRole)
        {
            InitializeComponent();
            _currentUserRole = userRole;

            // Show role-specific buttons
            if (_currentUserRole == "Admin" || _currentUserRole == "Technician")
            {
                bt_AllTest.Visibility = Visibility.Visible;
                bt_BatteryTest.Visibility = Visibility.Visible;
                bt_ControllerTest.Visibility = Visibility.Visible;
                bt_InverterTest.Visibility = Visibility.Visible;
                bt_SwitchTest.Visibility = Visibility.Visible;
                bt_TestReports.Visibility = Visibility.Visible;

                if (_currentUserRole == "Admin")
                {
                    bt_SettingMenu.Visibility = Visibility.Visible;
                    ManageUsersButton.Visibility = Visibility.Visible;
                }
            }
            else
            {
                bt_AllTest.Visibility = Visibility.Collapsed;
                bt_BatteryTest.Visibility = Visibility.Collapsed;
                bt_ControllerTest.Visibility = Visibility.Collapsed;
                bt_InverterTest.Visibility = Visibility.Collapsed;
                bt_SwitchTest.Visibility = Visibility.Collapsed;
                bt_SettingMenu.Visibility = Visibility.Collapsed;
                bt_TestReports.Visibility = Visibility.Collapsed;
                ManageUsersButton.Visibility = Visibility.Collapsed;
            }

            // Default content
            MainContent.Content = new DashboardView(_currentUserRole);
        }

        // NEW: allow child views (e.g., DashboardView) to navigate MainContent
        public void NavigateTo(UserControl view)
        {
            MainContent.Content = view;
        }

        private void bt_HouseholdMenu(object sender, RoutedEventArgs e)
        {
            MainContent.Content = new HouseholdsView(_currentUserRole);
        }

        private void bt_SiteMenu(object sender, RoutedEventArgs e)
        {
            MainContent.Content = new SitesView(_currentUserRole);
        }

        private void bt_TechnicianMenu(object sender, RoutedEventArgs e)
        {
            MainContent.Content = new TechView(_currentUserRole);
        }

        private void bt_InventoryMenu(object sender, RoutedEventArgs e)
        {
            MainContent.Content = new InventoryView(_currentUserRole);
        }

        private void bt_ServiceMenu(object sender, RoutedEventArgs e)
        {
            MainContent.Content = new ServiceRecordsView(_currentUserRole);
        }

        private void bt_TestReports_Click(object sender, RoutedEventArgs e)
        {
            MainContent.Content = new TestReportsView(_currentUserRole);
        }

        private void bt_ManageUsers(object sender, RoutedEventArgs e)
        {
            MainContent.Content = new UserManagementView();
        }

        private void Logout_Click(object sender, RoutedEventArgs e)
        {
            var loginWindow = new Login();
            loginWindow.Show();
            this.Close();
        }

        private void bt_AllTest_Click(object sender, RoutedEventArgs e)
        {
            var view = new AllTestMenuView(_currentUserRole);
            view.CloseRequested += AllTest_CloseRequested;
            MainContent.Content = view;
        }

        private void bt_BatteryTest_Click(object sender, RoutedEventArgs e)
        {
            MainContent.Content = new HouseholdMS.View.UserControls.EpeverMonitorControl();
        }


        private void bt_ControllerTest_Click(object sender, RoutedEventArgs e)
        {
            MainContent.Content = new ControllerTestMenuView(_currentUserRole);
        }

        private void bt_InverterTest_Click(object sender, RoutedEventArgs e)
        {
            MainContent.Content = new InverterTestMenuView(_currentUserRole);
        }

        private void bt_SwitchTest_Click(object sender, RoutedEventArgs e)
        {
            MainContent.Content = new SwitchTestMenuView(_currentUserRole);
        }

        private void bt_SettingMenu_Click(object sender, RoutedEventArgs e)
        {
            MainContent.Content = new SettingMenuView(_currentUserRole);
        }

        private void bt_MeasurementMenu(object sender, RoutedEventArgs e)
        {
            MainContent.Content = new MeasurementView();
        }

        private void bt_OscilloscopeMenu(object sender, RoutedEventArgs e)
        {
            MainContent.Content = new HouseholdMS.View.Measurement.OscilloscopeView();
        }

        private void bt_Template_Click(object sender, RoutedEventArgs e)
        {
            MainContent.Content = new TemplateView();
        }

        private void bt_Dashboard_Click(object sender, RoutedEventArgs e)
        {
            MainContent.Content = new DashboardView(_currentUserRole);
        }

        private void AllTest_CloseRequested(object sender, EventArgs e)
        {
            // Example post-close navigation; keep whichever you prefer:
            MainContent.Content = new TestReportsView(_currentUserRole);
            MainContent.Content = new HouseholdsView(_currentUserRole);
        }
    }
}
