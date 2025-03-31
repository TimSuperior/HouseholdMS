using System;
using System.Collections.ObjectModel;
using System.Data.SQLite;
using System.Windows;
using System.Windows.Controls;

namespace HouseholdMS.View
{
    public partial class AddHouseholdWindow : Window
    {
        private ObservableCollection<Household> households = new ObservableCollection<Household>();
        private int? EditingHouseholdID = null;

        public AddHouseholdWindow()
        {
            InitializeComponent();
            InstDatePicker.SelectedDate = DateTime.Today;
            LastInspPicker.SelectedDate = DateTime.Today;

            InstDatePicker.DisplayDateEnd = DateTime.Today;
            LastInspPicker.DisplayDateEnd = DateTime.Today;
        }

        public AddHouseholdWindow(Household householdToEdit) : this()
        {
            EditingHouseholdID = householdToEdit.HouseholdID;

            OwnerBox.Text = householdToEdit.OwnerName;
            AddressBox.Text = householdToEdit.Address;
            ContactBox.Text = householdToEdit.ContactNum;

            if (DateTime.TryParse(householdToEdit.InstDate, out var instDate))
                InstDatePicker.SelectedDate = instDate;

            if (DateTime.TryParse(householdToEdit.LastInspDate, out var lastInsp))
                LastInspPicker.SelectedDate = lastInsp;
        }

        public bool Saved { get; private set; } = false;

        public string OwnerName => OwnerBox.Text.Trim();
        public string Address => AddressBox.Text.Trim();
        public string Contact => ContactBox.Text.Trim();
        public string InstallDate => InstDatePicker.SelectedDate?.ToString("yyyy-MM-dd");
        public string LastInspect => LastInspPicker.SelectedDate?.ToString("yyyy-MM-dd");

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            bool hasError = false;

            // Reset validation styles
            OwnerBox.Tag = null;
            AddressBox.Tag = null;
            ContactBox.Tag = null;

            if (string.IsNullOrWhiteSpace(OwnerName))
            {
                OwnerBox.Tag = "error";
                hasError = true;
            }

            if (string.IsNullOrWhiteSpace(Address))
            {
                AddressBox.Tag = "error";
                hasError = true;
            }

            if (string.IsNullOrWhiteSpace(Contact))
            {
                ContactBox.Tag = "error";
                hasError = true;
            }

            if (InstDatePicker.SelectedDate == null || InstDatePicker.SelectedDate > DateTime.Today)
            {
                MessageBox.Show("Installation date cannot be in the future.", "Invalid Date", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (LastInspPicker.SelectedDate == null || LastInspPicker.SelectedDate > DateTime.Today)
            {
                MessageBox.Show("Inspection date cannot be in the future.", "Invalid Date", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (hasError)
            {
                MessageBox.Show("Please correct the highlighted fields.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            using (SQLiteConnection conn = DatabaseHelper.GetConnection())
            {
                conn.Open();

                // 🔒 Unique Contact Number
                var checkContact = new SQLiteCommand(
                    @"SELECT COUNT(*) FROM Households 
                      WHERE ContactNum = @Contact AND (HouseholdID != @ID OR @ID IS NULL)", conn);
                checkContact.Parameters.AddWithValue("@Contact", Contact);
                checkContact.Parameters.AddWithValue("@ID", EditingHouseholdID ?? (object)DBNull.Value);

                if (Convert.ToInt32(checkContact.ExecuteScalar()) > 0)
                {
                    MessageBox.Show("A household with this contact number already exists.\nContact numbers must be unique.",
                                    "Duplicate Contact", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // ⚠ Warning for same OwnerName or Address
                var checkSoft = new SQLiteCommand(
                    @"SELECT COUNT(*) FROM Households 
                      WHERE (OwnerName = @Owner OR Address = @Addr)
                      AND (HouseholdID != @ID OR @ID IS NULL)", conn);
                checkSoft.Parameters.AddWithValue("@Owner", OwnerName);
                checkSoft.Parameters.AddWithValue("@Addr", Address);
                checkSoft.Parameters.AddWithValue("@ID", EditingHouseholdID ?? (object)DBNull.Value);

                if (Convert.ToInt32(checkSoft.ExecuteScalar()) > 0)
                {
                    var confirm = MessageBox.Show(
                        "Another household with the same owner name or address exists.\nDo you want to proceed?",
                        "Potential Duplicate", MessageBoxButton.YesNo, MessageBoxImage.Warning);

                    if (confirm != MessageBoxResult.Yes)
                        return;
                }

                // ✅ Insert or Update
                string query = (EditingHouseholdID == null)
                    ? @"INSERT INTO Households (OwnerName, Address, ContactNum, InstallDate, LastInspect)
                       VALUES (@Owner, @Addr, @Contact, @Inst, @Last)"
                    : @"UPDATE Households 
                       SET OwnerName = @Owner, Address = @Addr, ContactNum = @Contact,
                           InstallDate = @Inst, LastInspect = @Last
                       WHERE HouseholdID = @ID";

                using (SQLiteCommand cmd = new SQLiteCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@Owner", OwnerName);
                    cmd.Parameters.AddWithValue("@Addr", Address);
                    cmd.Parameters.AddWithValue("@Contact", Contact);
                    cmd.Parameters.AddWithValue("@Inst", InstallDate);
                    cmd.Parameters.AddWithValue("@Last", LastInspect);

                    if (EditingHouseholdID != null)
                        cmd.Parameters.AddWithValue("@ID", EditingHouseholdID);

                    cmd.ExecuteNonQuery();
                }
            }

            Saved = true;
            this.DialogResult = true;
            this.Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
