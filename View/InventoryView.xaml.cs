using HouseholdMS.Model;
using HouseholdMS.View.UserControls; // AddInventoryControl
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data.SQLite;
using System.Linq;
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
            { "Display #", "DisplayOrder" },
            { "Item Type", "ItemType" },
            { "Available Qty", "TotalQuantity" },
            { "Used Qty", "UsedQuantity" },
            { "Low Threshold", "LowStockThreshold" },
            { "Last Restocked", "LastRestockedDate" },
            { "Order", "DisplayOrder" },
            { "Note", "Note" }
        };

        // Column selection for search
        private static readonly string[] AllColumnKeys = new[]
        {
            nameof(InventoryItem.ItemID),
            nameof(InventoryItem.DisplayOrder),
            nameof(InventoryItem.ItemType),
            nameof(InventoryItem.TotalQuantity),
            nameof(InventoryItem.UsedQuantity),
            nameof(InventoryItem.LowStockThreshold),
            nameof(InventoryItem.LastRestockedDate),
            nameof(InventoryItem.Note)
        };

        // empty set = treat as "all columns"
        private readonly HashSet<string> _selectedColumnKeys = new HashSet<string>(StringComparer.Ordinal);

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
            UpdateColumnFilterButtonContent(); // reflect "All"
        }

        private bool IsAdmin() => string.Equals(_currentUserRole, "Admin", StringComparison.OrdinalIgnoreCase);

        public void LoadInventory()
        {
            allInventory.Clear();

            using (var conn = DatabaseHelper.GetConnection())
            {
                conn.Open();
                using (var cmd = new SQLiteCommand("SELECT ItemID, ItemType, TotalQuantity, UsedQuantity, LastRestockedDate, LowStockThreshold, Note, DisplayOrder FROM StockInventory ORDER BY DisplayOrder ASC, ItemID ASC;", conn))
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
                            Note = reader["Note"] == DBNull.Value ? string.Empty : reader["Note"].ToString(),
                            DisplayOrder = reader["DisplayOrder"] == DBNull.Value ? Convert.ToInt32(reader["ItemID"]) : Convert.ToInt32(reader["DisplayOrder"])
                        });
                    }
                }
            }

            view = CollectionViewSource.GetDefaultView(allInventory);

            // Default sort by DisplayOrder
            view.SortDescriptions.Clear();
            view.SortDescriptions.Add(new SortDescription(nameof(InventoryItem.DisplayOrder), ListSortDirection.Ascending));

            InventoryListView.ItemsSource = view;
        }

        // ===== Search =====
        private void UpdateSearchPlaceholder()
        {
            if (SearchBox == null) return;

            string ph = "Search…";
            SearchBox.Tag = ph;

            if (string.IsNullOrWhiteSpace(SearchBox.Text) ||
                SearchBox.Text == "Search by item type" ||
                SearchBox.Text == ph)
            {
                SearchBox.Text = ph;
                SearchBox.Foreground = Brushes.Gray;
                SearchBox.FontStyle = FontStyles.Italic;
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplySearchFilter();
        }

        private void ApplySearchFilter()
        {
            if (view == null) return;

            string text = SearchBox.Text ?? string.Empty;
            string placeholder = SearchBox.Tag as string ?? string.Empty;

            if (string.IsNullOrWhiteSpace(text) || text == placeholder)
            {
                view.Filter = null;
                view.Refresh();
                return;
            }

            string search = text.Trim().ToLowerInvariant();
            var keys = _selectedColumnKeys.Count == 0 ? AllColumnKeys : _selectedColumnKeys.ToArray();

            view.Filter = obj =>
            {
                var it = obj as InventoryItem;
                if (it == null) return false;

                foreach (var k in keys)
                {
                    string cell = GetCellString(it, k);
                    if (!string.IsNullOrEmpty(cell) && cell.ToLowerInvariant().Contains(search))
                        return true;
                }
                return false;
            };
            view.Refresh();
        }

        private static string GetCellString(InventoryItem it, string key)
        {
            switch (key)
            {
                case nameof(InventoryItem.ItemID): return it.ItemID.ToString();
                case nameof(InventoryItem.DisplayOrder): return it.DisplayOrder.ToString();
                case nameof(InventoryItem.ItemType): return it.ItemType ?? string.Empty;
                case nameof(InventoryItem.TotalQuantity): return it.TotalQuantity.ToString();
                case nameof(InventoryItem.UsedQuantity): return it.UsedQuantity.ToString();
                case nameof(InventoryItem.LowStockThreshold): return it.LowStockThreshold.ToString();
                case nameof(InventoryItem.LastRestockedDate): return it.LastRestockedDate ?? string.Empty;
                case nameof(InventoryItem.Note): return it.Note ?? string.Empty;
                default: return string.Empty;
            }
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

        // ===== Row actions (restock/use) =====
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

        // ===== Reorder logic =====
        private void MoveUp_Click(object sender, RoutedEventArgs e)
        {
            if (!IsAdmin()) { Beep(); return; }

            var item = (sender as FrameworkElement)?.GetValue(Button.CommandParameterProperty) as InventoryItem;
            if (item == null) return;

            var ordered = allInventory.OrderBy(i => i.DisplayOrder).ThenBy(i => i.ItemID).ToList();
            int idx = ordered.FindIndex(i => i.ItemID == item.ItemID);
            if (idx <= 0) { Beep(); return; }

            var neighbor = ordered[idx - 1];
            SwapDisplayOrders(item, neighbor);
        }

        private void MoveDown_Click(object sender, RoutedEventArgs e)
        {
            if (!IsAdmin()) { Beep(); return; }

            var item = (sender as FrameworkElement)?.GetValue(Button.CommandParameterProperty) as InventoryItem;
            if (item == null) return;

            var ordered = allInventory.OrderBy(i => i.DisplayOrder).ThenBy(i => i.ItemID).ToList();
            int idx = ordered.FindIndex(i => i.ItemID == item.ItemID);
            if (idx < 0 || idx >= ordered.Count - 1) { Beep(); return; }

            var neighbor = ordered[idx + 1];
            SwapDisplayOrders(item, neighbor);
        }

        private void SwapDisplayOrders(InventoryItem a, InventoryItem b)
        {
            if (a == null || b == null) return;

            int aOrder = a.DisplayOrder;
            int bOrder = b.DisplayOrder;

            using (var conn = DatabaseHelper.GetConnection())
            {
                conn.Open();
                using (var tx = conn.BeginTransaction())
                {
                    using (var cmd1 = new SQLiteCommand("UPDATE StockInventory SET DisplayOrder = @newOrder WHERE ItemID = @id;", conn, tx))
                    {
                        cmd1.Parameters.AddWithValue("@newOrder", bOrder);
                        cmd1.Parameters.AddWithValue("@id", a.ItemID);
                        cmd1.ExecuteNonQuery();
                    }
                    using (var cmd2 = new SQLiteCommand("UPDATE StockInventory SET DisplayOrder = @newOrder WHERE ItemID = @id;", conn, tx))
                    {
                        cmd2.Parameters.AddWithValue("@newOrder", aOrder);
                        cmd2.Parameters.AddWithValue("@id", b.ItemID);
                        cmd2.ExecuteNonQuery();
                    }
                    tx.Commit();
                }
            }

            a.DisplayOrder = bOrder;
            b.DisplayOrder = aOrder;

            view.SortDescriptions.Clear();
            view.SortDescriptions.Add(new SortDescription(nameof(InventoryItem.DisplayOrder), ListSortDirection.Ascending));
            view.Refresh();
        }

        private void Beep()
        {
            try { System.Media.SystemSounds.Beep.Play(); } catch { }
        }

        // ===== Column chooser UI handlers =====
        private void ColumnFilterButton_Click(object sender, RoutedEventArgs e)
        {
            ColumnPopup.IsOpen = true;
        }

        private void ColumnPopup_Closed(object sender, EventArgs e)
        {
            UpdateColumnFilterButtonContent();

            var text = (SearchBox.Text ?? string.Empty);
            var placeholder = SearchBox.Tag as string ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(text) && text != placeholder)
                ApplySearchFilter();
        }

        private void ColumnCheckBox_Click(object sender, RoutedEventArgs e)
        {
            var cb = sender as CheckBox;
            var key = cb != null ? cb.Tag as string : null;
            if (string.IsNullOrWhiteSpace(key)) return;

            if (cb.IsChecked == true) _selectedColumnKeys.Add(key);
            else _selectedColumnKeys.Remove(key);
        }

        private void SelectAllColumns_Click(object sender, RoutedEventArgs e)
        {
            _selectedColumnKeys.Clear();
            foreach (var child in FindPopupCheckBoxes()) child.IsChecked = true;
            foreach (var k in AllColumnKeys) _selectedColumnKeys.Add(k);
        }

        private void ClearAllColumns_Click(object sender, RoutedEventArgs e)
        {
            _selectedColumnKeys.Clear(); // empty = "All"
            foreach (var child in FindPopupCheckBoxes()) child.IsChecked = false;
        }

        private void OkColumns_Click(object sender, RoutedEventArgs e)
        {
            // Update the chip text ("All ▾" or "N selected ▾")
            UpdateColumnFilterButtonContent();

            // If there's user text (not the placeholder), reapply search using the
            // currently selected columns; otherwise this clears any filter.
            var tagText = SearchBox?.Tag as string ?? string.Empty;
            var text = SearchBox?.Text ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(text) && !string.Equals(text, tagText, StringComparison.Ordinal))
            {
                ApplySearchFilter();   // <- no argument
            }
            else
            {
                ApplySearchFilter();   // clears filter because text is empty/placeholder
            }

            // Close the popup
            ColumnPopup.IsOpen = false;
        }


        private IEnumerable<CheckBox> FindPopupCheckBoxes()
        {
            var border = ColumnPopup.Child as Border;
            if (border == null) yield break;

            var sp = border.Child as StackPanel;
            if (sp == null) yield break;

            var sv = sp.Children.OfType<ScrollViewer>().FirstOrDefault();
            if (sv == null) yield break;

            var inner = sv.Content as StackPanel;
            if (inner == null) yield break;

            foreach (var child in inner.Children)
            {
                var cb = child as CheckBox;
                if (cb != null) yield return cb;
            }
        }

        private void UpdateColumnFilterButtonContent()
        {
            if (_selectedColumnKeys.Count == 0)
            {
                ColumnFilterButton.Content = "All ▾";
            }
            else
            {
                ColumnFilterButton.Content = string.Format("{0} selected ▾", _selectedColumnKeys.Count);
            }
        }
    }

    // ===== Model =====
    public class InventoryItem
    {
        public int ItemID { get; set; }
        public string ItemType { get; set; }
        public int TotalQuantity { get; set; }
        public int UsedQuantity { get; set; }
        public string LastRestockedDate { get; set; }
        public int LowStockThreshold { get; set; }
        public string Note { get; set; }
        public int DisplayOrder { get; set; }

        public bool IsOutOfStock => TotalQuantity <= 0;
        public bool IsLowStock => TotalQuantity > 0 && TotalQuantity <= LowStockThreshold;
    }
}
