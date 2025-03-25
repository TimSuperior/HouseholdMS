using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data.SQLite;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace HouseholdMS.View
{
    /// <summary>
    /// Interaction logic for ServiceRecordsView.xaml
    /// </summary>
    public partial class ServiceRecordsView : UserControl
    {
        private ObservableCollection<ServiceRecord> records = new ObservableCollection<ServiceRecord>();
        

        public ServiceRecordsView()
        {
            InitializeComponent();
            LoadServiceRecords();
        }

        private void LoadServiceRecords()
        {
            records.Clear();

            using (SQLiteConnection conn = DatabaseHelper.GetConnection())
            {
                conn.Open();

                string query = "SELECT * FROM InspectionReport";
                using (SQLiteCommand cmd = new SQLiteCommand(query, conn))
                using (SQLiteDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        records.Add(new ServiceRecord
                        {
                            ReportID = Convert.ToInt32(reader["ReportID"]),
                            HouseholdID = Convert.ToInt32(reader["HouseholdID"]),
                            TechnicianID = reader["TechnicianID"] != DBNull.Value ? Convert.ToInt32(reader["TechnicianID"]) : 0,
                            LastInspect = reader["LastInspect"].ToString(),
                            Problem = reader["Problem"]?.ToString(),
                            Action = reader["Action"]?.ToString(),
                            RepairDate = reader["RepairDate"]?.ToString()
                        });
                    }
                }
            }

            ServiceRecordsListView.ItemsSource = records;
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            LoadServiceRecords();
        }
    }
}
