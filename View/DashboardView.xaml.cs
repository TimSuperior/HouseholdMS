using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data.SQLite;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using HouseholdMS.View;                 // HouseholdsView, InventoryView
using HouseholdMS.View.UserControls;    // AddInventoryControl
using HouseholdMS.Model;                // DatabaseHelper, Household, InventoryItem (model)

namespace HouseholdMS.View.Dashboard
{
    public partial class DashboardView : UserControl
    {
        // Row 2 source for ItemsControl
        public ObservableCollection<InventoryTile> InventoryTiles { get; } = new ObservableCollection<InventoryTile>();

        public DashboardView()
        {
            InitializeComponent();
            DataContext = this;

            Loaded += (_, __) =>
            {
                LoadHouseholdCountsFromDb();   // real counts
                LoadInventoryTilesFromDb();    // real tiles
            };
        }

        // ===================== HOUSEHOLD COUNTS (DB) =====================
        private enum HCat { Operational, InService, NotOperational }

        private static HCat ClassifyStatus(string s)
        {
            // Normalize: lower + remove spaces/underscores/hyphens
            string n = (s ?? "").Trim().ToLowerInvariant()
                                 .Replace(" ", "").Replace("_", "").Replace("-", "");

            if (n.Contains("notoper") || n.Contains("notwork") || n.Contains("down") || n.Contains("fail")
                || n.Contains("broken") || n.Contains("inactive") || n == "notoperational" || n == "notworking")
                return HCat.NotOperational;

            if (n.Contains("service") || n.Contains("maint") || n.Contains("repair") || n == "inservice")
                return HCat.InService;

            // default bucket
            return HCat.Operational;
        }

        private void LoadHouseholdCountsFromDb()
        {
            int op = 0, ins = 0, notop = 0;

            try
            {
                using (var conn = DatabaseHelper.GetConnection())
                {
                    conn.Open();
                    using (var cmd = new SQLiteCommand("SELECT Statuss FROM Households", conn))
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            var raw = r["Statuss"]?.ToString();
                            switch (ClassifyStatus(raw))
                            {
                                case HCat.Operational: op++; break;
                                case HCat.InService: ins++; break;
                                case HCat.NotOperational: notop++; break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("LoadHouseholdCountsFromDb error: " + ex);
            }

            OperationalCountText.Text = op.ToString();
            InServiceCountText.Text = ins.ToString();
            NotOperationalCountText.Text = notop.ToString();
        }

        // Open your existing HouseholdsView in a popup, filtered by category
        private void OpenHouseholdsPopupFiltered(HCat cat, string title)
        {
            var hv = new HouseholdsView(userRole: "Admin"); // or "User"

            hv.Loaded += (s, e) =>
            {
                // Don't touch their SearchBox (avoids wiping the filter).
                if (hv.FindName("HouseholdListView") is ListView list && list.ItemsSource != null)
                {
                    var cv = CollectionViewSource.GetDefaultView(list.ItemsSource);
                    cv.Filter = obj =>
                    {
                        if (obj is Household h)
                            return ClassifyStatus(h.Statuss) == cat;
                        return false;
                    };
                    cv.Refresh();
                }
            };

            var win = new Window
            {
                Title = title,
                Content = hv,
                Owner = Window.GetWindow(this),
                Width = 1200,
                Height = 750,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            win.ShowDialog();
        }

        private void OpenOperational_Click(object sender, RoutedEventArgs e)
            => OpenHouseholdsPopupFiltered(HCat.Operational, "Households – Operational");

        private void OpenInService_Click(object sender, RoutedEventArgs e)
            => OpenHouseholdsPopupFiltered(HCat.InService, "Households – In Service");

        private void OpenNotOperational_Click(object sender, RoutedEventArgs e)
            => OpenHouseholdsPopupFiltered(HCat.NotOperational, "Households – Not Operational");

        // ===================== INVENTORY (DB tiles + open list + edit popup) =====================
        private void LoadInventoryTilesFromDb()
        {
            InventoryTiles.Clear();

            try
            {
                using (var conn = DatabaseHelper.GetConnection())
                {
                    conn.Open();
                    using (var cmd = new SQLiteCommand(@"
                        SELECT ItemID, ItemType, TotalQuantity, UsedQuantity, LowStockThreshold, LastRestockedDate, Note
                        FROM StockInventory
                        ORDER BY (TotalQuantity <= LowStockThreshold) DESC, ItemType ASC;", conn))
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            var tile = new InventoryTile
                            {
                                ItemID = Convert.ToInt32(r["ItemID"]),
                                Name = r["ItemType"]?.ToString(),
                                TotalQuantity = Convert.ToInt32(r["TotalQuantity"]),
                                UsedQuantity = Convert.ToInt32(r["UsedQuantity"]),
                                LowStockThreshold = Convert.ToInt32(r["LowStockThreshold"]),
                                Note = r["Note"] == DBNull.Value ? null : r["Note"].ToString(),
                                LastRestockedDate = ParseDate(r["LastRestockedDate"]?.ToString())
                            };
                            InventoryTiles.Add(tile);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("LoadInventoryTilesFromDb error: " + ex);
            }
        }

        private static DateTime? ParseDate(string s)
            => DateTime.TryParse(s, out var d) ? d : (DateTime?)null;

        // Small button near "Inventory" -> open your InventoryView in a popup
        private void OpenInventoryList_Click(object sender, RoutedEventArgs e)
        {
            var inv = new InventoryView(userRole: "Admin"); // or "User"
            var win = new Window
            {
                Title = "Inventory",
                Content = inv,
                Owner = Window.GetWindow(this),
                Width = 1100,
                Height = 700,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            win.ShowDialog();

            // After closing, reload tiles in case user edited items there
            LoadInventoryTilesFromDb();
        }

        // Clicking a tile -> open AddInventoryControl in a popup (Edit mode) for that item
        private void InventoryTile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is InventoryTile t)
            {
                // Build the model expected by AddInventoryControl (HouseholdMS.Model.InventoryItem)
                var modelItem = new InventoryItem
                {
                    ItemID = t.ItemID,
                    ItemType = t.Name,
                    TotalQuantity = t.TotalQuantity,
                    UsedQuantity = t.UsedQuantity,
                    LowStockThreshold = t.LowStockThreshold,
                    LastRestockedDate = t.LastRestockedDate?.ToString("yyyy-MM-dd") ?? null,
                    Note = t.Note ?? string.Empty
                };

                var ctrl = new AddInventoryControl(modelItem);
                ctrl.OnSavedSuccessfully += (_, __) =>
                {
                    // close the window and refresh tiles
                    Window.GetWindow(ctrl)?.Close();
                    LoadInventoryTilesFromDb();
                };
                ctrl.OnCancelRequested += (_, __) =>
                {
                    Window.GetWindow(ctrl)?.Close();
                };

                var win = new Window
                {
                    Title = $"Edit Inventory – {t.Name}",
                    Content = ctrl,
                    Owner = Window.GetWindow(this),
                    Width = 480,
                    Height = 560,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    ResizeMode = ResizeMode.NoResize
                };
                win.ShowDialog();
            }
        }
    }

    // ---- Inventory tile model for dashboard (Row 2) ----
    public class InventoryTile
    {
        public int ItemID { get; set; }
        public string Name { get; set; }
        public int TotalQuantity { get; set; }
        public int UsedQuantity { get; set; }
        public int Remaining => Math.Max(0, TotalQuantity - UsedQuantity);
        public int LowStockThreshold { get; set; }
        public DateTime? LastRestockedDate { get; set; }
        public string Note { get; set; }
        public bool IsLowStock => Remaining <= LowStockThreshold;
    }
}
