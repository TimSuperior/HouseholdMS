using System;
using System.Data.SQLite;
using System.Windows;

namespace HouseholdMS.View
{
    public partial class AddInventoryWindow : Window
    {
        public bool Saved { get; private set; } = false;

        public AddInventoryWindow()
        {
            InitializeComponent();
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
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

                var checkCmd = new SQLiteCommand("SELECT COUNT(*) FROM StockInventory WHERE ItemType = @type", conn);
                checkCmd.Parameters.AddWithValue("@type", itemType);
                int count = Convert.ToInt32(checkCmd.ExecuteScalar());

                if (count > 0)
                {
                    var updateCmd = new SQLiteCommand("UPDATE StockInventory SET TotalQuantity = TotalQuantity + @qty, LastRestockedDate = @date WHERE ItemType = @type", conn);
                    updateCmd.Parameters.AddWithValue("@qty", qty);
                    updateCmd.Parameters.AddWithValue("@date", DateTime.Now.ToString("yyyy-MM-dd"));
                    updateCmd.Parameters.AddWithValue("@type", itemType);
                    updateCmd.ExecuteNonQuery();
                }
                else
                {
                    var insertCmd = new SQLiteCommand("INSERT INTO StockInventory (ItemType, TotalQuantity, UsedQuantity, LastRestockedDate) VALUES (@type, @qty, 0, @date)", conn);
                    insertCmd.Parameters.AddWithValue("@type", itemType);
                    insertCmd.Parameters.AddWithValue("@qty", qty);
                    insertCmd.Parameters.AddWithValue("@date", DateTime.Now.ToString("yyyy-MM-dd"));
                    insertCmd.ExecuteNonQuery();
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
