using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using HouseholdMS.View.UserControls;
using HouseholdMS.Model;
using System.Data.SQLite; // SQLite

namespace HouseholdMS.View
{
    public partial class ServiceRecordsView : UserControl
    {
        private readonly ObservableCollection<ServiceRecord> records = new ObservableCollection<ServiceRecord>();
        private readonly string _currentUserRole;

        public ServiceRecordsView(string userRole = "User")
        {
            InitializeComponent();
            _currentUserRole = userRole;
            LoadServiceRecords();
            ApplyRoleRestrictions();
        }

        private void ApplyRoleRestrictions()
        {
            if (_currentUserRole == "User")
            {
                if (FindName("AddServiceRecordButton") is Button addBtn)
                    addBtn.Visibility = Visibility.Collapsed;

                if (FindName("RefreshButton") is Button refreshBtn)
                    refreshBtn.Visibility = Visibility.Collapsed;

                if (FindName("FormContent") is ContentControl formContent)
                    formContent.Visibility = Visibility.Collapsed;
            }
            else if (_currentUserRole == "Technician")
            {
                if (FindName("FormContent") is ContentControl formContent)
                    formContent.Visibility = Visibility.Visible;
            }
        }

        private void LoadServiceRecords()
        {
            records.Clear();

            using (var conn = DatabaseHelper.GetConnection())
            {
                conn.Open();
                string query = "SELECT * FROM InspectionReport";

                using (var cmd = new SQLiteCommand(query, conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        records.Add(new ServiceRecord
                        {
                            ReportID = Convert.ToInt32(reader["ReportID"]),
                            HouseholdID = Convert.ToInt32(reader["HouseholdID"]),
                            TechnicianID = reader["TechnicianID"] != DBNull.Value ? Convert.ToInt32(reader["TechnicianID"]) : 0,
                            LastInspect = reader["LastInspect"]?.ToString(),
                            Problem = reader["Problem"]?.ToString(),
                            Action = reader["Action"]?.ToString(),
                            RepairDate = reader["RepairDate"]?.ToString()
                        });
                    }
                }
            }

            ServiceRecordsListView.ItemsSource = records;
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            LoadServiceRecords();
            ServiceRecordsListView.SelectedItem = null;
            FormContent.Content = null;
        }

        private void AddServiceRecordButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentUserRole == "User")
            {
                MessageBox.Show("Access Denied: You cannot add service records.", "Access Denied", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var form = new AddServiceRecordControl(); // Add mode

            form.OnSavedSuccessfully += (s, args) =>
            {
                FormContent.Content = null;
                LoadServiceRecords();
            };

            form.OnCancelRequested += (s, args) =>
            {
                FormContent.Content = null;
            };

            FormContent.Content = form;
        }

        private void ServiceRecordsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ServiceRecordsListView.SelectedItem is ServiceRecord selected)
            {
                if (_currentUserRole == "User")
                {
                    FormContent.Content = null;
                    return;
                }

                var form = new AddServiceRecordControl(selected); // Edit mode

                form.OnSavedSuccessfully += (s, args) =>
                {
                    FormContent.Content = null;
                    LoadServiceRecords();
                    ServiceRecordsListView.SelectedItem = null;
                };

                form.OnCancelRequested += (s, args) =>
                {
                    FormContent.Content = null;
                    ServiceRecordsListView.SelectedItem = null;
                };

                FormContent.Content = form;
            }
            else
            {
                FormContent.Content = null;
            }
        }

        private void DeleteServiceRecord(ServiceRecord record)
        {
            if (_currentUserRole != "Admin")
            {
                MessageBox.Show("Access Denied: Only Admins can delete service records.", "Access Denied", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (MessageBox.Show($"Are you sure you want to delete Service Record #{record.ReportID}?",
                "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                using (var conn = DatabaseHelper.GetConnection())
                {
                    conn.Open();
                    using (var cmd = new SQLiteCommand("DELETE FROM InspectionReport WHERE ReportID = @id", conn))
                    {
                        cmd.Parameters.AddWithValue("@id", record.ReportID);
                        cmd.ExecuteNonQuery();
                    }
                }

                LoadServiceRecords();
                FormContent.Content = null;
            }
        }
    }
}
