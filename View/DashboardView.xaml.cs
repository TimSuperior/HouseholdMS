using System;
using System.Collections.ObjectModel;
using System.Data.SQLite;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using HouseholdMS.Model;
using HouseholdMS.View.Inventory;       // InventoryView, InventoryDetails
using HouseholdMS.View.Dashboard;       // SitesView & SitesLanding assumed in same namespace

namespace HouseholdMS.View.Dashboard
{
    public partial class DashboardView : UserControl
    {
        private const string OPERATIONAL = "Operational";
        private const string IN_SERVICE = "In Service"; // UI label: "Out of Service"

        private readonly string _userRole;

        public ObservableCollection<InventoryTileVm> InventoryTiles { get; } = new ObservableCollection<InventoryTileVm>();

        public DashboardView(string userRole = "User")
        {
            InitializeComponent();
            _userRole = string.IsNullOrWhiteSpace(userRole) ? "User" : userRole.Trim();

            // Keep bindings simple for the tiles
            this.DataContext = this;

            Loaded += DashboardView_Loaded;
        }

        private void DashboardView_Loaded(object sender, RoutedEventArgs e)
        {
            LoadHouseholdCountsFromDb();
            LoadInventoryTilesFromDb();
        }

        #region Household counts (for the three top tiles)
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
                System.Diagnostics.Debug.WriteLine("DashboardView.LoadHouseholdCountsFromDb error: " + ex);
            }

            int total = op + outs;

            if (TotalCountText != null) TotalCountText.Text = total.ToString(CultureInfo.InvariantCulture);
            if (OperationalCountText != null) OperationalCountText.Text = op.ToString(CultureInfo.InvariantCulture);
            if (OutOfServiceCountText != null) OutOfServiceCountText.Text = outs.ToString(CultureInfo.InvariantCulture);

            double opPct = total > 0 ? (op * 100.0 / total) : 0.0;
            double outPct = total > 0 ? (outs * 100.0 / total) : 0.0;

            if (OperationalProgress != null) OperationalProgress.Percentage = opPct;
            if (OutOfServiceProgress != null) OutOfServiceProgress.Percentage = outPct;
        }
        #endregion

        #region Inventory tiles
        private void LoadInventoryTilesFromDb()
        {
            InventoryTiles.Clear();

            try
            {
                using (var conn = DatabaseHelper.GetConnection())
                {
                    conn.Open();

                    // Use the view with precomputed OnHand
                    using (var cmd = new SQLiteCommand(
                        @"SELECT ItemID,
                                 ItemType,
                                 LowStockThreshold,
                                 LastRestockedDate,
                                 OnHand
                          FROM v_ItemOnHand
                          ORDER BY ItemType COLLATE NOCASE", conn))
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            var vm = new InventoryTileVm
                            {
                                ItemID = r["ItemID"] == DBNull.Value ? 0 : Convert.ToInt32(r["ItemID"]),
                                Name = r["ItemType"] == DBNull.Value ? "" : Convert.ToString(r["ItemType"]),
                                LowStockThreshold = r["LowStockThreshold"] == DBNull.Value ? 0 : Convert.ToInt32(r["LowStockThreshold"]),
                                Remaining = r["OnHand"] == DBNull.Value ? 0 : Convert.ToInt32(r["OnHand"]),
                                LastRestockedDate = r["LastRestockedDate"] == DBNull.Value ? "—" : Convert.ToString(r["LastRestockedDate"])
                            };
                            InventoryTiles.Add(vm);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("DashboardView.LoadInventoryTilesFromDb error: " + ex);
            }
        }
        #endregion

        #region Tile/Buttons handlers
        private void OpenAllHouseholds_Click(object sender, RoutedEventArgs e)
        {
            // Navigate to SitesView landing "All"
            var host = Window.GetWindow(this);
            var mw = host as MainWindow;
            if (mw != null)
            {
                mw.NavigateTo(new SitesView(_userRole, SitesLanding.All));
            }
            else
            {
                var win = new Window
                {
                    Title = "Sites",
                    Content = new SitesView(_userRole, SitesLanding.All),
                    Owner = host,
                    Width = 1200,
                    Height = 750,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };
                win.ShowDialog();
            }
        }

        private void OpenOperational_Click(object sender, RoutedEventArgs e)
        {
            var host = Window.GetWindow(this);
            var mw = host as MainWindow;
            if (mw != null)
            {
                mw.NavigateTo(new SitesView(_userRole, SitesLanding.Operational));
            }
            else
            {
                var win = new Window
                {
                    Title = "Sites - Operational",
                    Content = new SitesView(_userRole, SitesLanding.Operational),
                    Owner = host,
                    Width = 1200,
                    Height = 750,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };
                win.ShowDialog();
            }
        }

        private void OpenOutOfService_Click(object sender, RoutedEventArgs e)
        {
            var host = Window.GetWindow(this);
            var mw = host as MainWindow;
            if (mw != null)
            {
                mw.NavigateTo(new SitesView(_userRole, SitesLanding.OutOfService));
            }
            else
            {
                var win = new Window
                {
                    Title = "Sites - Out of Service",
                    Content = new SitesView(_userRole, SitesLanding.OutOfService),
                    Owner = host,
                    Width = 1200,
                    Height = 750,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };
                win.ShowDialog();
            }
        }

        private void OpenInventoryList_Click(object sender, RoutedEventArgs e)
        {
            // Navigate to the Inventory list view
            var host = Window.GetWindow(this);
            var mw = host as MainWindow;
            if (mw != null)
            {
                // If your InventoryView takes parameters (e.g., userRole), pass them here.
                mw.NavigateTo(new InventoryView());
            }
            else
            {
                // Fallback: open as a modal window if not hosted in MainWindow
                var win = new Window
                {
                    Title = "Inventory",
                    Content = new InventoryView(),
                    Owner = host,
                    Width = 1200,
                    Height = 750,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };
                win.ShowDialog();
            }
        }

        private void InventoryTile_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn?.Tag == null) return;

            int itemId;
            if (!int.TryParse(btn.Tag.ToString(), out itemId)) return;

            var owner = Window.GetWindow(this);
            var win = new InventoryDetails(itemId) { Owner = owner };
            win.ShowDialog();
        }
        #endregion
    }

    #region ViewModel used for each inventory tile
    public sealed class InventoryTileVm
    {
        public int ItemID { get; set; }
        public string Name { get; set; }
        public int Remaining { get; set; }
        public int LowStockThreshold { get; set; }
        public string LastRestockedDate { get; set; }  // stored as TEXT in DB

        public bool IsZero { get { return Remaining <= 0; } }
        public bool IsBelowThreshold { get { return Remaining > 0 && Remaining <= LowStockThreshold; } }
        public bool IsOk { get { return Remaining > LowStockThreshold; } }
    }
    #endregion
}
