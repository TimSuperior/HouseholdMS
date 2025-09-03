using System;
using System.Collections.ObjectModel;
using System.Data.SQLite;
using System.Windows;
using System.Windows.Controls;
using HouseholdMS;                      // MainWindow
using HouseholdMS.View;                 // InventoryView
using HouseholdMS.View.UserControls;    // AddInventoryControl
using HouseholdMS.Model;                // DatabaseHelper, InventoryItem
using HouseholdMS.View.Dashboard;       // SitesView, SitesLanding

namespace HouseholdMS.View.Dashboard
{
    public partial class DashboardView : UserControl
    {
        private const string OPERATIONAL = "Operational";
        private const string IN_SERVICE = "In Service"; // UI label: "Out of Service"

        private readonly string _userRole;

        public ObservableCollection<InventoryTile> InventoryTiles { get; } = new ObservableCollection<InventoryTile>();

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

        public DashboardView() : this("User") { }

        private enum HCat { Operational, InService }

        private static HCat ClassifyStatus(string s)
        {
            string t = (s ?? string.Empty).Trim();
            if (t.Equals(OPERATIONAL, StringComparison.OrdinalIgnoreCase)) return HCat.Operational;
            if (t.Equals(IN_SERVICE, StringComparison.OrdinalIgnoreCase)) return HCat.InService;
            return HCat.Operational;
        }

        private void LoadHouseholdCountsFromDb()
        {
            int op = 0, outs = 0;

            try
            {
                using (SQLiteConnection conn = DatabaseHelper.GetConnection())
                {
                    conn.Open();
                    using (SQLiteCommand cmd = new SQLiteCommand("SELECT Statuss FROM Households", conn))
                    using (SQLiteDataReader r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            string raw = r["Statuss"] == DBNull.Value ? null : r["Statuss"].ToString();
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

            if (TotalCountText != null) TotalCountText.Text = total.ToString();
            if (OperationalCountText != null) OperationalCountText.Text = op.ToString();
            if (OutOfServiceCountText != null) OutOfServiceCountText.Text = outs.ToString();

            double opPct = total > 0 ? (op * 100.0 / total) : 0.0;
            double outPct = total > 0 ? (outs * 100.0 / total) : 0.0;

            if (OperationalProgress != null) OperationalProgress.Percentage = opPct;
            if (OutOfServiceProgress != null) OutOfServiceProgress.Percentage = outPct;
        }

        // ---------------- Household tile openers -> navigate to SitesView with landing ----------------

        private void OpenAllHouseholds_Click(object sender, RoutedEventArgs e)
        {
            NavigateMain(new SitesView(_userRole, SitesLanding.All));
        }

        private void OpenOperational_Click(object sender, RoutedEventArgs e)
        {
            NavigateMain(new SitesView(_userRole, SitesLanding.Operational));
        }

        private void OpenOutOfService_Click(object sender, RoutedEventArgs e)
        {
            NavigateMain(new SitesView(_userRole, SitesLanding.OutOfService));
        }

        private void NavigateMain(UserControl view)
        {
            Window host = Window.GetWindow(this);
            MainWindow mw = host as MainWindow;
            if (mw != null)
            {
                mw.NavigateTo(view);
            }
            else
            {
                Window win = new Window
                {
                    Title = view.GetType().Name,
                    Content = view,
                    Owner = host,
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
                using (SQLiteConnection conn = DatabaseHelper.GetConnection())
                {
                    conn.Open();
                    using (SQLiteCommand cmd = new SQLiteCommand(@"
                        SELECT ItemID, ItemType, TotalQuantity, UsedQuantity, LowStockThreshold, LastRestockedDate, Note
                        FROM StockInventory
                        ORDER BY (TotalQuantity <= LowStockThreshold) DESC, ItemType ASC;", conn))
                    using (SQLiteDataReader r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            InventoryTile tile = new InventoryTile
                            {
                                ItemID = Convert.ToInt32(r["ItemID"]),
                                Name = r["ItemType"] == DBNull.Value ? null : r["ItemType"].ToString(),
                                TotalQuantity = Convert.ToInt32(r["TotalQuantity"]),
                                UsedQuantity = Convert.ToInt32(r["UsedQuantity"]),
                                LowStockThreshold = Convert.ToInt32(r["LowStockThreshold"]),
                                Note = r["Note"] == DBNull.Value ? null : r["Note"].ToString(),
                                LastRestockedDate = DateTime.TryParse(
                                    r["LastRestockedDate"] == DBNull.Value ? null : r["LastRestockedDate"].ToString(),
                                    out DateTime d) ? d : (DateTime?)null
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
            InventoryView inv = new InventoryView(userRole: _userRole);
            Window win = new Window
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
            Button btn = sender as Button;
            if (btn == null) return;

            InventoryTile t = btn.DataContext as InventoryTile;
            if (t == null) return;

            InventoryItem modelItem = new InventoryItem
            {
                ItemID = t.ItemID,
                ItemType = t.Name,
                TotalQuantity = t.TotalQuantity,
                UsedQuantity = t.UsedQuantity,
                LowStockThreshold = t.LowStockThreshold,
                LastRestockedDate = t.LastRestockedDate.HasValue ? t.LastRestockedDate.Value.ToString("yyyy-MM-dd") : null,
                Note = t.Note ?? string.Empty
            };

            AddInventoryControl ctrl = new AddInventoryControl(modelItem);
            ctrl.OnSavedSuccessfully += delegate
            {
                Window.GetWindow(ctrl)?.Close();
                LoadInventoryTilesFromDb();
            };
            ctrl.OnCancelRequested += delegate
            {
                Window.GetWindow(ctrl)?.Close();
            };

            Window win = new Window
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
