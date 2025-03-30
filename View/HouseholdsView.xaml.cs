using System;
using System.Collections.ObjectModel;
using System.Data.SQLite;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace HouseholdMS.View
{
    public partial class HouseholdsView : UserControl
    {
        private ObservableCollection<Household> allHouseholds = new ObservableCollection<Household>();
        private ObservableCollection<Household> filteredHouseholds = new ObservableCollection<Household>();

        public HouseholdsView()
        {
            InitializeComponent();
            LoadHouseholds();
        }

        public void LoadHouseholds()
        {
            allHouseholds.Clear();
            filteredHouseholds.Clear();

            using (SQLiteConnection conn = DatabaseHelper.GetConnection())
            {
                conn.Open();
                using (SQLiteCommand cmd = new SQLiteCommand("SELECT * FROM Households", conn))
                using (SQLiteDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var household = new Household
                        {
                            HouseholdID = Convert.ToInt32(reader["HouseholdID"]),
                            OwnerName = reader["OwnerName"].ToString(),
                            Address = reader["Address"].ToString(),
                            ContactNum = reader["ContactNum"].ToString(),
                            InstDate = reader["InstallDate"].ToString(),
                            LastInspDate = reader["LastInspect"].ToString()
                        };

                        allHouseholds.Add(household);
                        filteredHouseholds.Add(household);
                    }
                }
            }

            HouseholdListView.ItemsSource = filteredHouseholds;
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string search = SearchBox.Text.Trim().ToLower();
            filteredHouseholds.Clear();

            foreach (var h in allHouseholds)
            {
                if (h.OwnerName.ToLower().Contains(search) ||
                    h.Address.ToLower().Contains(search) ||
                    h.ContactNum.ToLower().Contains(search))
                {
                    filteredHouseholds.Add(h);
                }
            }
        }

        private void AddHouseholdButton_Click(object sender, RoutedEventArgs e)
        {
            var win = new AddHouseholdWindow();
            if (win.ShowDialog() == true)
            {
                LoadHouseholds();
            }
        }

        private void EditHousehold_Click(object sender, RoutedEventArgs e)
        {
            if (HouseholdListView.SelectedItem is Household selected)
            {
                var win = new AddHouseholdWindow(selected); // ✅ constructor with selected household
                if (win.ShowDialog() == true)
                {
                    LoadHouseholds();
                }
            }
            else
            {
                MessageBox.Show("Please select a household to edit.", "Edit Household", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void DeleteHousehold_Click(object sender, RoutedEventArgs e)
        {
            if (HouseholdListView.SelectedItem is Household selected)
            {
                var confirm = MessageBox.Show(
                    $"Are you sure you want to delete household \"{selected.OwnerName}\"?",
                    "Confirm Deletion", MessageBoxButton.YesNo, MessageBoxImage.Warning);

                if (confirm == MessageBoxResult.Yes)
                {
                    using (var conn = DatabaseHelper.GetConnection())
                    {
                        conn.Open();
                        var cmd = new SQLiteCommand("DELETE FROM Households WHERE HouseholdID = @id", conn);
                        cmd.Parameters.AddWithValue("@id", selected.HouseholdID);
                        cmd.ExecuteNonQuery();
                    }

                    LoadHouseholds();
                }
            }
            else
            {
                MessageBox.Show("Please select a household to delete.", "Delete Household", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ClearText(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox box && box.Text == box.Tag as string)
            {
                box.Text = "";
                box.Foreground = System.Windows.Media.Brushes.Black;
            }
        }

        private void ResetText(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox box && string.IsNullOrWhiteSpace(box.Text))
            {
                box.Text = box.Tag as string;
                box.Foreground = System.Windows.Media.Brushes.Gray;

                if (box.Name == "SearchBox")
                {
                    filteredHouseholds.Clear();
                    foreach (var h in allHouseholds)
                        filteredHouseholds.Add(h);
                }
            }
        }
    }
}
