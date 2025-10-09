using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Data.SQLite;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using HouseholdMS.Model;
using HouseholdMS.View.Inventory;
using HouseholdMS.View.Dashboard;
using HouseholdMS.View.UserControls;
using HouseholdMS.Resources;

namespace HouseholdMS.View.Dashboard
{
    public partial class DashboardView : UserControl
    {
        private const string OPERATIONAL = "Operational";
        private const string IN_SERVICE = "In Service";

        // Defaults for solar tiles
        private const double DefaultLat = 38.56;
        private const double DefaultLon = 68.79;
        private const double DefaultPeakKw = 5.0;
        private const int DefaultTilt = 30;
        private const int DefaultAzimuth = 180;
        private const double DefaultLossesPct = 14.0;

        private readonly string _userRole;

        public ObservableCollection<InventoryTileVm> InventoryTiles { get; } = new ObservableCollection<InventoryTileVm>();

        // ===== Inventory layout math (auto-fit to container; shrink if needed) =====
        private const double InvBaseWidth = 240.0;
        private const double InvBaseHeight = 170.0; // base
        private const double InvAspect = InvBaseHeight / InvBaseWidth;

        private const double TileMarginX = 16.0; // Button margin Left+Right = 8+8
        private const double TileMarginY = 16.0; // Button margin Top+Bottom = 8+8
        private const double ContainerPadding = 16.0; // approx ScrollViewer/ItemsControl padding/margins

        private const double AbsoluteMinWidth = 120.0; // allow smaller than before to guarantee fit
        private const double HardMinWidth = 90.0;  // emergency floor if panel is very short

        public static readonly DependencyProperty InventoryItemWidthProperty =
            DependencyProperty.Register(nameof(InventoryItemWidth), typeof(double), typeof(DashboardView),
                new PropertyMetadata(InvBaseWidth));

        public static readonly DependencyProperty InventoryItemHeightProperty =
            DependencyProperty.Register(nameof(InventoryItemHeight), typeof(double), typeof(DashboardView),
                new PropertyMetadata(InvBaseHeight));

        // Compatibility (not used by layout directly)
        public static readonly DependencyProperty InventoryPanelHeightProperty =
            DependencyProperty.Register(nameof(InventoryPanelHeight), typeof(double), typeof(DashboardView),
                new PropertyMetadata(InvBaseHeight * 2 + TileMarginY + 140));

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
            InventoryTiles.CollectionChanged += InventoryTiles_CollectionChanged;
        }

        private void DashboardView_Loaded(object sender, RoutedEventArgs e)
        {
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
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(RecalculateInventoryLayout));

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
        /// Choose ItemWidth/ItemHeight so that ALL inventory tiles fit inside the available
        /// inventory area without scrollbars. We try to keep them as large as possible,
        /// shrinking only when necessary. Works for 2, 3 or more rows.
        /// </summary>
        private void RecalculateInventoryLayout()
        {
            if (InventoryContainer == null) return;

            // Available space inside the panel
            double availW = Math.Max(0, InventoryContainer.ActualWidth - ContainerPadding);
            double availH = Math.Max(0, InventoryContainer.ActualHeight - ContainerPadding);

            if (availW <= 0 || availH <= 0)
            {
                // try again when layout settles
                Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(RecalculateInventoryLayout));
                return;
            }

            int count = InventoryTiles?.Count ?? 0;
            if (count <= 0)
            {
                InventoryItemWidth = InvBaseWidth;
                InventoryItemHeight = InvBaseHeight;
                InventoryPanelHeight = availH;
                return;
            }

            // Try all reasonable column counts and pick the largest tile size that fits.
            double bestW = -1, bestH = -1;
            int maxColsByWidth = Math.Max(1, (int)Math.Floor((availW + TileMarginX) / (AbsoluteMinWidth + TileMarginX)));
            int maxCols = Math.Max(1, Math.Min(count, maxColsByWidth));

            for (int cols = 1; cols <= maxCols; cols++)
            {
                // width that fits horizontally for 'cols' columns (including margins)
                double w = Math.Floor((availW - (cols * TileMarginX)) / cols);
                if (w <= 0) continue;
                if (w < AbsoluteMinWidth) continue;

                double h = w * InvAspect;
                int rows = (int)Math.Ceiling(count / (double)cols);
                double totalHeight = rows * h + (rows - 1) * TileMarginY;

                if (totalHeight <= availH)
                {
                    if (w > bestW)
                    {
                        bestW = w;
                        bestH = h;
                    }
                }
            }

            if (bestW < 0)
            {
                // No solution >= AbsoluteMinWidth fits. Allow smaller tiles to guarantee fit.
                // Start from the max columns that base width allows, then shrink by height constraint.
                int cols = Math.Max(1, maxCols);
                double wHoriz = Math.Floor((availW - (cols * TileMarginX)) / cols);
                if (wHoriz <= 0) wHoriz = AbsoluteMinWidth;

                int rows = (int)Math.Ceiling(count / (double)cols);
                double maxHPerItem = (availH - (rows - 1) * TileMarginY) / rows;
                double wFromH = Math.Floor(maxHPerItem / InvAspect);

                double w = Math.Max(HardMinWidth, Math.Min(wHoriz, wFromH));
                double h = Math.Max(40.0, w * InvAspect);

                bestW = w;
                bestH = h;
            }

            // Clamp to base (don't grow above base visuals)
            bestW = Math.Min(bestW, InvBaseWidth);
            bestH = Math.Min(bestH, InvBaseHeight);

            InventoryItemWidth = bestW;
            InventoryItemHeight = bestH;

            // keep compatibility property updated
            InventoryPanelHeight = availH;
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

        #region Inventory tiles (UNCHANGED data load)
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
                mw.NavigateTo(new InventoryView(_userRole));
            }
            else
            {
                var win = new Window
                {
                    Title = "Inventory",
                    Content = new InventoryView(_userRole),
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

            if (!int.TryParse(btn.Tag.ToString(), out int itemId)) return;

            var owner = Window.GetWindow(this);
            var win = new InventoryDetails(itemId) { Owner = owner };
            win.ShowDialog();
        }
        #endregion

        #region Weather & Solar tiles (UNCHANGED)
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
            win.Show();
        }

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
            win.Show();
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

        public bool IsZero => Remaining <= 0;
        public bool IsBelowThreshold => Remaining > 0 && Remaining <= LowStockThreshold;
        public bool IsOk => Remaining > LowStockThreshold;
    }
}
