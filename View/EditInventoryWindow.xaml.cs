using System;
using System.Data.SQLite;
using System.Windows;

namespace HouseholdMS.View
{
    public partial class EditInventoryWindow : Window
    {
        private InventoryItem _item;

        public EditInventoryWindow(InventoryItem item)
        {
            InitializeComponent();
            _item = item;

            // Pre-fill data
            ItemTypeBox.Text = item.ItemType;
            TotalQuantityBox.Text = item.TotalQuantity.ToString();
            UsedQuantityBox.Text = item.UsedQuantity.ToString();
            LowStockBox.Text = item.LowStockThreshold.ToString();
            NoteBox.Text = item.Note;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            // Validate input
            if (!int.TryParse(TotalQuantityBox.Text, out int totalQty) ||
                !int.TryParse(UsedQuantityBox.Text, out int usedQty) ||
                !int.TryParse(LowStockBox.Text, out int lowStock))
            {
                MessageBox.Show("Please enter valid numbers for quantities.", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (usedQty > totalQty)
            {
                MessageBox.Show("Used quantity cannot exceed total quantity.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            using (var conn = DatabaseHelper.GetConnection())
            {
                conn.Open();
                var cmd = new SQLiteCommand(@"
                    UPDATE StockInventory
                    SET ItemType = @type,
                        TotalQuantity = @total,
                        UsedQuantity = @used,
                        LowStockThreshold = @threshold,
                        Note = @note
                    WHERE ItemID = @id", conn);

                cmd.Parameters.AddWithValue("@type", ItemTypeBox.Text);
                cmd.Parameters.AddWithValue("@total", totalQty);
                cmd.Parameters.AddWithValue("@used", usedQty);
                cmd.Parameters.AddWithValue("@threshold", lowStock);
                cmd.Parameters.AddWithValue("@note", NoteBox.Text);
                cmd.Parameters.AddWithValue("@id", _item.ItemID);

                cmd.ExecuteNonQuery();
            }

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
