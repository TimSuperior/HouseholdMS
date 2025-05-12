using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data.SQLite;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using HouseholdMS.View.UserControls;

namespace HouseholdMS.View
{
    public partial class InventoryView : UserControl
    {
        private ObservableCollection<InventoryItem> allInventory = new ObservableCollection<InventoryItem>();
        private ICollectionView view;
        private readonly string _currentUserRole;

        public InventoryItem SelectedInventoryItem { get; set; }

        public InventoryView(string userRole = "User")
        {
            InitializeComponent();
            _currentUserRole = userRole;
            LoadInventory();
            ApplyRoleRestrictions();
        }

        private void ApplyRoleRestrictions()
        {
            if (_currentUserRole == "User")
            {
                if (FindName("AddInventoryButton") is Button addBtn)
                    addBtn.Visibility = Visibility.Collapsed;

                if (FindName("FormContent") is ContentControl formContent)
                    formContent.Visibility = Visibility.Collapsed;
            }
        }

        public void LoadInventory()
        {
            allInventory.Clear();

            using (var conn = DatabaseHelper.GetConnection())
            {
                conn.Open();
                using (var cmd = new SQLiteCommand("SELECT * FROM StockInventory", conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        allInventory.Add(new InventoryItem
                        {
                            ItemID = Convert.ToInt32(reader["ItemID"]),
                            ItemType = reader["ItemType"].ToString(),
                            TotalQuantity = Convert.ToInt32(reader["TotalQuantity"]),
                            UsedQuantity = Convert.ToInt32(reader["UsedQuantity"]),
                            LastRestockedDate = reader["LastRestockedDate"].ToString(),
                            LowStockThreshold = Convert.ToInt32(reader["LowStockThreshold"]),
                            Note = reader["Note"] == DBNull.Value ? string.Empty : reader["Note"].ToString()
                        });
                    }
                }
            }

            view = CollectionViewSource.GetDefaultView(allInventory);
            InventoryDataGrid.ItemsSource = view;
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (view == null) return;

            string search = SearchBox.Text.Trim().ToLower();
            view.Filter = obj =>
            {
                return obj is InventoryItem item &&
                       item.ItemType.ToLower().Contains(search);
            };
        }

        private void ClearText(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox box && box.Text == (string)box.Tag)
            {
                box.Text = "";
                box.Foreground = System.Windows.Media.Brushes.Black;
            }
        }

        private void ResetText(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox box && string.IsNullOrWhiteSpace(box.Text))
            {
                box.Text = (string)box.Tag;
                box.Foreground = System.Windows.Media.Brushes.Gray;
                view.Filter = null;
            }
        }

        private void AddInventoryButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentUserRole == "User")
            {
                MessageBox.Show("Access Denied: You cannot add inventory.", "Access Denied", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var form = new AddInventoryControl();
            form.OnSavedSuccessfully += (s, args) =>
            {
                FormContent.Content = null;
                LoadInventory();
            };
            form.OnCancelRequested += (s, args) => FormContent.Content = null;

            FormContent.Content = form;
        }

        private void InventoryDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (InventoryDataGrid.SelectedItem is InventoryItem selectedItem)
            {
                if (_currentUserRole == "User")
                {
                    FormContent.Content = null;
                    return;
                }

                var form = new AddInventoryControl(selectedItem);
                form.OnSavedSuccessfully += (s, args) =>
                {
                    FormContent.Content = null;
                    LoadInventory();
                    InventoryDataGrid.SelectedItem = null;
                };
                form.OnCancelRequested += (s, args) =>
                {
                    FormContent.Content = null;
                    InventoryDataGrid.SelectedItem = null;
                };

                FormContent.Content = form;
            }
            else
            {
                FormContent.Content = null;
            }
        }

        private void Restock_Click(object sender, RoutedEventArgs e)
        {
            if (_currentUserRole == "User")
            {
                MessageBox.Show("Access Denied: You cannot restock inventory.", "Access Denied", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (sender is Button btn && btn.Tag is InventoryItem item)
            {
                var dialog = new InputDialogWindow($"Enter quantity to restock for '{item.ItemType}':");
                if (dialog.ShowDialog() == true && dialog.Quantity.HasValue)
                {
                    int quantity = dialog.Quantity.Value;
                    if (quantity <= 0)
                    {
                        MessageBox.Show("Please enter a valid quantity greater than zero.", "Invalid Quantity", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    using (var conn = DatabaseHelper.GetConnection())
                    {
                        conn.Open();
                        var cmd = new SQLiteCommand(@"
                            UPDATE StockInventory 
                            SET TotalQuantity = TotalQuantity + @qty, 
                                LastRestockedDate = @now 
                            WHERE ItemID = @id", conn);
                        cmd.Parameters.AddWithValue("@qty", quantity);
                        cmd.Parameters.AddWithValue("@now", DateTime.Now.ToString("yyyy-MM-dd"));
                        cmd.Parameters.AddWithValue("@id", item.ItemID);
                        cmd.ExecuteNonQuery();
                    }

                    LoadInventory();
                }
            }
        }

        private void Use_Click(object sender, RoutedEventArgs e)
        {
            if (_currentUserRole == "User")
            {
                MessageBox.Show("Access Denied: You cannot use inventory items.", "Access Denied", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (sender is Button btn && btn.Tag is InventoryItem item)
            {
                var dialog = new InputDialogWindow($"Enter quantity to use from '{item.ItemType}':");
                if (dialog.ShowDialog() == true && dialog.Quantity.HasValue)
                {
                    int quantity = dialog.Quantity.Value;
                    if (quantity <= 0)
                    {
                        MessageBox.Show("Please enter a valid quantity greater than zero.", "Invalid Quantity", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    if (quantity > item.TotalQuantity)
                    {
                        MessageBox.Show("Not enough items in stock.", "Insufficient Stock", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    using (var conn = DatabaseHelper.GetConnection())
                    {
                        conn.Open();
                        var cmd = new SQLiteCommand(@"
                            UPDATE StockInventory 
                            SET TotalQuantity = TotalQuantity - @qty, 
                                UsedQuantity = UsedQuantity + @qty 
                            WHERE ItemID = @id", conn);
                        cmd.Parameters.AddWithValue("@qty", quantity);
                        cmd.Parameters.AddWithValue("@id", item.ItemID);
                        cmd.ExecuteNonQuery();
                    }

                    LoadInventory();
                }
            }
        }
    }

    // Model
    public class InventoryItem
    {
        public int ItemID { get; set; }
        public string ItemType { get; set; }
        public int TotalQuantity { get; set; }
        public int UsedQuantity { get; set; }
        public string LastRestockedDate { get; set; }
        public int LowStockThreshold { get; set; }
        public string Note { get; set; }
        public bool IsLowStock => TotalQuantity <= LowStockThreshold;
    }

    // Converter
    public class LowStockHighlightConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length >= 2
             && int.TryParse(values[0]?.ToString(), out int total)
             && int.TryParse(values[1]?.ToString(), out int threshold))
            {
                return total <= threshold;
            }
            return false;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
