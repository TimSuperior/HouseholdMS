using HouseholdMS.View;
using HouseholdMS.Properties;
using System;
using System.Windows;
using System.Windows.Controls;
using HouseholdMS.View.EqTesting;

namespace HouseholdMS
{
    public partial class MainWindow : Window
    {
        private readonly string _currentUserRole;

        public MainWindow(string userRole)
        {
            InitializeComponent();
            _currentUserRole = userRole;

            if (_currentUserRole == "Admin")
            {
                ManageUsersButton.Visibility = Visibility.Visible;
            }
            else
            {
                ManageUsersButton.Visibility = Visibility.Collapsed;
            }

            // 🔥 Pass role to HouseholdsView
            MainContent.Content = new HouseholdsView(_currentUserRole);
        }

        private void bt_HouseholdMenu(object sender, RoutedEventArgs e)
        {
            MainContent.Content = new HouseholdsView(_currentUserRole);
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
            MainContent.Content = new AllTestMenuView(_currentUserRole);
        }

        private void bt_BatteryTest_Click(object sender, RoutedEventArgs e)
        {
            MainContent.Content = new BatteryTestMenuView(_currentUserRole);
        }
    }
}
