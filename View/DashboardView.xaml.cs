using System;
using System.Collections.ObjectModel;
using System.Data.SQLite;
using System.Windows;
using System.Windows.Controls;
using HouseholdMS;                      // for MainWindow
using HouseholdMS.View;                 // InventoryView
using HouseholdMS.View.UserControls;    // AddInventoryControl (used by tile editor)
using HouseholdMS.Model;                // DatabaseHelper, InventoryItem

namespace HouseholdMS.View.Dashboard
{
    public partial class DashboardView : UserControl
    {
        private const string OPERATIONAL = "Operational";
        private const string IN_SERVICE = "In Service"; // UI label: "Out of Service"

        private readonly string _userRole; // <-- carry role from MainWindow/Login

        public ObservableCollection<InventoryTile> InventoryTiles { get; } = new ObservableCollection<InventoryTile>();

        // Prefer this ctor (MainWindow already calls new DashboardView(_currentUserRole))
        public DashboardView(string userRole)
        {
            InitializeComponent();

            _userRole = string.IsNullOrWhiteSpace(userRole) ? "User" : userRole.Trim();

            DataContext = this;

            Loaded += delegate
            {
                LoadHouseholdCountsFromDb();
                LoadInventoryTilesFromDb();
            };
        }

        // Safe default if someone constructs without a role (treated as "User")
        public DashboardView() : this("User") { }

        private enum HCat { Operational, InService }

        private static HCat ClassifyStatus(string s)
        {
            var t = (s ?? string.Empty).Trim();
            if (t.Equals(OPERATIONAL, StringComparison.OrdinalIgnoreCase)) return HCat.Operational;
            if (t.Equals(IN_SERVICE, StringComparison.OrdinalIgnoreCase)) return HCat.InService;
            return HCat.Operational; // default bucket
        }

        private void LoadHouseholdCountsFromDb()
        {
            int op = 0, outs = 0;

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
                                case HCat.InService: outs++; break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("LoadHouseholdCountsFromDb error: " + ex);
            }

            int total = op + outs;

            // Update counts
            if (TotalCountText != null) TotalCountText.Text = total.ToString();
            if (OperationalCountText != null) OperationalCountText.Text = op.ToString();
            if (OutOfServiceCountText != null) OutOfServiceCountText.Text = outs.ToString();

            // Update ring percentages — out + op = 100% of total
            double opPct = total > 0 ? (op * 100.0 / total) : 0.0;
            double outPct = total > 0 ? (outs * 100.0 / total) : 0.0;

            if (OperationalProgress != null) OperationalProgress.Percentage = opPct;
            if (OutOfServiceProgress != null) OutOfServiceProgress.Percentage = outPct;
        }

        // ---------------- Household tile openers -> navigate MainContent ----------------

        private void OpenAllHouseholds_Click(object sender, RoutedEventArgs e)
            => NavigateMain(new AllHouseholdsView(_userRole));

        private void OpenOperational_Click(object sender, RoutedEventArgs e)
            => NavigateMain(new OperationalHouseholdsView(_userRole));

        // "Out of Service" corresponds to DB status "In Service"
        private void OpenOutOfService_Click(object sender, RoutedEventArgs e)
            => NavigateMain(new OutOfServiceHouseholdsView(_userRole));

        private void NavigateMain(UserControl view)
        {
            // Navigate in MainWindow's MainContent if available; fallback to popup if not.
            if (Window.GetWindow(this) is MainWindow mw)
            {
                mw.NavigateTo(view);
            }
            else
            {
                var win = new Window
                {
                    Title = view.GetType().Name,
                    Content = view,
                    Owner = Window.GetWindow(this),
                    Width = 1200,
                    Height = 750,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };
                win.ShowDialog();
            }
        }

        // ---------------- Inventory ----------------

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
                                LastRestockedDate = DateTime.TryParse(
                                                        r["LastRestockedDate"] == DBNull.Value ? null : r["LastRestockedDate"].ToString(),
                                                        out var d) ? d : (DateTime?)null
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
            // Pass the current role so InventoryView can enforce its own access rules
            var inv = new InventoryView(userRole: _userRole);
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
                LastRestockedDate = t.LastRestockedDate?.ToString("yyyy-MM-dd"),
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
