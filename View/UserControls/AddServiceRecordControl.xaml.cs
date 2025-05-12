using System;
using System.Data.SQLite;
using System.Windows;
using System.Windows.Controls;

namespace HouseholdMS.View.UserControls
{
    public partial class AddServiceRecordControl : UserControl
    {
        private readonly ServiceRecord _record;
        private readonly bool _isEdit;

        public event EventHandler OnSavedSuccessfully;
        public event EventHandler OnCancelRequested;

        public AddServiceRecordControl()
        {
            InitializeComponent();
            FormHeader.Text = "➕ Add Service Record";
            SaveButton.Content = "➕ Add";
            RepairDatePicker.SelectedDate = DateTime.Today;
            LastInspectPicker.SelectedDate = DateTime.Today;
        }

        public AddServiceRecordControl(ServiceRecord record) : this()
        {
            _record = record;
            _isEdit = true;

            FormHeader.Text = $"✏ Edit Record #{record.ReportID}";
            SaveButton.Content = "✏ Save Changes";
            DeleteButton.Visibility = Visibility.Visible;

            HouseholdIDBox.Text = record.HouseholdID.ToString();
            TechnicianIDBox.Text = record.TechnicianID.ToString();

            if (DateTime.TryParse(record.LastInspect, out var lastInspDate))
                LastInspectPicker.SelectedDate = lastInspDate;

            ProblemBox.Text = record.Problem;
            ActionBox.Text = record.Action;

            if (DateTime.TryParse(record.RepairDate, out var parsedDate))
                RepairDatePicker.SelectedDate = parsedDate;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(HouseholdIDBox.Text.Trim(), out int householdID) ||
                !int.TryParse(TechnicianIDBox.Text.Trim(), out int technicianID))
            {
                MessageBox.Show("Please enter valid numeric values for Household ID and Technician ID.",
                                "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string lastInspect = LastInspectPicker.SelectedDate?.ToString("yyyy-MM-dd");
            string problem = ProblemBox.Text.Trim();
            string action = ActionBox.Text.Trim();
            string repairDate = RepairDatePicker.SelectedDate?.ToString("yyyy-MM-dd");

            using (var conn = DatabaseHelper.GetConnection())
            {
                conn.Open();
                string query = _isEdit
                    ? @"UPDATE InspectionReport SET 
                            HouseholdID = @householdID,
                            TechnicianID = @technicianID,
                            LastInspect = @lastInspect,
                            Problem = @problem,
                            Action = @action,
                            RepairDate = @repairDate
                        WHERE ReportID = @reportID"
                    : @"INSERT INTO InspectionReport 
                            (HouseholdID, TechnicianID, LastInspect, Problem, Action, RepairDate)
                       VALUES (@householdID, @technicianID, @lastInspect, @problem, @action, @repairDate)";

                using (var cmd = new SQLiteCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@householdID", householdID);
                    cmd.Parameters.AddWithValue("@technicianID", technicianID);
                    cmd.Parameters.AddWithValue("@lastInspect", lastInspect ?? DBNull.Value.ToString());
                    cmd.Parameters.AddWithValue("@problem", string.IsNullOrWhiteSpace(problem) ? DBNull.Value : (object)problem);
                    cmd.Parameters.AddWithValue("@action", string.IsNullOrWhiteSpace(action) ? DBNull.Value : (object)action);
                    cmd.Parameters.AddWithValue("@repairDate", repairDate ?? DBNull.Value.ToString());

                    if (_isEdit)
                        cmd.Parameters.AddWithValue("@reportID", _record.ReportID);

                    cmd.ExecuteNonQuery();
                }
            }

            MessageBox.Show(_isEdit ? "Record updated!" : "Record added!",
                            "Success", MessageBoxButton.OK, MessageBoxImage.Information);

            OnSavedSuccessfully?.Invoke(this, EventArgs.Empty);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            OnCancelRequested?.Invoke(this, EventArgs.Empty);
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (!_isEdit || _record == null)
                return;

            var result = MessageBox.Show($"Are you sure you want to delete Record #{_record.ReportID}?",
                                         "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
                return;

            using (var conn = DatabaseHelper.GetConnection())
            {
                conn.Open();
                using (var cmd = new SQLiteCommand("DELETE FROM InspectionReport WHERE ReportID = @id", conn))
                {
                    cmd.Parameters.AddWithValue("@id", _record.ReportID);
                    cmd.ExecuteNonQuery();
                }
            }

            MessageBox.Show("Record deleted.", "Deleted", MessageBoxButton.OK, MessageBoxImage.Information);
            OnSavedSuccessfully?.Invoke(this, EventArgs.Empty);
        }
    }
}
