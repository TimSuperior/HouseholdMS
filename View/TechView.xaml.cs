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

        public void LoadTechnicians()
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

        private void OpenAddTechnicianDialog_Click(object sender, RoutedEventArgs e)
        {
            var win = new AddTechnicianWindow();
            if (win.ShowDialog() == true)
            {
                LoadTechnicians(); // Refresh after successful addition
            }
        }
    }
}
