using System;
using System.Collections.ObjectModel;
using System.Data.SQLite;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using HouseholdMS.View.UserControls;

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
        }

        private void ApplyRoleRestrictions()
        {
            if (_currentUserRole != "Admin")
            {
                // 🔥 Instead of disabling, hide Add Button
                if (FindName("AddTechnicianButton") is Button addBtn)
                    addBtn.Visibility = Visibility.Collapsed;

                // 🔥 Hide FormContent panel too
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
                var cmd = new SQLiteCommand("SELECT * FROM Technicians", conn);
                var reader = cmd.ExecuteReader();

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

            TechnicianDataGrid.ItemsSource = technicianList;
        }

        private void AddTechnicianButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentUserRole != "Admin")
            {
                MessageBox.Show("Access Denied: Only Admins can add technicians.", "Access Denied", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var form = new AddTechnicianControl(); // Add mode
            form.OnSavedSuccessfully += (s, args) =>
            {
                FormContent.Content = null;
                LoadTechnicians();
            };
            form.OnCancelRequested += (s, args) => FormContent.Content = null;

            FormContent.Content = form;
        }

        private void TechnicianDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selected = TechnicianDataGrid.SelectedItem as Technician;
            if (selected == null)
            {
                FormContent.Content = null;
                return;
            }

            if (_currentUserRole != "Admin")
            {
                FormContent.Content = null;
                return;
            }

            var form = new AddTechnicianControl(selected); // Edit mode
            form.OnSavedSuccessfully += (s, args) =>
            {
                FormContent.Content = null;
                LoadTechnicians();
                TechnicianDataGrid.SelectedItem = null;
            };
            form.OnCancelRequested += (s, args) =>
            {
                FormContent.Content = null;
                TechnicianDataGrid.SelectedItem = null;
            };

            FormContent.Content = form;
        }

        private void SearchBox_KeyUp(object sender, KeyEventArgs e)
        {
            string searchTerm = SearchBox.Text.ToLower();

            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                TechnicianDataGrid.ItemsSource = technicianList;
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

                TechnicianDataGrid.ItemsSource = filtered;
            }
        }

        private void SearchBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (SearchBox.Text == "Search...")
            {
                SearchBox.Text = "";
                SearchBox.Foreground = new SolidColorBrush(Colors.Black);
            }
        }

        private void SearchBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(SearchBox.Text))
            {
                SearchBox.Text = "Search...";
                SearchBox.Foreground = new SolidColorBrush(Colors.Gray);
            }
        }
    }
}
