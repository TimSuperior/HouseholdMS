using HouseholdMS.View;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data.SQLite;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;


namespace HouseholdMS
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private ObservableCollection<Household> households = new ObservableCollection<Household>();
        public MainWindow()
        {

            InitializeComponent();
            MainContent.Content = new HouseholdsView();
        }

        

        private void bt_HouseholdMenu(object sender, RoutedEventArgs e)
        {
            MainContent.Content = new HouseholdsView();
        }

        private void bt_TechnicianMenu(object sender, RoutedEventArgs e)
        {
            MainContent.Content = new TechView();
        }

        private void bt_InventoryMenu(object sender, RoutedEventArgs e)
        {
            MainContent.Content = new InventoryView();
        }

        private void bt_ServiceMenu(object sender, RoutedEventArgs e)
        {
            MainContent.Content = new ServiceRecordsView();
        }

        private void Logout_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("You have been logged out.");
            Application.Current.Shutdown(); // or redirect to login page
        }

    }
}
