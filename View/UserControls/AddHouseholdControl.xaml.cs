using System;
using System.Windows;
using System.Windows.Controls;
using HouseholdMS.Model;
using System.Data.SQLite;

namespace HouseholdMS.View.UserControls
{
    public partial class AddHouseholdControl : UserControl
    {
        // DB canonical values
        private const string DB_OPERATIONAL = "Operational";
        private const string DB_IN_SERVICE = "In Service";

        // UI label
        private const string UI_OUT_OF_SERVICE = "Out of Service";

        // Legacy to be treated as "In Service" for compatibility when editing
        private const string LEGACY_NOT_OPERATIONAL = "Not Operational";

        private int? EditingHouseholdID = null;

        public event EventHandler OnSavedSuccessfully;
        public event EventHandler OnCancelRequested;

        public AddHouseholdControl()
        {
            InitializeComponent();
            FormHeader.Text = "➕ Add Household";
            SaveButton.Content = "➕ Add";
            DeleteButton.Visibility = Visibility.Collapsed;

            // Default dates
            InstDatePicker.SelectedDate = DateTime.Today;
            LastInspPicker.SelectedDate = DateTime.Today;

            // Default status is "Operational" (SelectedIndex=0)
        }

        public AddHouseholdControl(Household householdToEdit) : this()
        {
            EditingHouseholdID = householdToEdit.HouseholdID;

            FormHeader.Text = $"✏ Edit Household #{EditingHouseholdID}";
            SaveButton.Content = "✏ Save Changes";
            DeleteButton.Visibility = Visibility.Visible;

            OwnerBox.Text = householdToEdit.OwnerName;
            UserNameBox.Text = householdToEdit.UserName;
            MunicipalityBox.Text = householdToEdit.Municipality;
            DistrictBox.Text = householdToEdit.District;
            ContactBox.Text = householdToEdit.ContactNum;
            NoteBox.Text = householdToEdit.UserComm;

            InstDatePicker.SelectedDate = householdToEdit.InstallDate;
            LastInspPicker.SelectedDate = householdToEdit.LastInspect;

            // set status dropdown from DB value
            SelectStatus(householdToEdit.Statuss);
        }

        private void SelectStatus(string statusFromDb)
        {
            var s = (statusFromDb ?? string.Empty).Trim();

            if (s.Equals(DB_OPERATIONAL, StringComparison.OrdinalIgnoreCase))
            {
                StatusCombo.SelectedIndex = 0; // "Operational"
                return;
            }

            // Treat DB "In Service", UI "Out of Service", and legacy "Not Operational" as the same bucket
            if (s.Equals(DB_IN_SERVICE, StringComparison.OrdinalIgnoreCase) ||
                s.Equals(UI_OUT_OF_SERVICE, StringComparison.OrdinalIgnoreCase) ||
                s.Equals(LEGACY_NOT_OPERATIONAL, StringComparison.OrdinalIgnoreCase))
            {
                StatusCombo.SelectedIndex = 1; // "Out of Service" (maps to DB "In Service")
                return;
            }

            // Fallback
            StatusCombo.SelectedIndex = 0;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateFields()) return;

            using (var conn = DatabaseHelper.GetConnection())
            {
                conn.Open();

                // Duplicate contact number check
                using (var contactCheck = new SQLiteCommand(@"
                    SELECT COUNT(*) FROM Households
                    WHERE ContactNum = @Contact
                      AND (@ID IS NULL OR HouseholdID != @ID)", conn))
                {
                    contactCheck.Parameters.AddWithValue("@Contact", Contact);
                    contactCheck.Parameters.AddWithValue("@ID", (object)EditingHouseholdID ?? DBNull.Value);

                    if (Convert.ToInt32(contactCheck.ExecuteScalar()) > 0)
                    {
                        MessageBox.Show("A household with this contact number already exists.", "Duplicate Contact", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }

                // Soft check based on OwnerName only
                using (var softCheck = new SQLiteCommand(@"
                    SELECT COUNT(*) FROM Households
                    WHERE OwnerName = @Owner
                      AND (@ID IS NULL OR HouseholdID != @ID)", conn))
                {
                    softCheck.Parameters.AddWithValue("@Owner", OwnerName);
                    softCheck.Parameters.AddWithValue("@ID", (object)EditingHouseholdID ?? DBNull.Value);

                    if (Convert.ToInt32(softCheck.ExecuteScalar()) > 0)
                    {
                        var confirm = MessageBox.Show(
                            "A similar household already exists.\nDo you still want to save?",
                            "Possible Duplicate",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Warning);

                        if (confirm != MessageBoxResult.Yes) return;
                    }
                }

                string query = (EditingHouseholdID == null)
                    ? @"INSERT INTO Households 
                        (OwnerName, UserName, Municipality, District, ContactNum, InstallDate, LastInspect, UserComm, Statuss)
                       VALUES 
                        (@Owner, @UserName, @Municipality, @District, @Contact, @Inst, @Last, @UserComm, @Status)"
                    : @"UPDATE Households SET 
                            OwnerName = @Owner,
                            UserName = @UserName,
                            Municipality = @Municipality,
                            District = @District,
                            ContactNum = @Contact,
                            InstallDate = @Inst,
                            LastInspect = @Last,
                            UserComm = @UserComm,
                            Statuss = @Status
                        WHERE HouseholdID = @ID";

                using (var cmd = new SQLiteCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@Owner", OwnerName);
                    cmd.Parameters.AddWithValue("@UserName", UserName);
                    cmd.Parameters.AddWithValue("@Municipality", Municipality);
                    cmd.Parameters.AddWithValue("@District", District);
                    cmd.Parameters.AddWithValue("@Contact", Contact);

                    // Save dates as yyyy-MM-dd
                    cmd.Parameters.AddWithValue("@Inst", InstDatePicker.SelectedDate.Value.ToString("yyyy-MM-dd"));
                    cmd.Parameters.AddWithValue("@Last", LastInspPicker.SelectedDate.Value.ToString("yyyy-MM-dd"));

                    cmd.Parameters.AddWithValue("@UserComm", string.IsNullOrWhiteSpace(Note) ? (object)DBNull.Value : Note);
                    cmd.Parameters.AddWithValue("@Status", Status); // Status property returns DB canonical value

                    if (EditingHouseholdID != null)
                        cmd.Parameters.AddWithValue("@ID", EditingHouseholdID);

                    cmd.ExecuteNonQuery();
                }
            }

            MessageBox.Show("Household saved successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            OnSavedSuccessfully?.Invoke(this, EventArgs.Empty);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            OnCancelRequested?.Invoke(this, EventArgs.Empty);
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (EditingHouseholdID == null) return;

            var confirm = MessageBox.Show(
                "Are you sure you want to delete this household?",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            using (var conn = DatabaseHelper.GetConnection())
            {
                conn.Open();
                using (var cmd = new SQLiteCommand("DELETE FROM Households WHERE HouseholdID = @id", conn))
                {
                    cmd.Parameters.AddWithValue("@id", EditingHouseholdID);
                    cmd.ExecuteNonQuery();
                }
            }

            MessageBox.Show("Household deleted successfully.", "Deleted", MessageBoxButton.OK, MessageBoxImage.Information);
            OnSavedSuccessfully?.Invoke(this, EventArgs.Empty);
        }

        private bool ValidateFields()
        {
            bool hasError = false;
            OwnerBox.Tag = ContactBox.Tag = null;

            if (string.IsNullOrWhiteSpace(OwnerName)) { OwnerBox.Tag = "error"; hasError = true; }
            if (string.IsNullOrWhiteSpace(Contact)) { ContactBox.Tag = "error"; hasError = true; }

            int dummy;
            if (!int.TryParse(ContactBox.Text, out dummy))
            {
                MessageBox.Show("Please enter valid contact number!", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (InstDatePicker.SelectedDate == null || InstDatePicker.SelectedDate > DateTime.Today)
            {
                MessageBox.Show("Installation date cannot be in the future or empty.", "Invalid Date", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (LastInspPicker.SelectedDate == null || LastInspPicker.SelectedDate > DateTime.Today)
            {
                MessageBox.Show("Inspection date cannot be in the future or empty.", "Invalid Date", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (InstDatePicker.SelectedDate > LastInspPicker.SelectedDate)
            {
                MessageBox.Show("Inspection date cannot be earlier than installation date.", "Invalid Date Order", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (hasError)
            {
                MessageBox.Show("Please correct the highlighted fields.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            return true;
        }

        // Properties for field values
        public string OwnerName { get { return OwnerBox.Text.Trim(); } }
        public string UserName { get { return UserNameBox.Text.Trim(); } }
        public string Municipality { get { return MunicipalityBox.Text.Trim(); } }
        public string District { get { return DistrictBox.Text.Trim(); } }
        public string Contact { get { return ContactBox.Text.Trim(); } }
        public string Note { get { return NoteBox.Text.Trim(); } }

        /// <summary>
        /// Returns the DB canonical value for Status:
        /// - "Operational" (UI Operational)
        /// - "In Service"  (UI Out of Service)
        /// </summary>
        public string Status
        {
            get
            {
                var selectedText = (StatusCombo.SelectedItem as ComboBoxItem)?.Content?.ToString().Trim() ?? string.Empty;
                if (string.Equals(selectedText, UI_OUT_OF_SERVICE, StringComparison.OrdinalIgnoreCase))
                    return DB_IN_SERVICE;
                return DB_OPERATIONAL;
            }
        }

        public DateTime InstallDate { get { return InstDatePicker.SelectedDate.Value; } }
        public DateTime LastInspect { get { return LastInspPicker.SelectedDate.Value; } }
    }
}
