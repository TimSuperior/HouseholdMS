using System;
using System.Data.SQLite;
using System.Windows;
using System.Windows.Controls;
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

        private readonly string _userRole;
        private readonly SitesLanding _landing;

        public SitesView(string userRole)
        {
            InitializeComponent();

            _userRole = string.IsNullOrWhiteSpace(userRole) ? "User" : userRole.Trim();
            _landing = SitesLanding.None;

            Loaded += SitesView_Loaded;
            PreviewKeyDown += SitesView_PreviewKeyDown; // Alt+Left shortcut
        }

        public SitesView(string userRole, SitesLanding landing) : this(userRole)
        {
            _landing = landing;
        }

        public SitesView() : this("User") { }

        private void SitesView_Loaded(object sender, RoutedEventArgs e)
        {
            LoadHouseholdCountsFromDb();
            ShowLandingIfAny();
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
                System.Diagnostics.Debug.WriteLine("SitesView.LoadHouseholdCountsFromDb error: " + ex);
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

        private void ClearPlaceholder()
        {
            if (Placeholder != null) Placeholder.Visibility = Visibility.Collapsed;
        }

        private void ShowInDetail(UserControl view)
        {
            ClearPlaceholder();
            if (DetailHost != null)
            {
                DetailHost.Content = view;
            }
        }

        private void OpenAllSites_Click(object sender, RoutedEventArgs e)
        {
            ShowInDetail(new AllHouseholdsView(_userRole));
        }

        private void OpenOperational_Click(object sender, RoutedEventArgs e)
        {
            ShowInDetail(new OperationalHouseholdsView(_userRole));
        }

        private void OpenOutOfService_Click(object sender, RoutedEventArgs e)
        {
            ShowInDetail(new OutOfServiceHouseholdsView(_userRole));
        }

        private void ShowLandingIfAny()
        {
            switch (_landing)
            {
                case SitesLanding.All:
                    ShowInDetail(new AllHouseholdsView(_userRole));
                    break;
                case SitesLanding.Operational:
                    ShowInDetail(new OperationalHouseholdsView(_userRole));
                    break;
                case SitesLanding.OutOfService:
                    ShowInDetail(new OutOfServiceHouseholdsView(_userRole));
                    break;
                case SitesLanding.None:
                default:
                    // keep placeholder
                    break;
            }
        }

        private void BreadcrumbDashboard_Click(object sender, RoutedEventArgs e)
        {
            Window host = Window.GetWindow(this);
            MainWindow mw = host as MainWindow;
            if (mw != null)
            {
                mw.NavigateTo(new DashboardView(_userRole));
            }
            else
            {
                Window win = new Window
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
    }
}
