using HouseholdMS.Model;
using HouseholdMS.View.UserControls; // AddInventoryControl
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data.SQLite;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace HouseholdMS.View
{
    public partial class InventoryView : UserControl
    {
        private readonly ObservableCollection<InventoryItem> allInventory = new ObservableCollection<InventoryItem>();
        private ICollectionView view;
        private readonly string _currentUserRole;

        private GridViewColumnHeader _lastHeaderClicked;
        private ListSortDirection _lastDirection = ListSortDirection.Ascending;

        private readonly Dictionary<string, string> _headerToProperty = new Dictionary<string, string>
        {
            { "ID", "ItemID" },
            { "Item Type", "ItemType" },
            { "Available Qty", "TotalQuantity" },   // renamed column header, same backing property
            { "Used Qty", "UsedQuantity" },
            { "Low Threshold", "LowStockThreshold" },
            { "Last Restocked", "LastRestockedDate" },
            { "Note", "Note" }
        };

        public InventoryItem SelectedInventoryItem { get; set; }

        public InventoryView(string userRole = "User")
        {
            _currentUserRole = string.IsNullOrWhiteSpace(userRole) ? "User" : userRole.Trim();
            InitializeComponent();
            InitializeAndLoad();
        }

        private void InitializeAndLoad()
        {
            LoadInventory();

            InventoryListView.AddHandler(GridViewColumnHeader.ClickEvent, new RoutedEventHandler(GridViewColumnHeader_Click));

            if (!IsAdmin())
            {
                if (FindName("AddInventoryButton") is Button addBtn)
                    addBtn.Visibility = Visibility.Collapsed;
            }

            UpdateSearchPlaceholder();
        }

        private bool IsAdmin() => string.Equals(_currentUserRole, "Admin", StringComparison.OrdinalIgnoreCase);

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
                            ItemType = reader["ItemType"]?.ToString(),
                            TotalQuantity = Convert.ToInt32(reader["TotalQuantity"]),
                            UsedQuantity = Convert.ToInt32(reader["UsedQuantity"]),
                            LastRestockedDate = reader["LastRestockedDate"]?.ToString(),
                            LowStockThreshold = Convert.ToInt32(reader["LowStockThreshold"]),
                            Note = reader["Note"] == DBNull.Value ? string.Empty : reader["Note"].ToString()
                        });
                    }
                }
            }

            view = CollectionViewSource.GetDefaultView(allInventory);
            InventoryListView.ItemsSource = view;
        }

        // ===== Search =====
        private void UpdateSearchPlaceholder()
        {
            if (SearchBox == null) return;

            string ph = "Search by item type";
            SearchBox.Tag = ph;

            if (string.IsNullOrWhiteSpace(SearchBox.Text) ||
                SearchBox.Text == "Search by item type")
            {
                SearchBox.Text = ph;
                SearchBox.Foreground = Brushes.Gray;
                SearchBox.FontStyle = FontStyles.Italic;
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (view == null) return;

            string text = SearchBox.Text ?? string.Empty;
            if (text == (SearchBox.Tag as string))
            {
                view.Filter = null;
                view.Refresh();
                return;
            }

            string search = text.Trim().ToLowerInvariant();
            view.Filter = obj =>
            {
                if (obj is InventoryItem it)
                    return (it.ItemType ?? string.Empty).ToLowerInvariant().Contains(search);
                return false;
            };
            view.Refresh();
        }

        private void ClearText(object sender, RoutedEventArgs e)
        {
            var box = sender as TextBox;
            if (box == null) return;

            if (box.Text == box.Tag as string)
            {
                box.Text = string.Empty;
            }
            box.Foreground = Brushes.Black;
            box.FontStyle = FontStyles.Normal;

            if (view != null)
            {
                view.Filter = null;
                view.Refresh();
            }
        }

        private void ResetText(object sender, RoutedEventArgs e)
        {
            var box = sender as TextBox;
            if (box != null && string.IsNullOrWhiteSpace(box.Text))
            {
                box.Text = box.Tag as string;
                box.Foreground = Brushes.Gray;
                box.FontStyle = FontStyles.Italic;

                if (view != null)
                {
                    view.Filter = null;
                    view.Refresh();
                }
            }
        }

        // ===== Sorting on header click =====
        private void GridViewColumnHeader_Click(object sender, RoutedEventArgs e)
        {
            var header = e.OriginalSource as GridViewColumnHeader;
            if (header == null) return;

            string headerText = header.Content?.ToString();
            if (string.IsNullOrEmpty(headerText) || !_headerToProperty.ContainsKey(headerText))
                return;

            string sortBy = _headerToProperty[headerText];

            ListSortDirection direction =
                (_lastHeaderClicked == header && _lastDirection == ListSortDirection.Ascending)
                ? ListSortDirection.Descending
                : ListSortDirection.Ascending;

            _lastHeaderClicked = header;
            _lastDirection = direction;

            view.SortDescriptions.Clear();
            view.SortDescriptions.Add(new SortDescription(sortBy, direction));
            view.Refresh();
        }

        // ===== Add / Edit popup =====
        private void AddInventoryButton_Click(object sender, RoutedEventArgs e)
        {
            if (!IsAdmin())
            {
                MessageBox.Show("Only admins can add inventory items.", "Access Denied",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var form = new AddInventoryControl();
            var dialog = CreateWideDialog(form, "Add Inventory Item");

            form.OnSavedSuccessfully += delegate
            {
                dialog.DialogResult = true;
                dialog.Close();
                LoadInventory();
            };
            form.OnCancelRequested += delegate
            {
                dialog.DialogResult = false;
                dialog.Close();
            };

            dialog.ShowDialog();
        }

        private void InventoryListView_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var selected = InventoryListView.SelectedItem as InventoryItem;
            if (selected == null) return;

            if (IsAdmin())
            {
                var form = new AddInventoryControl(selected);
                var dialog = CreateWideDialog(form, $"Edit Item #{selected.ItemID}");

                form.OnSavedSuccessfully += delegate
                {
                    dialog.DialogResult = true;
                    dialog.Close();
                    LoadInventory();
                };
                form.OnCancelRequested += delegate
                {
                    dialog.DialogResult = false;
                    dialog.Close();
                };

                dialog.ShowDialog();
            }
            else
            {
                var dt = (DataTemplate)FindResource("InventoryReadOnlyTemplate");
                var content = (FrameworkElement)dt.LoadContent();
                content.DataContext = selected;

                var scroller = new ScrollViewer
                {
                    Content = content,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                };

                var dialog = CreateWideDialog(scroller, $"Item #{selected.ItemID} — Details");
                dialog.ShowDialog();
            }
        }

        private Window CreateWideDialog(FrameworkElement content, string title)
        {
            var host = new Grid { Margin = new Thickness(16) };
            host.Children.Add(content);

            var owner = Window.GetWindow(this);

            var dlg = new Window
            {
                Title = title,
                Owner = owner,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.CanResize,
                ShowInTaskbar = false,
                Content = host,
                Width = 900,
                Height = 640,
                MinWidth = 780,
                MinHeight = 520,
                Background = Brushes.White
            };

            try { if (owner != null) dlg.Icon = owner.Icon; } catch { }
            return dlg;
        }

        // ===== Row actions =====
        private void Restock_Click(object sender, RoutedEventArgs e)
        {
            if (!IsAdmin())
            {
                MessageBox.Show("Access Denied: You cannot restock inventory.", "Access Denied",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var item = (sender as FrameworkElement)?.GetValue(Button.CommandParameterProperty) as InventoryItem
                       ?? (sender as Button)?.Tag as InventoryItem;
            if (item == null) return;

            var dialog = new ItemActionDialog("Restock", item.ItemType);
            if (dialog.ShowDialog() == true && dialog.Quantity.HasValue)
            {
                int qty = dialog.Quantity.Value;
                if (qty <= 0)
                {
                    MessageBox.Show("Please enter a valid quantity greater than zero.",
                        "Invalid Quantity", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                using (var conn = DatabaseHelper.GetConnection())
                {
                    conn.Open();

                    // 1) StockInventory totals (+) and last restocked date
                    using (var cmd = new SQLiteCommand(@"
                        UPDATE StockInventory 
                        SET TotalQuantity = TotalQuantity + @qty,
                            LastRestockedDate = @now
                        WHERE ItemID = @id;", conn))
                    {
                        cmd.Parameters.AddWithValue("@qty", qty);
                        cmd.Parameters.AddWithValue("@now", DateTime.Now.ToString("yyyy-MM-dd"));
                        cmd.Parameters.AddWithValue("@id", item.ItemID);
                        cmd.ExecuteNonQuery();
                    }

                    // 2) Append restock history with person + note
                    using (var cmd2 = new SQLiteCommand(@"
                        INSERT INTO ItemRestock (ItemID, Quantity, RestockedAt, CreatedByName, Note)
                        VALUES (@id, @qty, datetime('now'), @by, @note);", conn))
                    {
                        cmd2.Parameters.AddWithValue("@id", item.ItemID);
                        cmd2.Parameters.AddWithValue("@qty", qty);
                        cmd2.Parameters.AddWithValue("@by", string.IsNullOrWhiteSpace(dialog.Person) ? (object)DBNull.Value : dialog.Person.Trim());
                        cmd2.Parameters.AddWithValue("@note", string.IsNullOrWhiteSpace(dialog.NoteOrReason) ? (object)DBNull.Value : dialog.NoteOrReason.Trim());
                        cmd2.ExecuteNonQuery();
                    }
                }

                LoadInventory();
            }
        }

        private void Use_Click(object sender, RoutedEventArgs e)
        {
            if (!IsAdmin())
            {
                MessageBox.Show("Access Denied: You cannot use inventory items.", "Access Denied",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var item = (sender as FrameworkElement)?.GetValue(Button.CommandParameterProperty) as InventoryItem
                       ?? (sender as Button)?.Tag as InventoryItem;
            if (item == null) return;

            var dialog = new ItemActionDialog("Use", item.ItemType);
            if (dialog.ShowDialog() == true && dialog.Quantity.HasValue)
            {
                int qty = dialog.Quantity.Value;
                if (qty <= 0)
                {
                    MessageBox.Show("Please enter a valid quantity greater than zero.",
                        "Invalid Quantity", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (qty > item.TotalQuantity)
                {
                    MessageBox.Show("Not enough items in stock.", "Insufficient Stock",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                using (var conn = DatabaseHelper.GetConnection())
                {
                    conn.Open();

                    // 1) Keep your existing behavior: total(-), used(+)
                    using (var cmd = new SQLiteCommand(@"
                        UPDATE StockInventory 
                        SET TotalQuantity = TotalQuantity - @qty,
                            UsedQuantity  = UsedQuantity + @qty
                        WHERE ItemID = @id;", conn))
                    {
                        cmd.Parameters.AddWithValue("@qty", qty);
                        cmd.Parameters.AddWithValue("@id", item.ItemID);
                        cmd.ExecuteNonQuery();
                    }

                    // 2) Log manual usage with person + reason
                    using (var cmd2 = new SQLiteCommand(@"
                        INSERT INTO ItemUsage (ItemID, Quantity, UsedAt, UsedByName, Reason)
                        VALUES (@id, @qty, datetime('now'), @by, @reason);", conn))
                    {
                        cmd2.Parameters.AddWithValue("@id", item.ItemID);
                        cmd2.Parameters.AddWithValue("@qty", qty);
                        cmd2.Parameters.AddWithValue("@by", string.IsNullOrWhiteSpace(dialog.Person) ? (object)DBNull.Value : dialog.Person.Trim());
                        cmd2.Parameters.AddWithValue("@reason", string.IsNullOrWhiteSpace(dialog.NoteOrReason) ? (object)DBNull.Value : dialog.NoteOrReason.Trim());
                        cmd2.ExecuteNonQuery();
                    }
                }

                LoadInventory();
            }
        }
    }

    // ===== Model (kept, with state flags for row colors) =====
    public class InventoryItem
    {
        public int ItemID { get; set; }
        public string ItemType { get; set; }
        public int TotalQuantity { get; set; }           // acts as Available Qty
        public int UsedQuantity { get; set; }
        public string LastRestockedDate { get; set; }
        public int LowStockThreshold { get; set; }
        public string Note { get; set; }

        // NEW: explicit flags for row styles
        public bool IsOutOfStock => TotalQuantity <= 0;
        public bool IsLowStock => TotalQuantity > 0 && TotalQuantity <= LowStockThreshold;
    }
}
