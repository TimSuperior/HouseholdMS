using System;
using System.Data.SQLite;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HouseholdMS.View
{
    public partial class AddInventoryWindow : Window
    {
        public bool Saved { get; private set; } = false;

        public AddInventoryWindow()
        {
            InitializeComponent();
        }

        /* ✅ Обработка нажатия кнопки OK */
        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            string itemType = ItemTypeBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(itemType) || itemType == (string)ItemTypeBox.Tag)
            {
                MessageBox.Show("Please enter a valid item type.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(QuantityBox.Text.Trim(), out int qty) || qty <= 0)
            {
                MessageBox.Show("Enter a valid positive quantity.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(ThresholdBox.Text.Trim(), out int threshold) || threshold < 0)
            {
                MessageBox.Show("Enter a valid low stock threshold (0 or more).", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Capture and process the Note field.
            string note = NoteBox.Text.Trim();
            if (note == (string)NoteBox.Tag)
            {
                note = string.Empty;
            }

            try
            {
                using (var conn = DatabaseHelper.GetConnection())
                {
                    conn.Open();

                    // Проверка наличия типа
                    var checkCmd = new SQLiteCommand("SELECT COUNT(*) FROM StockInventory WHERE ItemType = @type", conn);
                    checkCmd.Parameters.AddWithValue("@type", itemType);
                    int count = Convert.ToInt32(checkCmd.ExecuteScalar());

                    if (count > 0)
                    {
                        MessageBox.Show("Item already exists. Please use the restock function to update the quantity.",
                                        "Item Exists", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                    else
                    {
                        // Добавление нового, включая поле Note
                        var insertCmd = new SQLiteCommand(@"
                            INSERT INTO StockInventory 
                            (ItemType, TotalQuantity, UsedQuantity, LastRestockedDate, LowStockThreshold, Note)
                            VALUES (@type, @qty, 0, @date, @threshold, @note)", conn);

                        insertCmd.Parameters.AddWithValue("@type", itemType);
                        insertCmd.Parameters.AddWithValue("@qty", qty);
                        insertCmd.Parameters.AddWithValue("@date", DateTime.Now.ToString("yyyy-MM-dd"));
                        insertCmd.Parameters.AddWithValue("@threshold", threshold);
                        insertCmd.Parameters.AddWithValue("@note", note);
                        insertCmd.ExecuteNonQuery();
                    }
                }

                Saved = true;
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error while saving inventory item:\n" + ex.Message, "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /* ❌ Обработка отмены */
        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        /* 🧼 Очистка текста при фокусе */
        private void ClearText(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox box && box.Text == (string)box.Tag)
            {
                box.Text = "";
                box.Foreground = Brushes.Black;
            }
        }

        /* 🔄 Сброс текста при потере фокуса */
        private void ResetText(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox box && string.IsNullOrWhiteSpace(box.Text))
            {
                box.Text = (string)box.Tag;
                box.Foreground = Brushes.Gray;
            }
        }
    }
}
