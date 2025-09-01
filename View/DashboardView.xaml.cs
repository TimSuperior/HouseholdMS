using System;
using System.Collections.ObjectModel;
using System.Data.SQLite;
using System.Windows;
using System.Windows.Controls;
using HouseholdMS.View;                 // HouseholdsView, InventoryView
using HouseholdMS.View.UserControls;    // AddInventoryControl (used by tile editor)
using HouseholdMS.Model;                // DatabaseHelper, InventoryItem

namespace HouseholdMS.View.Dashboard
{
    public partial class DashboardView : UserControl
    {
        private const string OPERATIONAL = "Operational";
        private const string IN_SERVICE = "In Service";
        private const string NOT_OPERATIONAL = "Not Operational";

        public ObservableCollection<InventoryTile> InventoryTiles { get; } = new ObservableCollection<InventoryTile>();

        public DashboardView(string userRole)
        {
            InitializeComponent();
            DataContext = this;

            Loaded += delegate
            {
                LoadHouseholdCountsFromDb();
                LoadInventoryTilesFromDb();
            };
        }

        private enum HCat { Operational, InService, NotOperational }

        private static HCat ClassifyStatus(string s)
        {
            var t = (s ?? string.Empty).Trim();
            if (t.Equals(OPERATIONAL, StringComparison.OrdinalIgnoreCase)) return HCat.Operational;
            if (t.Equals(IN_SERVICE, StringComparison.OrdinalIgnoreCase)) return HCat.InService;
            if (t.Equals(NOT_OPERATIONAL, StringComparison.OrdinalIgnoreCase)) return HCat.NotOperational;
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
                            var raw = r["Statuss"] == DBNull.Value ? null : r["Statuss"].ToString();
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

            int total = op + ins + notop;

            // Update UI counts
            if (TotalCountText != null) TotalCountText.Text = total.ToString();
            if (OperationalCountText != null) OperationalCountText.Text = op.ToString();
            if (OutOfServiceCountText != null) OutOfServiceCountText.Text = ins.ToString(); // "Out of Service" == DB "In Service"
        }

        // Opens popup, optionally forcing ALL mode
        private void OpenHouseholdsPopup(string initialStatusFilter, string title, bool showAll = false)
        {
            var hv = new HouseholdsView(
                userRole: "Admin",
                initialStatusFilter: initialStatusFilter,
                showAll: showAll
            );

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

            // refresh counts in case statuses changed
            LoadHouseholdCountsFromDb();
        }

        private void OpenOperational_Click(object sender, RoutedEventArgs e)
            => OpenHouseholdsPopup(OPERATIONAL, "Households – Operational", showAll: false);

        // "Out of Service" tile uses DB status "In Service"
        private void OpenInService_Click(object sender, RoutedEventArgs e)
            => OpenHouseholdsPopup(IN_SERVICE, "Households – Out of Service", showAll: false);

        // Header button and tile 1 both open ALL households
        private void OpenAllHouseholds_Click(object sender, RoutedEventArgs e)
            => OpenHouseholdsPopup(initialStatusFilter: null, title: "Households – All", showAll: true);

        // -------- Inventory --------
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
                                Name = r["ItemType"] == DBNull.Value ? null : r["ItemType"].ToString(),
                                TotalQuantity = Convert.ToInt32(r["TotalQuantity"]),
                                UsedQuantity = Convert.ToInt32(r["UsedQuantity"]),
                                LowStockThreshold = Convert.ToInt32(r["LowStockThreshold"]),
                                Note = r["Note"] == DBNull.Value ? null : r["Note"].ToString(),
                                LastRestockedDate = DateTime.TryParse(r["LastRestockedDate"] == DBNull.Value ? null : r["LastRestockedDate"].ToString(), out var d) ? d : (DateTime?)null
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

        private void OpenInventoryList_Click(object sender, RoutedEventArgs e)
        {
            var inv = new InventoryView(userRole: "Admin");
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

            LoadInventoryTilesFromDb();
        }

        private void InventoryTile_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;
            var t = btn.DataContext as InventoryTile;
            if (t == null) return;

            var modelItem = new InventoryItem
            {
                ItemID = t.ItemID,
                ItemType = t.Name,
                TotalQuantity = t.TotalQuantity,
                UsedQuantity = t.UsedQuantity,
                LowStockThreshold = t.LowStockThreshold,
                LastRestockedDate = t.LastRestockedDate.HasValue ? t.LastRestockedDate.Value.ToString("yyyy-MM-dd") : null,
                Note = t.Note ?? string.Empty
            };

            var ctrl = new AddInventoryControl(modelItem);
            ctrl.OnSavedSuccessfully += delegate
            {
                Window.GetWindow(ctrl)?.Close();
                LoadInventoryTilesFromDb();
            };
            ctrl.OnCancelRequested += delegate
            {
                Window.GetWindow(ctrl)?.Close();
            };

            var win = new Window
            {
                Title = "Edit Inventory – " + (t.Name ?? ""),
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

    // dashboard tile model for inventory
    public class InventoryTile
    {
        public int ItemID { get; set; }
        public string Name { get; set; }
        public int TotalQuantity { get; set; }
        public int UsedQuantity { get; set; }
        public int Remaining { get { return Math.Max(0, TotalQuantity - UsedQuantity); } }
        public int LowStockThreshold { get; set; }
        public DateTime? LastRestockedDate { get; set; }
        public string Note { get; set; }

        public bool IsZero { get { return Remaining == 0; } }
        public bool IsBelowThreshold { get { return Remaining > 0 && Remaining < LowStockThreshold; } }
        public bool IsOk { get { return Remaining >= LowStockThreshold; } }
    }
}
