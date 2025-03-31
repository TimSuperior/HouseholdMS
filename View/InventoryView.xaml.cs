using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data.SQLite;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;

namespace HouseholdMS.View
{
    public partial class InventoryView : UserControl
    {
        private ObservableCollection<InventoryItem> allInventory = new ObservableCollection<InventoryItem>();
        private ICollectionView view;

        private string _lastSortField = "";
        private ListSortDirection _lastDirection = ListSortDirection.Ascending;

        public InventoryView()
        {
            InitializeComponent();
            LoadInventory();

            // ✅ Proper event hook for header clicks
            InventoryListView.AddHandler(GridViewColumnHeader.ClickEvent, new RoutedEventHandler(GridViewColumnHeader_Click));
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
                            LastRestockedDate = reader["LastRestockedDate"].ToString()
                        });
                    }
                }
            }

            view = CollectionViewSource.GetDefaultView(allInventory);
            InventoryListView.ItemsSource = view;
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (view == null) return;

            string search = SearchBox.Text.Trim().ToLower();

            view.Filter = obj =>
            {
                if (obj is InventoryItem item)
                {
                    return item.ItemType.ToLower().Contains(search);
                }
                return false;
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

                if (view != null)
                    view.Filter = null;
            }
        }

        private void GridViewColumnHeader_Click(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is GridViewColumnHeader header &&
                header.Column?.DisplayMemberBinding is Binding binding)
            {
                string sortBy = binding.Path.Path;

                ListSortDirection direction;
                if (_lastSortField == sortBy)
                {
                    // 🔁 Toggle direction
                    direction = _lastDirection == ListSortDirection.Ascending
                        ? ListSortDirection.Descending
                        : ListSortDirection.Ascending;
                }
                else
                {
                    direction = ListSortDirection.Ascending;
                }

                _lastSortField = sortBy;
                _lastDirection = direction;

                view.SortDescriptions.Clear();
                view.SortDescriptions.Add(new SortDescription(sortBy, direction));
                view.Refresh();
            }
        }

        private void AddInventoryButton_Click(object sender, RoutedEventArgs e)
        {
            var win = new AddInventoryWindow();
            if (win.ShowDialog() == true)
            {
                LoadInventory();
            }
        }
    }
}
