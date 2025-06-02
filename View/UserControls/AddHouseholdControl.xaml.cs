using System;
using System.Data;
using System.Data.SqlClient;
using System.Windows;
using System.Windows.Controls;
using HouseholdMS.Model;

namespace HouseholdMS.View.UserControls
{
    public partial class AddHouseholdControl : UserControl
    {
        private int? EditingHouseholdID = null;

        public event EventHandler OnSavedSuccessfully;
        public event EventHandler OnCancelRequested;

        public AddHouseholdControl()
        {
            InitializeComponent();
            FormHeader.Text = "➕ Add Household";
            SaveButton.Content = "➕ Add";
            DeleteButton.Visibility = Visibility.Collapsed;

            InstDatePicker.SelectedDate = DateTime.Today;
            LastInspPicker.SelectedDate = DateTime.Today;
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
            StatusBox.Text = householdToEdit.Statuss;

            if (DateTime.TryParse(householdToEdit.InstallDate.ToString(), out DateTime dt1))
                InstDatePicker.SelectedDate = dt1;

            if (DateTime.TryParse(householdToEdit.LastInspect.ToString(), out DateTime dt2))
                LastInspPicker.SelectedDate = dt2;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateFields()) return;

            using (var conn = DatabaseHelper.GetConnection())
            {
                conn.Open();

                // Duplicate contact number check
                var contactCheck = new SqlCommand(@"
                    SELECT COUNT(*) FROM Households
                    WHERE ContactNum = @Contact
                    AND (@ID IS NULL OR HouseholdID != @ID)", conn);

                contactCheck.Parameters.AddWithValue("@Contact", Contact);
                contactCheck.Parameters.AddWithValue("@ID", (object)EditingHouseholdID ?? DBNull.Value);

                if (Convert.ToInt32(contactCheck.ExecuteScalar()) > 0)
                {
                    MessageBox.Show("A household with this contact number already exists.", "Duplicate Contact", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Soft check based on OwnerName only
                var softCheck = new SqlCommand(@"
                    SELECT COUNT(*) FROM Households
                    WHERE OwnerName = @Owner
                    AND (@ID IS NULL OR HouseholdID != @ID)", conn);

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

                using (var cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@Owner", OwnerName);
                    cmd.Parameters.AddWithValue("@UserName", UserName);
                    cmd.Parameters.AddWithValue("@Municipality", Municipality);
                    cmd.Parameters.AddWithValue("@District", District);
                    cmd.Parameters.AddWithValue("@Contact", Contact);
                    cmd.Parameters.AddWithValue("@Inst", string.IsNullOrEmpty(InstallDate) ? (object)DBNull.Value : InstallDate);
                    cmd.Parameters.AddWithValue("@Last", string.IsNullOrEmpty(LastInspect) ? (object)DBNull.Value : LastInspect);
                    cmd.Parameters.AddWithValue("@UserComm", string.IsNullOrWhiteSpace(Note) ? (object)DBNull.Value : Note);
                    cmd.Parameters.AddWithValue("@Status", string.IsNullOrWhiteSpace(Status) ? (object)DBNull.Value : Status);

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
                var cmd = new SqlCommand("DELETE FROM Households WHERE HouseholdID = @id", conn);
                cmd.Parameters.AddWithValue("@id", EditingHouseholdID);
                cmd.ExecuteNonQuery();
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

            if (!int.TryParse(ContactBox.Text, out _))
            {
                MessageBox.Show("Please enter valid contact number!", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (InstDatePicker.SelectedDate == null || InstDatePicker.SelectedDate > DateTime.Today)
            {
                MessageBox.Show("Installation date cannot be in the future.", "Invalid Date", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (LastInspPicker.SelectedDate == null || LastInspPicker.SelectedDate > DateTime.Today)
            {
                MessageBox.Show("Inspection date cannot be in the future.", "Invalid Date", MessageBoxButton.OK, MessageBoxImage.Warning);
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

        public string OwnerName => OwnerBox.Text.Trim();
        public string UserName => UserNameBox.Text.Trim();
        public string Municipality => MunicipalityBox.Text.Trim();
        public string District => DistrictBox.Text.Trim();
        public string Contact => ContactBox.Text.Trim();
        public string InstallDate => InstDatePicker.SelectedDate?.ToString("yyyy-MM-dd");
        public string LastInspect => LastInspPicker.SelectedDate?.ToString("yyyy-MM-dd");
        public string Note => NoteBox.Text.Trim();
        public string Status => StatusBox.Text.Trim();
    }
}
