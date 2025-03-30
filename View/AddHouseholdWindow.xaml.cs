using System;
using System.Collections.ObjectModel;
using System.Data.SQLite;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Controls;
using System.Windows.Input;

namespace HouseholdMS.View
{
    public partial class AddHouseholdWindow : Window
    {
        private ObservableCollection<Household> households = new ObservableCollection<Household>();
        private int? EditingHouseholdID = null;

        public AddHouseholdWindow()
        {
            InitializeComponent();
            InstDatePicker.SelectedDate = DateTime.Now;
            LastInspPicker.SelectedDate = DateTime.Now;
        }

        // Constructor for Edit mode
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
            if (string.IsNullOrWhiteSpace(OwnerName) || string.IsNullOrWhiteSpace(Address) ||
                string.IsNullOrWhiteSpace(Contact) || InstallDate == null || LastInspect == null)
            {
                MessageBox.Show("Please fill in all fields.");
                return;
            }

            using (SQLiteConnection conn = DatabaseHelper.GetConnection())
            {
                conn.Open();

                string query;

                if (EditingHouseholdID == null)
                {
                    // INSERT
                    query = "INSERT INTO Households (OwnerName, Address, ContactNum, InstallDate, LastInspect) " +
                            "VALUES (@Owner, @Addr, @Contact, @Inst, @Last)";
                }
                else
                {
                    // UPDATE
                    query = "UPDATE Households SET OwnerName = @Owner, Address = @Addr, ContactNum = @Contact, " +
                            "InstallDate = @Inst, LastInspect = @Last WHERE HouseholdID = @ID";
                }

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
