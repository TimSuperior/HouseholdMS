using System.Collections.ObjectModel;
using System.Data.SQLite;
using System.Windows;

namespace HouseholdMS.View
{
    public partial class AddHouseholdWindow : Window
    {
        private ObservableCollection<Household> households = new ObservableCollection<Household>();
        public AddHouseholdWindow()
        {
            InitializeComponent();
        }
        public bool Saved { get; private set; } = false;

        public string OwnerName => OwnerBox.Text.Trim(); // ✅ safe name

        //public string Owner => OwnerBox.Text.Trim();
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

                
                    string owner = OwnerName;
                    string address = Address;
                    string contact = Contact;
                    string instDate = InstallDate;
                    string lastInsp = LastInspect;

                    using (SQLiteConnection conn = DatabaseHelper.GetConnection())
                    {
                        conn.Open();

                        string query = "INSERT INTO Households (OwnerName, Address, ContactNum, InstallDate, LastInspect) " +
                                       "VALUES (@Owner, @Addr, @Contact, @Inst, @Last)";

                        using (SQLiteCommand cmd = new SQLiteCommand(query, conn))
                        {
                            cmd.Parameters.AddWithValue("@Owner", owner);
                            cmd.Parameters.AddWithValue("@Addr", address);
                            cmd.Parameters.AddWithValue("@Contact", contact);
                            cmd.Parameters.AddWithValue("@Inst", instDate);
                            cmd.Parameters.AddWithValue("@Last", lastInsp);
                            cmd.ExecuteNonQuery();
                        }
                    }


            Saved = true;
            this.DialogResult = true; // ✅ Important!
            this.Close();
            }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}