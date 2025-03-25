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
    /// Interaction logic for InventoryView.xaml
    /// </summary>
    public partial class InventoryView : UserControl
    {
        private ObservableCollection<InventoryItem> inventory = new ObservableCollection<InventoryItem>();


        public InventoryView()
        {
            InitializeComponent();
            LoadInventory();
        }

        private void LoadInventory()
        {
            inventory.Clear();

            using (var conn = DatabaseHelper.GetConnection())
            {
                conn.Open();
                var cmd = new SQLiteCommand("SELECT * FROM StockInventory", conn);
                var reader = cmd.ExecuteReader();

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

            InventoryListView.ItemsSource = inventory;
        }

        private void RestockItem_Click(object sender, RoutedEventArgs e)
        {
            string itemType = ItemTypeBox.Text.Trim();
            if (!int.TryParse(QuantityBox.Text.Trim(), out int qty) || qty <= 0)
            {
                MessageBox.Show("Enter a valid positive quantity.");
                return;
            }

            using (var conn = DatabaseHelper.GetConnection())
            {
                conn.Open();

                // Check if item exists
                var checkCmd = new SQLiteCommand("SELECT COUNT(*) FROM StockInventory WHERE ItemType = @type", conn);
                checkCmd.Parameters.AddWithValue("@type", itemType);
                int count = Convert.ToInt32(checkCmd.ExecuteScalar());

                if (count > 0)
                {
                    // Update existing
                    var updateCmd = new SQLiteCommand("UPDATE StockInventory SET TotalQuantity = TotalQuantity + @qty, LastRestockedDate = @date WHERE ItemType = @type", conn);
                    updateCmd.Parameters.AddWithValue("@qty", qty);
                    updateCmd.Parameters.AddWithValue("@date", DateTime.Now.ToString("yyyy-MM-dd"));
                    updateCmd.Parameters.AddWithValue("@type", itemType);
                    updateCmd.ExecuteNonQuery();
                }
                else
                {
                    // Insert new
                    var insertCmd = new SQLiteCommand("INSERT INTO StockInventory (ItemType, TotalQuantity, UsedQuantity, LastRestockedDate) VALUES (@type, @qty, 0, @date)", conn);
                    insertCmd.Parameters.AddWithValue("@type", itemType);
                    insertCmd.Parameters.AddWithValue("@qty", qty);
                    insertCmd.Parameters.AddWithValue("@date", DateTime.Now.ToString("yyyy-MM-dd"));
                    insertCmd.ExecuteNonQuery();
                }
            }

            LoadInventory();
            ItemTypeBox.Text = "ItemType";
            QuantityBox.Text = "10";
        }
    }
}
