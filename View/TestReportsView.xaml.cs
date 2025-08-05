using HouseholdMS.Model;
using System;
using System.Collections.ObjectModel;
using System.Data.SQLite;
using System.Windows;
using System.Windows.Controls;
using System.Collections.Generic;

namespace HouseholdMS.View
{
    public partial class TestReportsView : UserControl
    {
        private readonly ObservableCollection<TestReport> reports = new ObservableCollection<TestReport>();
        private readonly string _currentUserRole;

        public TestReportsView(string userRole = "User")
        {
            InitializeComponent();
            _currentUserRole = userRole;
            LoadTestReports();
            ApplyRoleRestrictions();
        }

        private void ApplyRoleRestrictions()
        {
            if (_currentUserRole == "User")
            {
                RefreshButton.Visibility = Visibility.Collapsed;
                FormContent.Visibility = Visibility.Collapsed;
            }
            else if (_currentUserRole == "Technician")
            {
                FormContent.Visibility = Visibility.Visible;
            }
        }

        private void LoadTestReports()
        {
            reports.Clear();
            using (var conn = DatabaseHelper.GetConnection())
            {
                conn.Open();
                using (var cmd = new SQLiteCommand("SELECT * FROM TestReports", conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        DateTime testDate = DateTime.MinValue;
                        if (reader["TestDate"] != DBNull.Value)
                        {
                            DateTime.TryParse(reader["TestDate"].ToString(), out testDate);
                        }

                        reports.Add(new TestReport
                        {
                            ReportID = Convert.ToInt32(reader["ReportID"]),
                            HouseholdID = Convert.ToInt32(reader["HouseholdID"]),
                            TechnicianID = Convert.ToInt32(reader["TechnicianID"]),
                            TestDate = testDate,
                            DeviceStatus = reader["DeviceStatus"]?.ToString(),
                            InspectionItems = ParseInspectionItems(reader["InspectionItems"]?.ToString()),
                            Annotations = ParseStringList(reader["Annotations"]?.ToString()),
                            SettingsVerification = ParseSettingsVerification(reader["SettingsVerification"]?.ToString()),
                            ImagePaths = ParseStringList(reader["ImagePaths"]?.ToString())
                        });
                    }
                }
            }

            TestReportsListView.ItemsSource = reports;
        }

        // Helper: Parse delimited string to list of InspectionItem
        private List<InspectionItem> ParseInspectionItems(string str)
        {
            var list = new List<InspectionItem>();
            if (string.IsNullOrWhiteSpace(str)) return list;
            // Each item: Name:Result:Annotation, items separated by ;
            foreach (var part in str.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var fields = part.Split(':');
                list.Add(new InspectionItem
                {
                    Name = fields.Length > 0 ? fields[0] : "",
                    Result = fields.Length > 1 ? fields[1] : "",
                    Annotation = fields.Length > 2 ? fields[2] : ""
                });
            }
            return list;
        }

        // Helper: Parse delimited string to list of SettingsVerificationItem
        private List<SettingsVerificationItem> ParseSettingsVerification(string str)
        {
            var list = new List<SettingsVerificationItem>();
            if (string.IsNullOrWhiteSpace(str)) return list;
            // Each item: Parameter:Value:Status, items separated by ;
            foreach (var part in str.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var fields = part.Split(':');
                list.Add(new SettingsVerificationItem
                {
                    Parameter = fields.Length > 0 ? fields[0] : "",
                    Value = fields.Length > 1 ? fields[1] : "",
                    Status = fields.Length > 2 ? fields[2] : ""
                });
            }
            return list;
        }

        // Helper: Parse delimited string to list of string (for Annotations, ImagePaths)
        private List<string> ParseStringList(string str)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(str)) return list;
            foreach (var item in str.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                list.Add(item.Trim());
            }
            return list;
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            LoadTestReports();
            TestReportsListView.SelectedItem = null;
            FormContent.Content = null;
        }

        private void TestReportsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TestReportsListView.SelectedItem is TestReport selected)
            {
                if (_currentUserRole == "User")
                {
                    FormContent.Content = null;
                    return;
                }

                var form = new UserControls.TestReportDetailControl(selected);
                form.OnCloseRequested += (_, __) =>
                {
                    FormContent.Content = null;
                    TestReportsListView.SelectedItem = null;
                };

                FormContent.Content = form;
            }
            else
            {
                FormContent.Content = null;
            }
        }
    }
}
