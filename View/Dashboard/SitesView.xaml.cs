using System;
using System.Data.SQLite;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using HouseholdMS;               // MainWindow
using HouseholdMS.Model;         // DatabaseHelper

namespace HouseholdMS.View.Dashboard
{
    // Public enum so DashboardView can reference it without new C# features
    public enum SitesLanding
    {
        None = 0,
        All = 1,
        Operational = 2,
        OutOfService = 3
    }

    public partial class SitesView : UserControl
    {
        private const string OPERATIONAL = "Operational";
        private const string IN_SERVICE = "In Service"; // UI name: "Out of Service"
        private const string NOT_OPERATIONAL = "Not Operational";

        private readonly string _userRole;
        private readonly SitesLanding _landing;

        // --- Safety-net: DB version polling (detects any write from any screen) ---
        private DispatcherTimer _pulse;
        private int _lastDbVersion = -1;

        public SitesView(string userRole)
        {
            InitializeComponent();

            _userRole = string.IsNullOrWhiteSpace(userRole) ? "User" : userRole.Trim();

            // DEFAULT LANDING → All (so the content control is NOT empty on first load)
            _landing = SitesLanding.All;

            Loaded += SitesView_Loaded;
            Unloaded += SitesView_Unloaded;
            PreviewKeyDown += SitesView_PreviewKeyDown; // Alt+Left shortcut
        }

        public SitesView(string userRole, SitesLanding landing) : this(userRole)
        {
            // If caller specified a landing, respect it
            _landing = landing;
        }

        public SitesView() : this("User") { }

        private void SitesView_Loaded(object sender, RoutedEventArgs e)
        {
            RefreshTiles();     // initial snapshot
            StartPulse();       // safety net: auto-refresh when DB changes
            ShowLandingIfAny(); // open requested (or default) child view
        }

        private void SitesView_Unloaded(object sender, RoutedEventArgs e)
        {
            StopPulse();
        }

        private void SitesView_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            // Alt+Left -> dashboard
            if (e.SystemKey == System.Windows.Input.Key.Left &&
                (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Alt) == System.Windows.Input.ModifierKeys.Alt)
            {
                BreadcrumbDashboard_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
        }

        private enum HCat { Operational, InService }

        private static HCat ClassifyStatus(string s)
        {
            string t = (s ?? string.Empty).Trim();
            if (t.Equals(OPERATIONAL, StringComparison.OrdinalIgnoreCase)) return HCat.Operational;
            if (t.Equals(IN_SERVICE, StringComparison.OrdinalIgnoreCase)) return HCat.InService;
            if (t.Equals(NOT_OPERATIONAL, StringComparison.OrdinalIgnoreCase)) return HCat.InService; // legacy → treat as In Service
            // Fallback: assume operational
            return HCat.Operational;
        }

        // === PUBLIC refresh entry (used by children via callback) ===
        private void RefreshTiles()
        {
            LoadHouseholdCountsFromDb();
        }

        private void LoadHouseholdCountsFromDb()
        {
            int op = 0, outs = 0, total = 0;

            try
            {
                using (var conn = DatabaseHelper.GetConnection())
                {
                    conn.Open();

                    // Fast aggregate counting with normalization in SQL
                    using (var cmd = new SQLiteCommand(@"
                        SELECT
                            SUM(CASE 
                                    WHEN lower(replace(replace(COALESCE(Statuss,''),'_',' '),'-',' ')) LIKE 'operational%'
                                        THEN 1 ELSE 0
                                END) AS Op,
                            SUM(CASE 
                                    WHEN lower(replace(replace(COALESCE(Statuss,''),'_',' '),'-',' ')) LIKE 'in service%'
                                      OR lower(replace(replace(COALESCE(Statuss,''),'_',' '),'-',' ')) LIKE 'service%'
                                      OR lower(replace(replace(COALESCE(Statuss,''),'_',' '),'-',' ')) LIKE 'not operational%'
                                        THEN 1 ELSE 0
                                END) AS Outs,
                            COUNT(*) AS Total
                        FROM Households;", conn))
                    using (var r = cmd.ExecuteReader())
                    {
                        if (r.Read())
                        {
                            op = SafeInt(r, "Op");
                            outs = SafeInt(r, "Outs");
                            total = SafeInt(r, "Total");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("SitesView.LoadHouseholdCountsFromDb error: " + ex);
            }

            if (TotalCountText != null) TotalCountText.Text = total.ToString(CultureInfo.InvariantCulture);
            if (OperationalCountText != null) OperationalCountText.Text = op.ToString(CultureInfo.InvariantCulture);
            if (OutOfServiceCountText != null) OutOfServiceCountText.Text = outs.ToString(CultureInfo.InvariantCulture);

            double opPct = total > 0 ? (op * 100.0 / total) : 0.0;
            double outPct = total > 0 ? (outs * 100.0 / total) : 0.0;

            // Your CircularProgressBar exposes "Percentage"
            if (OperationalProgress != null) OperationalProgress.Percentage = opPct;
            if (OutOfServiceProgress != null) OutOfServiceProgress.Percentage = outPct;
        }

        private static int SafeInt(System.Data.IDataRecord r, string name)
        {
            try
            {
                int i = r.GetOrdinal(name);
                if (i < 0 || r.IsDBNull(i)) return 0;
                try { return r.GetInt32(i); } catch { return Convert.ToInt32(r.GetValue(i), CultureInfo.InvariantCulture); }
            }
            catch { return 0; }
        }

        // ---- Helpers to manage placeholder and child host ----
        private void ClearPlaceholder()
        {
            if (Placeholder != null) Placeholder.Visibility = Visibility.Collapsed;
        }

        private void ShowInDetail(UserControl view)
        {
            ClearPlaceholder();
            if (DetailHost != null)
                DetailHost.Content = view;

            // Try to subscribe to child's RefreshRequested event if it exists (no new files needed)
            TryHookChildRefresh(view);
        }

        // ---- Tile click handlers (create children with optional callback if supported) ----
        private void OpenAllSites_Click(object sender, RoutedEventArgs e)
        {
            ShowInDetail(CreateChild(typeof(AllHouseholdsView)));
        }

        private void OpenOperational_Click(object sender, RoutedEventArgs e)
        {
            ShowInDetail(CreateChild(typeof(OperationalHouseholdsView)));
        }

        private void OpenOutOfService_Click(object sender, RoutedEventArgs e)
        {
            ShowInDetail(CreateChild(typeof(OutOfServiceHouseholdsView)));
        }

        // Create child view using the best available constructor:
        // 1) (string userRole, Action notifyParent)
        // 2) (string userRole)
        // 3) (Action notifyParent)
        // 4) ()
        private UserControl CreateChild(Type t)
        {
            object instance = null;

            // try (string, Action)
            try
            {
                instance = Activator.CreateInstance(t, new object[] { _userRole, (Action)RefreshTiles });
            }
            catch { /* ignore */ }

            // try (string)
            if (instance == null)
            {
                try { instance = Activator.CreateInstance(t, new object[] { _userRole }); }
                catch { /* ignore */ }
            }

            // try (Action)
            if (instance == null)
            {
                try { instance = Activator.CreateInstance(t, new object[] { (Action)RefreshTiles }); }
                catch { /* ignore */ }
            }

            // try ()
            if (instance == null)
            {
                try { instance = Activator.CreateInstance(t, new object[] { }); }
                catch { /* ignore */ }
            }

            var uc = instance as UserControl;
            if (uc == null)
                throw new InvalidOperationException("Failed to create child view: " + t.FullName);

            // If the child exposes a way to set callback later (SetParentRefreshCallback(Action)),
            // call it reflectively (optional).
            TrySetParentCallback(uc);

            return uc;
        }

        private void TrySetParentCallback(UserControl view)
        {
            if (view == null) return;

            // Method: SetParentRefreshCallback(Action)
            MethodInfo mi = view.GetType().GetMethod("SetParentRefreshCallback",
                             BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (mi != null)
            {
                var ps = mi.GetParameters();
                if (ps.Length == 1 && ps[0].ParameterType == typeof(Action))
                {
                    try { mi.Invoke(view, new object[] { (Action)RefreshTiles }); } catch { }
                }
            }

            // Property or field named "NotifyParent" of type Action
            var pi = view.GetType().GetProperty("NotifyParent",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (pi != null && pi.CanWrite && pi.PropertyType == typeof(Action))
            {
                try { pi.SetValue(view, (Action)RefreshTiles, null); } catch { }
            }
            var fi = view.GetType().GetField("NotifyParent",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (fi != null && fi.FieldType == typeof(Action))
            {
                try { fi.SetValue(view, (Action)RefreshTiles); } catch { }
            }
        }

        private void TryHookChildRefresh(UserControl view)
        {
            if (view == null) return;

            // Event named RefreshRequested with signature EventHandler
            EventInfo ev = view.GetType().GetEvent("RefreshRequested",
                             BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (ev != null && ev.EventHandlerType == typeof(EventHandler))
            {
                try
                {
                    EventHandler h = OnChildRefreshRequested;
                    ev.AddEventHandler(view, h);
                }
                catch { /* best-effort */ }
            }
        }

        private void OnChildRefreshRequested(object sender, EventArgs e)
        {
            // hop to UI thread if raised from background
            if (!Dispatcher.CheckAccess()) Dispatcher.Invoke(RefreshTiles);
            else RefreshTiles();
        }

        private void ShowLandingIfAny()
        {
            switch (_landing)
            {
                case SitesLanding.All:
                    ShowInDetail(CreateChild(typeof(AllHouseholdsView)));
                    break;
                case SitesLanding.Operational:
                    ShowInDetail(CreateChild(typeof(OperationalHouseholdsView)));
                    break;
                case SitesLanding.OutOfService:
                    ShowInDetail(CreateChild(typeof(OutOfServiceHouseholdsView)));
                    break;
                case SitesLanding.None:
                default:
                    // Fallback safety: also open All by default
                    ShowInDetail(CreateChild(typeof(AllHouseholdsView)));
                    break;
            }
        }

        private void BreadcrumbDashboard_Click(object sender, RoutedEventArgs e)
        {
            Window host = Window.GetWindow(this);
            var mw = host as MainWindow;
            if (mw != null)
            {
                mw.NavigateTo(new DashboardView(_userRole));
            }
            else
            {
                var win = new Window
                {
                    Title = "Dashboard",
                    Content = new DashboardView(_userRole),
                    Owner = host,
                    Width = 1200,
                    Height = 750,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };
                win.ShowDialog();
            }
        }

        // ==================== DB version polling (safety net) ====================

        private void StartPulse()
        {
            if (_pulse != null) return;
            _lastDbVersion = GetDbVersionSafe();
            _pulse = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _pulse.Tick += Pulse_Tick;
            _pulse.Start();
        }

        private void StopPulse()
        {
            if (_pulse == null) return;
            _pulse.Stop();
            _pulse.Tick -= Pulse_Tick;
            _pulse = null;
        }

        private void Pulse_Tick(object sender, EventArgs e)
        {
            int v = GetDbVersionSafe();
            if (v != _lastDbVersion)
            {
                _lastDbVersion = v;
                RefreshTiles(); // DB changed somewhere -> refresh tiles
            }
        }

        private int GetDbVersionSafe()
        {
            try
            {
                using (var conn = DatabaseHelper.GetConnection())
                {
                    conn.Open();
                    using (var cmd = new SQLiteCommand("PRAGMA data_version;", conn))
                    {
                        object o = cmd.ExecuteScalar();
                        return o == null || o == DBNull.Value ? -1 : Convert.ToInt32(o, CultureInfo.InvariantCulture);
                    }
                }
            }
            catch
            {
                return _lastDbVersion;
            }
        }
    }
}
