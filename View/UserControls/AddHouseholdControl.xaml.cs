using System;
using System.Data.SQLite;
using System.Windows;
using System.Windows.Controls;

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
            AddressBox.Text = householdToEdit.Address;
            ContactBox.Text = householdToEdit.ContactNum;
            NoteBox.Text = householdToEdit.Note;

            if (DateTime.TryParse(householdToEdit.InstDate, out var instDate))
                InstDatePicker.SelectedDate = instDate;

            if (DateTime.TryParse(householdToEdit.LastInspDate, out var lastInsp))
                LastInspPicker.SelectedDate = lastInsp;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateFields()) return;

            using (var conn = DatabaseHelper.GetConnection())
            {
                conn.Open();

                // Duplicate contact check
                var contactCheck = new SQLiteCommand(@"
                    SELECT COUNT(*) FROM Households
                    WHERE ContactNum = @Contact
                    AND (HouseholdID != @ID OR @ID IS NULL)", conn);

                contactCheck.Parameters.AddWithValue("@Contact", Contact);
                contactCheck.Parameters.AddWithValue("@ID", EditingHouseholdID ?? (object)DBNull.Value);

                if (Convert.ToInt32(contactCheck.ExecuteScalar()) > 0)
                {
                    MessageBox.Show("A household with this contact number already exists.", "Duplicate Contact", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Soft duplicate check on Owner or Address
                var softCheck = new SQLiteCommand(@"
                    SELECT COUNT(*) FROM Households
                    WHERE (OwnerName = @Owner OR Address = @Addr)
                    AND (HouseholdID != @ID OR @ID IS NULL)", conn);

                softCheck.Parameters.AddWithValue("@Owner", OwnerName);
                softCheck.Parameters.AddWithValue("@Addr", Address);
                softCheck.Parameters.AddWithValue("@ID", EditingHouseholdID ?? (object)DBNull.Value);

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
                    ? @"INSERT INTO Households (OwnerName, Address, ContactNum, InstallDate, LastInspect, Note)
                       VALUES (@Owner, @Addr, @Contact, @Inst, @Last, @Note)"
                    : @"UPDATE Households SET 
                            OwnerName = @Owner,
                            Address = @Addr,
                            ContactNum = @Contact,
                            InstallDate = @Inst,
                            LastInspect = @Last,
                            Note = @Note
                        WHERE HouseholdID = @ID";

                using (var cmd = new SQLiteCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@Owner", OwnerName);
                    cmd.Parameters.AddWithValue("@Addr", Address);
                    cmd.Parameters.AddWithValue("@Contact", Contact);
                    cmd.Parameters.AddWithValue("@Inst", InstallDate);
                    cmd.Parameters.AddWithValue("@Last", LastInspect);
                    cmd.Parameters.AddWithValue("@Note", string.IsNullOrWhiteSpace(Note) ? DBNull.Value : (object)Note);

                    if (EditingHouseholdID != null)
                        cmd.Parameters.AddWithValue("@ID", EditingHouseholdID);

                    cmd.ExecuteNonQuery();
                }
            }

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
                var cmd = new SQLiteCommand("DELETE FROM Households WHERE HouseholdID = @id", conn);
                cmd.Parameters.AddWithValue("@id", EditingHouseholdID);
                cmd.ExecuteNonQuery();
            }

            MessageBox.Show("Household deleted successfully.", "Deleted", MessageBoxButton.OK, MessageBoxImage.Information);
            OnSavedSuccessfully?.Invoke(this, EventArgs.Empty);
        }

        private bool ValidateFields()
        {
            bool hasError = false;
            OwnerBox.Tag = AddressBox.Tag = ContactBox.Tag = null;

            if (string.IsNullOrWhiteSpace(OwnerName)) { OwnerBox.Tag = "error"; hasError = true; }
            if (string.IsNullOrWhiteSpace(Address)) { AddressBox.Tag = "error"; hasError = true; }
            if (string.IsNullOrWhiteSpace(Contact)) { ContactBox.Tag = "error"; hasError = true; }

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

            // 🚀 NEW IMPORTANT CHECK
            if (InstDatePicker.SelectedDate != null && LastInspPicker.SelectedDate != null)
            {
                if (LastInspPicker.SelectedDate < InstDatePicker.SelectedDate)
                {
                    MessageBox.Show("Inspection date cannot be earlier than installation date.", "Invalid Date Order", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }
            }

            if (hasError)
            {
                MessageBox.Show("Please correct the highlighted fields.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            return true;
        }

        public string OwnerName => OwnerBox.Text.Trim();
        public string Address => AddressBox.Text.Trim();
        public string Contact => ContactBox.Text.Trim();
        public string InstallDate => InstDatePicker.SelectedDate?.ToString("yyyy-MM-dd");
        public string LastInspect => LastInspPicker.SelectedDate?.ToString("yyyy-MM-dd");
        public string Note => NoteBox.Text.Trim();
    }
}
