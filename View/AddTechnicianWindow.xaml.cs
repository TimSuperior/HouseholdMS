using System;
using System.Data.SQLite;
using System.Windows;

namespace HouseholdMS.View
{
    public partial class AddTechnicianWindow : Window
    {
        public bool Saved { get; private set; } = false;

        public AddTechnicianWindow()
        {
            InitializeComponent();
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            string name = NameBox.Text.Trim();
            string contact = ContactBox.Text.Trim();
            string area = AreaBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(contact) || string.IsNullOrWhiteSpace(area))
            {
                MessageBox.Show("Please fill in all fields.");
                return;
            }

            using (var conn = DatabaseHelper.GetConnection())
            {
                conn.Open();

                var cmd = new SQLiteCommand("INSERT INTO Technicians (Name, ContactNum, Address, AssignedArea) VALUES (@name, @contact, '', @area)", conn);
                cmd.Parameters.AddWithValue("@name", name);
                cmd.Parameters.AddWithValue("@contact", contact);
                cmd.Parameters.AddWithValue("@area", area);
                cmd.ExecuteNonQuery();
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
