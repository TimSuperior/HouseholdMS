using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data.SQLite;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using HouseholdMS;              // DatabaseHelper
using HouseholdMS.Model;
using HouseholdMS.View.Dashboard; // AddServiceControl

namespace HouseholdMS.View.Dashboard
{
    public partial class OperationalHouseholdsView : UserControl
    {
        private const string OPERATIONAL = "Operational";
        private const string IN_SERVICE = "In Service";
        private const string NOT_OPERATIONAL = "Not Operational";

        private readonly ObservableCollection<Household> allHouseholds = new ObservableCollection<Household>();
        private ICollectionView view;

        private GridViewColumnHeader _lastHeaderClicked;
        private ListSortDirection _lastDirection = ListSortDirection.Ascending;

        private readonly Dictionary<string, string> _headerToProperty = new Dictionary<string, string>()
        {
            { "ID", "HouseholdID" },
            { "Owner Name", "OwnerName" },
            { "User Name", "UserName" },
            { "Municipality", "Municipality" },
            { "District", "District" },
            { "Contact", "ContactNum" },
            { "Installed", "InstallDate" },
            { "Last Inspect", "LastInspect" },
            { "Status", "Statuss" },
            { "Comment", "UserComm" }
        };

        private readonly string _currentUserRole;

        public OperationalHouseholdsView() : this(GetRoleFromMain()) { }

        public OperationalHouseholdsView(string userRole)
        {
            InitializeComponent();
            _currentUserRole = string.IsNullOrWhiteSpace(userRole) ? "User" : userRole;

            LoadHouseholds();
            HouseholdListView.AddHandler(GridViewColumnHeader.ClickEvent, new RoutedEventHandler(GridViewColumnHeader_Click));
            UpdateSearchPlaceholder();
            ApplyFilter();
        }

        private static string GetRoleFromMain()
        {
            try
            {
                MainWindow mw = Application.Current.MainWindow as MainWindow;
                if (mw != null)
                {
                    var roleProp = mw.GetType().GetProperty("CurrentUserRole");
                    if (roleProp != null)
                    {
                        string val = roleProp.GetValue(mw, null) as string;
                        if (!string.IsNullOrWhiteSpace(val)) return val;
                    }
                }
            }
            catch { }
            return "User";
        }

        public void LoadHouseholds()
        {
            allHouseholds.Clear();

            using (SQLiteConnection conn = DatabaseHelper.GetConnection())
            {
                conn.Open();

                const string sql = @"SELECT * FROM Households;";
                using (SQLiteCommand cmd = new SQLiteCommand(sql, conn))
                using (SQLiteDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        DateTime installDate = DateTime.TryParse(reader["InstallDate"] == DBNull.Value ? null : reader["InstallDate"].ToString(), out DateTime dt1) ? dt1 : DateTime.MinValue;
                        DateTime lastInspect = DateTime.TryParse(reader["LastInspect"] == DBNull.Value ? null : reader["LastInspect"].ToString(), out DateTime dt2) ? dt2 : DateTime.MinValue;

                        Household h = new Household
                        {
                            HouseholdID = Convert.ToInt32(reader["HouseholdID"]),
                            OwnerName = reader["OwnerName"] == DBNull.Value ? null : reader["OwnerName"].ToString(),
                            UserName = reader["UserName"] == DBNull.Value ? null : reader["UserName"].ToString(),
                            Municipality = reader["Municipality"] == DBNull.Value ? null : reader["Municipality"].ToString(),
                            District = reader["District"] == DBNull.Value ? null : reader["District"].ToString(),
                            ContactNum = reader["ContactNum"] == DBNull.Value ? null : reader["ContactNum"].ToString(),
                            InstallDate = installDate,
                            LastInspect = lastInspect,
                            UserComm = reader["UserComm"] != DBNull.Value ? reader["UserComm"].ToString() : string.Empty,
                            Statuss = reader["Statuss"] != DBNull.Value ? reader["Statuss"].ToString() : string.Empty
                        };

                        allHouseholds.Add(h);
                    }
                }
            }

            view = CollectionViewSource.GetDefaultView(allHouseholds);
            HouseholdListView.ItemsSource = view;
        }

        // Treat empty/whitespace as Operational. Normalize underscores/hyphens/spacing.
        private static bool IsOperational(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return true;
            char[] chars = s.Select(ch => char.IsWhiteSpace(ch) ? ' ' : char.ToLowerInvariant(ch)).ToArray();
            string t = new string(chars).Replace("_", " ").Replace("-", " ").Trim();
            while (t.Contains("  ")) t = t.Replace("  ", " ");
            return t.StartsWith("operational");
        }

        // ---- Search / filter ----
        private void UpdateSearchPlaceholder()
        {
            if (SearchBox == null) return;

            const string ph = "Search within \"Operational\"";
            if (string.IsNullOrWhiteSpace(SearchBox.Text) ||
                SearchBox.Text == "Search by owner, user, area or contact" ||
                SearchBox.Text == "Search all households")
            {
                SearchBox.Text = ph;
                SearchBox.Tag = ph;
                SearchBox.Foreground = Brushes.Gray;
                SearchBox.FontStyle = FontStyles.Italic;
            }
        }

        private void ApplyFilter()
        {
            if (view == null) return;

            string search = (SearchBox != null && SearchBox.Text != (string)SearchBox.Tag)
                ? (SearchBox.Text ?? string.Empty).Trim().ToLowerInvariant()
                : string.Empty;

            view.Filter = delegate (object obj)
            {
                Household h = obj as Household;
                if (h == null) return false;

                if (!IsOperational(h.Statuss)) return false;

                if (string.IsNullOrEmpty(search)) return true;

                return (h.OwnerName != null && h.OwnerName.ToLowerInvariant().Contains(search))
                    || (h.ContactNum != null && h.ContactNum.ToLowerInvariant().Contains(search))
                    || (h.UserName != null && h.UserName.ToLowerInvariant().Contains(search))
                    || (h.Municipality != null && h.Municipality.ToLowerInvariant().Contains(search))
                    || (h.District != null && h.District.ToLowerInvariant().Contains(search));
            };

            view.Refresh();
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter();
        }

        private void ResetText(object sender, RoutedEventArgs e)
        {
            TextBox box = sender as TextBox;
            if (box != null && string.IsNullOrWhiteSpace(box.Text))
            {
                box.Text = box.Tag as string;
                box.Foreground = Brushes.Gray;
                box.FontStyle = FontStyles.Italic;
                ApplyFilter();
            }
        }

        private void ClearText(object sender, RoutedEventArgs e)
        {
            TextBox box = sender as TextBox;
            if (box == null) return;

            if (box.Text == box.Tag as string)
                box.Text = string.Empty;

            box.Foreground = Brushes.Black;
            box.FontStyle = FontStyles.Normal;

            ApplyFilter();
        }

        // ---- Sorting ----
        private void GridViewColumnHeader_Click(object sender, RoutedEventArgs e)
        {
            GridViewColumnHeader header = e.OriginalSource as GridViewColumnHeader;
            if (header == null) return;

            string headerText = header.Content == null ? null : header.Content.ToString();
            if (string.IsNullOrEmpty(headerText) || !_headerToProperty.ContainsKey(headerText)) return;

            string sortBy = _headerToProperty[headerText];
            ListSortDirection direction = (_lastHeaderClicked == header && _lastDirection == ListSortDirection.Ascending)
                ? ListSortDirection.Descending
                : ListSortDirection.Ascending;

            _lastHeaderClicked = header;
            _lastDirection = direction;

            view.SortDescriptions.Clear();
            view.SortDescriptions.Add(new SortDescription(sortBy, direction));
            view.Refresh();
        }

        // === Double-click to open AddServiceControl (dialog). Single click = select only.
        private void HouseholdListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            Household selected = HouseholdListView.SelectedItem as Household;
            if (selected == null) return;

            // Block if there is already an open service
            bool hasOpenService = false;
            using (SQLiteConnection conn = DatabaseHelper.GetConnection())
            {
                conn.Open();
                using (SQLiteCommand chk = new SQLiteCommand(
                    "SELECT 1 FROM Service WHERE HouseholdID=@hid AND FinishDate IS NULL LIMIT 1", conn))
                {
                    chk.Parameters.AddWithValue("@hid", selected.HouseholdID);
                    hasOpenService = chk.ExecuteScalar() != null;
                }
            }

            if (hasOpenService)
            {
                MessageBox.Show("An open service already exists for this household.",
                                "Already Open", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Host AddServiceControl inside a wide dialog
            AddServiceControl svc = new AddServiceControl(selected, _currentUserRole);

            Window dlg = CreateWideDialog(svc,
                string.Format("Start Service Call — Household #{0}", selected.HouseholdID));

            EventHandler onCreated = null;
            EventHandler onCancel = null;

            onCreated = delegate (object s, EventArgs args)
            {
                svc.ServiceCreated -= onCreated;
                svc.CancelRequested -= onCancel;
                try { dlg.DialogResult = true; } catch { }
                dlg.Close();

                // After confirming, reload (status may change outside this view's control)
                LoadHouseholds();
                ApplyFilter();
                HouseholdListView.SelectedItem = null;
            };

            onCancel = delegate (object s, EventArgs args)
            {
                svc.ServiceCreated -= onCreated;
                svc.CancelRequested -= onCancel;
                try { dlg.DialogResult = false; } catch { }
                dlg.Close();
            };

            svc.ServiceCreated += onCreated;
            svc.CancelRequested += onCancel;

            dlg.ShowDialog();
        }

        // =====(Kept as a safe no-op if something else wires it accidentally)=====
        private void StatusText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // READ-ONLY in this view by design: do nothing.
            e.Handled = true;
        }

        private Window CreateWideDialog(FrameworkElement content, string title)
        {
            Grid host = new Grid { Margin = new Thickness(16) };
            host.Children.Add(content);

            Window owner = Window.GetWindow(this);

            Window dlg = new Window
            {
                Title = title,
                Owner = owner,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.CanResize,
                ShowInTaskbar = false,
                Content = host,
                Width = 1100,
                Height = 760,
                MinWidth = 900,
                MinHeight = 600,
                Background = Brushes.White
            };

            try { if (owner != null) dlg.Icon = owner.Icon; } catch { }
            return dlg;
        }
    }
}
