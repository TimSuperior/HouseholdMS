using System;
using System.Collections.ObjectModel;
using System.Data.SQLite;
using System.Windows;
using System.Windows.Controls;

namespace HouseholdMS.View
{
    public partial class HouseholdsView : UserControl
    {
        private ObservableCollection<Household> households = new ObservableCollection<Household>();

        public HouseholdsView()
        {


            InitializeComponent();

            //string dbPath = System.IO.Path.GetFullPath("household_management.db");
            //MessageBox.Show(dbPath); // Debug: shows where it's reading from


            LoadHouseholds();
        }

        public void LoadHouseholds()
        {
            households.Clear();

            using (SQLiteConnection conn = DatabaseHelper.GetConnection())
            {
                conn.Open();
                using (SQLiteCommand cmd = new SQLiteCommand("SELECT * FROM Households", conn))
                using (SQLiteDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        households.Add(new Household
                        {
                            HouseholdID = Convert.ToInt32(reader["HouseholdID"]),
                            OwnerName = reader["OwnerName"].ToString(),
                            Address = reader["Address"].ToString(),
                            ContactNum = reader["ContactNum"].ToString(),
                            InstDate = reader["InstallDate"].ToString(),
                            LastInspDate = reader["LastInspect"].ToString()
                        });
                    }
                }
            }

            HouseholdListView.ItemsSource = households;
        }

        /*private void AddHousehold()
        {
            string owner = OwnerNameBox.Text;
            string address = AddressBox.Text;
            string contact = ContactBox.Text;
            string instDate = InstDateBox.Text;
            string lastInsp = LastInspBox.Text;

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

            LoadHouseholds();
            ClearInputs();
        }*/

        /*private void ClearInputs()
        {
            OwnerNameBox.Text = "";
            AddressBox.Text = "";
            ContactBox.Text = "";
            InstDateBox.Text = "";
            LastInspBox.Text = "";
        }*/


        private void AddHouseholdButton_Click(object sender, RoutedEventArgs e)
        {
            var win = new AddHouseholdWindow();
            if (win.ShowDialog() == true)
            {
                LoadHouseholds(); // 
            }
        }

        private void ClearText(object sender, RoutedEventArgs e)
        {
            TextBox box = sender as TextBox;
            if (box != null && box.Text == box.Tag as string)
            {
                box.Text = "";
                box.Foreground = System.Windows.Media.Brushes.Black;
            }
        }

        private void ResetText(object sender, RoutedEventArgs e)
        {
            TextBox box = sender as TextBox;
            if (box != null && string.IsNullOrWhiteSpace(box.Text))
            {
                box.Text = box.Tag as string;
                box.Foreground = System.Windows.Media.Brushes.Gray;
            }
        }


    }
}
