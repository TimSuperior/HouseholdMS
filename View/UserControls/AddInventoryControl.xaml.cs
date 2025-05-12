using System;
using System.Data.SQLite;
using System.Windows;
using System.Windows.Controls;

namespace HouseholdMS.View.UserControls
{
    public partial class AddInventoryControl : UserControl
    {
        private InventoryItem _editingItem;
        private bool isEditMode = false;

        public event EventHandler OnSavedSuccessfully;
        public event EventHandler OnCancelRequested;

        public AddInventoryControl()
        {
            InitializeComponent();
            FormHeader.Text = "➕ Add Inventory Item";
            SaveButton.Content = "➕ Add";
        }

        public AddInventoryControl(InventoryItem item) : this()
        {
            _editingItem = item;
            isEditMode = true;

            FormHeader.Text = $"✏ Edit Inventory #{item.ItemID}";
            SaveButton.Content = "✏ Save Changes";
            DeleteButton.Visibility = Visibility.Visible;

            ItemTypeBox.Text = item.ItemType;
            TotalQtyBox.Text = item.TotalQuantity.ToString();
            ThresholdBox.Text = item.LowStockThreshold.ToString();
            NoteBox.Text = item.Note;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(TotalQtyBox.Text.Trim(), out int totalQty) || totalQty < 0 ||
                !int.TryParse(ThresholdBox.Text.Trim(), out int threshold) || threshold < 0)
            {
                MessageBox.Show("Please enter valid numeric values for quantities.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string type = ItemTypeBox.Text.Trim();
            string note = NoteBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(type))
            {
                MessageBox.Show("Item Type is required.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            using (var conn = DatabaseHelper.GetConnection())
            {
                conn.Open();

                string query = isEditMode
                    ? @"UPDATE StockInventory SET
                            ItemType = @type,
                            TotalQuantity = @total,
                            LowStockThreshold = @threshold,
                            Note = @note
                        WHERE ItemID = @id"
                    : @"INSERT INTO StockInventory (ItemType, TotalQuantity, UsedQuantity, LastRestockedDate, LowStockThreshold, Note)
                       VALUES (@type, @total, 0, @date, @threshold, @note)";

                using (var cmd = new SQLiteCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@type", type);
                    cmd.Parameters.AddWithValue("@total", totalQty);
                    cmd.Parameters.AddWithValue("@threshold", threshold);
                    cmd.Parameters.AddWithValue("@note", string.IsNullOrWhiteSpace(note) ? DBNull.Value : (object)note);

                    if (isEditMode)
                        cmd.Parameters.AddWithValue("@id", _editingItem.ItemID);
                    else
                        cmd.Parameters.AddWithValue("@date", DateTime.Now.ToString("yyyy-MM-dd"));

                    cmd.ExecuteNonQuery();
                }
            }

            MessageBox.Show(isEditMode ? "Inventory item updated." : "Inventory item added.",
                            "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            OnSavedSuccessfully?.Invoke(this, EventArgs.Empty);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            OnCancelRequested?.Invoke(this, EventArgs.Empty);
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (!isEditMode || _editingItem == null) return;

            var result = MessageBox.Show($"Are you sure you want to delete item '{_editingItem.ItemType}'?",
                                         "Confirm Deletion", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    using (var conn = DatabaseHelper.GetConnection())
                    {
                        conn.Open();
                        var cmd = new SQLiteCommand("DELETE FROM StockInventory WHERE ItemID = @id", conn);
                        cmd.Parameters.AddWithValue("@id", _editingItem.ItemID);
                        cmd.ExecuteNonQuery();
                    }

                    MessageBox.Show("Inventory item deleted successfully.", "Deleted", MessageBoxButton.OK, MessageBoxImage.Information);
                    OnSavedSuccessfully?.Invoke(this, EventArgs.Empty);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error deleting item: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}
