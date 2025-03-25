using System;
using System.Collections.ObjectModel;
using System.Data.SQLite;
using System.Windows;
using System.Windows.Controls;

namespace HouseholdMS.View
{
    public partial class TechView : UserControl
    {
        private ObservableCollection<Technician> technicianList = new ObservableCollection<Technician>();
       

        public TechView()
        {
            InitializeComponent();
            LoadTechnicians();
        }

        private void LoadTechnicians()
        {
            technicianList.Clear();

            using (var conn = DatabaseHelper.GetConnection())
            {
                conn.Open();
                var cmd = new SQLiteCommand("SELECT * FROM Technicians", conn);
                var reader = cmd.ExecuteReader();

                while (reader.Read())
                {
                    technicianList.Add(new Technician
                    {
                        TechnicianID = Convert.ToInt32(reader["TechnicianID"]),
                        Name = reader["Name"].ToString(),
                        ContactNum = reader["ContactNum"].ToString(),
                        Address = reader["Address"].ToString(),
                        AssignedArea = reader["AssignedArea"].ToString()
                    });
                }
            }

            TechnicianListView.ItemsSource = technicianList;
        }

        private void AddTechnician_Click(object sender, RoutedEventArgs e)
        {
            string name = TechNameBox.Text.Trim();
            string contact = TechContactBox.Text.Trim();
            
            string area = TechAreaBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(contact) ||
                 string.IsNullOrWhiteSpace(area))
            {
                MessageBox.Show("Please fill in all fields.");
                return;
            }

            using (var conn = DatabaseHelper.GetConnection())
            {
                conn.Open();

                var cmd = new SQLiteCommand("INSERT INTO Technicians (Name, ContactNum, Address, AssignedArea) VALUES (@name, @contact, @address, @area)", conn);
                cmd.Parameters.AddWithValue("@name", name);
                cmd.Parameters.AddWithValue("@contact", contact);
                cmd.Parameters.AddWithValue("@area", area);
                cmd.ExecuteNonQuery();
            }

            LoadTechnicians();
            ClearInputs();
        }

        private void ClearInputs()
        {
            TechNameBox.Text = "Name";
            TechContactBox.Text = "Contact";
            TechAreaBox.Text = "Assigned Area";
        }
    }
}
