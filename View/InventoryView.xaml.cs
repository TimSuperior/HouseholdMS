using System;
using System.Collections.ObjectModel;
using System.Data.SQLite;
using System.Windows;
using System.Windows.Controls;

namespace HouseholdMS.View
{
    public partial class InventoryView : UserControl
    {
        private ObservableCollection<InventoryItem> inventory = new ObservableCollection<InventoryItem>();

        public InventoryView()
        {
            InitializeComponent();
            LoadInventory();
        }

        public void LoadInventory()
        {
            inventory.Clear();

            using (SQLiteConnection conn = DatabaseHelper.GetConnection())
            {
                conn.Open();
                using (SQLiteCommand cmd = new SQLiteCommand("SELECT * FROM StockInventory", conn))
                using (SQLiteDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        inventory.Add(new InventoryItem
                        {
                            ItemID = Convert.ToInt32(reader["ItemID"]),
                            ItemType = reader["ItemType"].ToString(),
                            TotalQuantity = Convert.ToInt32(reader["TotalQuantity"]),
                            UsedQuantity = Convert.ToInt32(reader["UsedQuantity"]),
                            LastRestockedDate = reader["LastRestockedDate"].ToString()
                        });
                    }
                }
            }

            InventoryListView.ItemsSource = inventory;
        }

        private void AddInventoryButton_Click(object sender, RoutedEventArgs e)
        {
            var win = new AddInventoryWindow();
            if (win.ShowDialog() == true)
            {
                LoadInventory(); // Refresh list after dialog closes with success
            }
        }
    }
}
