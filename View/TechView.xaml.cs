using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using HouseholdMS.View.UserControls;
using HouseholdMS.Model;
using System.Data.SQLite; // <-- Use SQLite

namespace HouseholdMS.View
{
    public partial class TechView : UserControl
    {
        public ObservableCollection<Technician> technicianList = new ObservableCollection<Technician>();
        private readonly string _currentUserRole;

        public TechView(string userRole = "User")
        {
            InitializeComponent();
            _currentUserRole = userRole;
            LoadTechnicians();
            ApplyRoleRestrictions();

            listView.ItemsSource = technicianList;
            listView.SelectionChanged += TechnicianListView_SelectionChanged;
        }

        private void ApplyRoleRestrictions()
        {
            if (_currentUserRole != "Admin")
            {
                if (FindName("AddTechnicianButton") is Button addBtn)
                    addBtn.Visibility = Visibility.Collapsed;

                if (FindName("FormContent") is ContentControl formContent)
                    formContent.Visibility = Visibility.Collapsed;
            }
        }

        public void LoadTechnicians()
        {
            technicianList.Clear();

            using (var conn = DatabaseHelper.GetConnection())
            {
                conn.Open();
                using (var cmd = new SQLiteCommand("SELECT * FROM Technicians", conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        technicianList.Add(new Technician
                        {
                            TechnicianID = Convert.ToInt32(reader["TechnicianID"]),
                            Namee = reader["Name"].ToString(),
                            ContactNumm = reader["ContactNum"].ToString(),
                            Addresss = reader["Address"].ToString(),
                            AssignedAreaa = reader["AssignedArea"].ToString(),
                            Notee = reader["Note"]?.ToString()
                        });
                    }
                }
            }
        }

        private void AddTechnicianButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentUserRole != "Admin")
            {
                MessageBox.Show("Access Denied: Only Admins can add technicians.", "Access Denied", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var form = new AddTechnicianControl();
            form.OnSavedSuccessfully += (s, args) =>
            {
                FormContent.Content = null;
                LoadTechnicians();
            };
            form.OnCancelRequested += (s, args) => FormContent.Content = null;

            FormContent.Content = form;
        }

        private void TechnicianListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selected = listView.SelectedItem as Technician;
            if (selected == null || _currentUserRole != "Admin")
            {
                FormContent.Content = null;
                return;
            }

            var form = new AddTechnicianControl(selected);
            form.OnSavedSuccessfully += (s, args) =>
            {
                FormContent.Content = null;
                LoadTechnicians();
                listView.SelectedItem = null;
            };
            form.OnCancelRequested += (s, args) =>
            {
                FormContent.Content = null;
                listView.SelectedItem = null;
            };

            FormContent.Content = form;
        }

        private void SearchBox_KeyUp(object sender, KeyEventArgs e)
        {
            string searchTerm = SearchBox.Text.ToLower();

            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                listView.ItemsSource = technicianList;
            }
            else
            {
                var filtered = technicianList
                    .Where(t =>
                        (t.Namee?.ToLower().Contains(searchTerm) ?? false)
                     || (t.ContactNumm?.ToLower().Contains(searchTerm) ?? false)
                     || (t.Addresss?.ToLower().Contains(searchTerm) ?? false)
                     || (t.AssignedAreaa?.ToLower().Contains(searchTerm) ?? false)
                     || (t.Notee?.ToLower().Contains(searchTerm) ?? false))
                    .ToList();

                listView.ItemsSource = filtered;
            }
        }

        private void SearchBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (SearchBox.Text == "Search by name, address, or contact")
            {
                SearchBox.Text = "";
                SearchBox.Foreground = new SolidColorBrush(Colors.Black);
            }
        }

        private void SearchBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(SearchBox.Text))
            {
                SearchBox.Text = "Search by name, address, or contact";
                SearchBox.Foreground = new SolidColorBrush(Colors.Gray);
            }
        }
    }
}
