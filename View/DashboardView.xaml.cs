using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Data.SQLite;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using HouseholdMS.Model;
using HouseholdMS.View.Inventory;
using HouseholdMS.View.Dashboard;
using HouseholdMS.View.UserControls;

namespace HouseholdMS.View.Dashboard
{
    public partial class DashboardView : UserControl
    {
        private const string OPERATIONAL = "Operational";
        private const string IN_SERVICE = "In Service";

        // Defaults for solar tiles (Sughd/Tajikistan-ish). Adjust later or bind per-site.
        private const double DefaultLat = 38.56;
        private const double DefaultLon = 68.79;

        private const double DefaultPeakKw = 5.0;
        private const int DefaultTilt = 30;
        private const int DefaultAzimuth = 180;
        private const double DefaultLossesPct = 14.0;

        private readonly string _userRole;

        public ObservableCollection<InventoryTileVm> InventoryTiles { get; } = new ObservableCollection<InventoryTileVm>();

        // ===== Inventory layout math (two rows, auto-shrink) =====
        private const double InvBaseWidth = 240.0;
        private const double InvBaseHeight = 130.0;
        private const double InvAspect = InvBaseHeight / InvBaseWidth;
        private const double ItemMarginX = 16.0;
        private const double ItemMarginY = 16.0;
        private const double MinItemWidth = 160.0;

        // Dependency properties so XAML can bind WrapPanel.ItemWidth/ItemHeight and container Height
        public static readonly DependencyProperty InventoryItemWidthProperty =
            DependencyProperty.Register(nameof(InventoryItemWidth), typeof(double), typeof(DashboardView),
                new PropertyMetadata(InvBaseWidth));

        public static readonly DependencyProperty InventoryItemHeightProperty =
            DependencyProperty.Register(nameof(InventoryItemHeight), typeof(double), typeof(DashboardView),
                new PropertyMetadata(InvBaseHeight));

        // Taller than before (extra space so weather row can be smaller)
        public static readonly DependencyProperty InventoryPanelHeightProperty =
            DependencyProperty.Register(nameof(InventoryPanelHeight), typeof(double), typeof(DashboardView),
                new PropertyMetadata(InvBaseHeight * 2 + ItemMarginY + 56)); // was +24

        public double InventoryItemWidth
        {
            get => (double)GetValue(InventoryItemWidthProperty);
            set => SetValue(InventoryItemWidthProperty, value);
        }
        public double InventoryItemHeight
        {
            get => (double)GetValue(InventoryItemHeightProperty);
            set => SetValue(InventoryItemHeightProperty, value);
        }
        public double InventoryPanelHeight
        {
            get => (double)GetValue(InventoryPanelHeightProperty);
            set => SetValue(InventoryPanelHeightProperty, value);
        }

        public DashboardView(string userRole = "User")
        {
            InitializeComponent();
            _userRole = string.IsNullOrWhiteSpace(userRole) ? "User" : userRole.Trim();
            DataContext = this;

            Loaded += DashboardView_Loaded;

            // When collection changes (add/remove items), reflow
            InventoryTiles.CollectionChanged += InventoryTiles_CollectionChanged;
        }

        private void DashboardView_Loaded(object sender, RoutedEventArgs e)
        {
            // ensure tiles have default params if not set in XAML
            if (IrradianceTile != null)
            {
                if (double.IsNaN(IrradianceTile.Latitude)) IrradianceTile.Latitude = DefaultLat;
                if (double.IsNaN(IrradianceTile.Longitude)) IrradianceTile.Longitude = DefaultLon;
            }
            if (PvTile != null)
            {
                if (double.IsNaN(PvTile.Latitude)) PvTile.Latitude = DefaultLat;
                if (double.IsNaN(PvTile.Longitude)) PvTile.Longitude = DefaultLon;
                if (PvTile.PeakKw <= 0) PvTile.PeakKw = DefaultPeakKw;
                if (PvTile.Tilt <= 0) PvTile.Tilt = DefaultTilt;
                if (PvTile.Azimuth == 0) PvTile.Azimuth = DefaultAzimuth;
                if (PvTile.LossesPct <= 0) PvTile.LossesPct = DefaultLossesPct;
            }

            if (InventoryContainer != null)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(RecalculateInventoryLayout));
            }

            LoadHouseholdCountsFromDb();
            LoadInventoryTilesFromDb();
        }

        private void InventoryTiles_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            RecalculateInventoryLayout();
        }

        private void InventoryContainer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            RecalculateInventoryLayout();
        }

        /// <summary>
        /// Compute a uniform ItemWidth/ItemHeight so that all inventory tiles
        /// fit into at most two rows within the fixed-height container.
        /// </summary>
        private void RecalculateInventoryLayout()
        {
            if (InventoryContainer == null) return;

            double available = Math.Max(0, InventoryContainer.ActualWidth - 16);
            if (available <= 0)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(RecalculateInventoryLayout));
                return;
            }

            int count = InventoryTiles?.Count ?? 0;
            if (count <= 0)
            {
                InventoryItemWidth = InvBaseWidth;
                InventoryItemHeight = InvBaseHeight;
                InventoryPanelHeight = InvBaseHeight * 2 + ItemMarginY + 56; // taller than before
                return;
            }

            int columnsAtBase = Math.Max(1, (int)Math.Floor((available + ItemMarginX) / (InvBaseWidth + ItemMarginX)));
            int columnsRequired = Math.Max(1, (int)Math.Ceiling(count / 2.0));

            double newWidth;
            if (columnsRequired <= columnsAtBase)
            {
                newWidth = InvBaseWidth; // base size fits in two rows
            }
            else
            {
                newWidth = Math.Floor((available - (columnsRequired * ItemMarginX)) / columnsRequired);
                newWidth = Math.Max(MinItemWidth, Math.Min(InvBaseWidth, newWidth));
            }

            double newHeight = Math.Round(newWidth * InvAspect);

            InventoryItemWidth = newWidth;
            InventoryItemHeight = newHeight;

            // keep the section a bit taller
            InventoryPanelHeight = (newHeight * 2) + ItemMarginY + 56;
        }

        #region Household counts (UNCHANGED)
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

        #region Inventory tiles (UNCHANGED except call to Recalculate)
        private void LoadInventoryTilesFromDb()
        {
            InventoryTiles.Clear();

            try
            {
                using (var conn = DatabaseHelper.GetConnection())
                {
                    conn.Open();

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

            // Lay out after data load
            RecalculateInventoryLayout();
        }
        #endregion

        #region Existing Tile/Buttons (UNCHANGED)
        private void OpenAllHouseholds_Click(object sender, RoutedEventArgs e)
        {
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
            var host = Window.GetWindow(this);
            var mw = host as MainWindow;
            if (mw != null)
            {
                mw.NavigateTo(new InventoryView());
            }
            else
            {
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
            if (btn == null || btn.Tag == null) return;

            int itemId;
            if (!int.TryParse(btn.Tag.ToString(), out itemId)) return;

            var owner = Window.GetWindow(this);
            var win = new InventoryDetails(itemId) { Owner = owner };
            win.ShowDialog();
        }
        #endregion

        #region Weather tile (UNCHANGED)
        private void OpenWeather_Click(object sender, RoutedEventArgs e)
        {
            var outer = sender as Button;
            if (outer != null && IsFromInnerButton(e.OriginalSource as DependencyObject, outer))
            {
                e.Handled = true;
                return;
            }

            if (YrTile == null) return;

            var owner = Window.GetWindow(this);
            var win = new YrMeteogramWindow(YrTile.LocationId, YrTile.YrLanguage)
            {
                Owner = owner,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            win.Show(); // non-modal
        }
        #endregion

        #region NEW: Solar tiles open detail windows (UNCHANGED)
        private void OpenIrradiance_Click(object sender, RoutedEventArgs e)
        {
            var outer = sender as Button;
            if (outer != null && IsFromInnerButton(e.OriginalSource as DependencyObject, outer))
            {
                e.Handled = true;
                return;
            }

            double lat = (IrradianceTile != null) ? IrradianceTile.Latitude : DefaultLat;
            double lon = (IrradianceTile != null) ? IrradianceTile.Longitude : DefaultLon;

            var owner = Window.GetWindow(this);
            var win = new IrradianceDetailsWindow(lat, lon)
            {
                Owner = owner,
                Width = 900,
                Height = 600,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            win.Show(); // non-modal like weather
        }

        private void OpenPvEnergy_Click(object sender, RoutedEventArgs e)
        {
            var outer = sender as Button;
            if (outer != null && IsFromInnerButton(e.OriginalSource as DependencyObject, outer))
            {
                e.Handled = true;
                return;
            }

            double lat = (PvTile != null) ? PvTile.Latitude : DefaultLat;
            double lon = (PvTile != null) ? PvTile.Longitude : DefaultLon;
            double kw = (PvTile != null && PvTile.PeakKw > 0) ? PvTile.PeakKw : DefaultPeakKw;
            int tilt = (PvTile != null && PvTile.Tilt > 0) ? PvTile.Tilt : DefaultTilt;
            int az = (PvTile != null && PvTile.Azimuth != 0) ? PvTile.Azimuth : DefaultAzimuth;
            double losses = (PvTile != null && PvTile.LossesPct > 0) ? PvTile.LossesPct : DefaultLossesPct;

            var owner = Window.GetWindow(this);
            var win = new PvEnergyDetailsWindow(lat, lon, kw, tilt, az, losses)
            {
                Owner = owner,
                Width = 900,
                Height = 620,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            win.Show();
        }
        #endregion

        private static bool IsFromInnerButton(DependencyObject origin, Button outer)
        {
            DependencyObject cur = origin;
            while (cur != null && cur != outer)
            {
                if (cur is Button) return true;
                cur = VisualTreeHelper.GetParent(cur);
            }
            return false;
        }
    }

    public sealed class InventoryTileVm
    {
        public int ItemID { get; set; }
        public string Name { get; set; }
        public int Remaining { get; set; }
        public int LowStockThreshold { get; set; }
        public string LastRestockedDate { get; set; }

        public bool IsZero { get { return Remaining <= 0; } }
        public bool IsBelowThreshold { get { return Remaining > 0 && Remaining <= LowStockThreshold; } }
        public bool IsOk { get { return Remaining > LowStockThreshold; } }
    }
}
