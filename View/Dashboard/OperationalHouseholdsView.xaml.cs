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

        // ===== Minimal parent refresh hook (no new files) =====
        private Action _notifyParent;
        public Action NotifyParent
        {
            get { return _notifyParent; }
            set { _notifyParent = value; }
        }
        public void SetParentRefreshCallback(Action cb) { _notifyParent = cb; }
        public event EventHandler RefreshRequested; // optional for event-based wiring
        private void RaiseParentRefresh()
        {
            var cb = _notifyParent;
            if (cb != null) { try { cb(); } catch { } }
            var h = RefreshRequested;
            if (h != null) { try { h(this, EventArgs.Empty); } catch { } }
        }
        // ======================================================

        // ===== Column chooser state (empty = preserve old behavior) =====
        private static readonly string[] AllColumnKeys = new[]
        {
            nameof(Household.HouseholdID),
            nameof(Household.OwnerName),
            nameof(Household.UserName),
            nameof(Household.Municipality),
            nameof(Household.District),
            nameof(Household.ContactNum),
            "InstallDateText",
            "LastInspectText",
            nameof(Household.Statuss),
            nameof(Household.UserComm)
        };

        // Default search columns = EXACTLY what this view used before (no ID match)
        private static readonly string[] DefaultColumnKeys = new[]
        {
            nameof(Household.OwnerName),
            nameof(Household.ContactNum),
            nameof(Household.UserName),
            nameof(Household.Municipality),
            nameof(Household.District)
        };

        private readonly HashSet<string> _selectedColumnKeys = new HashSet<string>(StringComparer.Ordinal);

        public OperationalHouseholdsView() : this(GetRoleFromMain()) { }

        public OperationalHouseholdsView(string userRole)
        {
            InitializeComponent();
            _currentUserRole = string.IsNullOrWhiteSpace(userRole) ? "User" : userRole;

            LoadHouseholds();
            HouseholdListView.AddHandler(GridViewColumnHeader.ClickEvent, new RoutedEventHandler(GridViewColumnHeader_Click));
            UpdateSearchPlaceholder();
            UpdateColumnFilterButtonContent();
            ApplyFilter();
        }

        // New overloads to allow SitesView to pass a callback directly
        public OperationalHouseholdsView(string userRole, Action notifyParent) : this(userRole)
        {
            _notifyParent = notifyParent;
        }
        public OperationalHouseholdsView(Action notifyParent) : this(GetRoleFromMain(), notifyParent) { }

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

            // Decide which columns to use (empty => preserve old behavior)
            var keys = _selectedColumnKeys.Count == 0 ? DefaultColumnKeys : _selectedColumnKeys.ToArray();

            view.Filter = delegate (object obj)
            {
                Household h = obj as Household;
                if (h == null) return false;

                if (!IsOperational(h.Statuss)) return false;

                if (string.IsNullOrEmpty(search)) return true;

                if (_selectedColumnKeys.Count == 0)
                {
                    // ORIGINAL behavior: owner/contact/username/municipality/district
                    return (h.OwnerName != null && h.OwnerName.ToLowerInvariant().Contains(search))
                        || (h.ContactNum != null && h.ContactNum.ToLowerInvariant().Contains(search))
                        || (h.UserName != null && h.UserName.ToLowerInvariant().Contains(search))
                        || (h.Municipality != null && h.Municipality.ToLowerInvariant().Contains(search))
                        || (h.District != null && h.District.ToLowerInvariant().Contains(search));
                }

                // Column-based search when user selected columns
                for (int i = 0; i < keys.Length; i++)
                {
                    string cell = GetCellString(h, keys[i]);
                    if (!string.IsNullOrEmpty(cell) && cell.ToLowerInvariant().Contains(search))
                        return true;
                }
                return false;
            };

            view.Refresh();
        }

        private static string GetCellString(Household h, string key)
        {
            switch (key)
            {
                case nameof(Household.HouseholdID): return h.HouseholdID.ToString();
                case nameof(Household.OwnerName): return h.OwnerName ?? string.Empty;
                case nameof(Household.UserName): return h.UserName ?? string.Empty;
                case nameof(Household.Municipality): return h.Municipality ?? string.Empty;
                case nameof(Household.District): return h.District ?? string.Empty;
                case nameof(Household.ContactNum): return h.ContactNum ?? string.Empty;
                case "InstallDateText": return h.InstallDate == DateTime.MinValue ? string.Empty : h.InstallDate.ToString("yyyy-MM-dd");
                case "LastInspectText": return h.LastInspect == DateTime.MinValue ? string.Empty : h.LastInspect.ToString("yyyy-MM-dd");
                case nameof(Household.Statuss): return h.Statuss ?? string.Empty;
                case nameof(Household.UserComm): return h.UserComm ?? string.Empty;
                default: return string.Empty;
            }
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

                // Tell SitesView to refresh tiles (minimal hook)
                RaiseParentRefresh();
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

        // ===== Column chooser UI handlers =====
        private void ColumnFilterButton_Click(object sender, RoutedEventArgs e)
        {
            ColumnPopup.IsOpen = true;
        }

        private void ColumnPopup_Closed(object sender, EventArgs e)
        {
            UpdateColumnFilterButtonContent();

            // Re-apply filter if there's text
            string text = SearchBox != null ? (SearchBox.Text ?? string.Empty) : string.Empty;
            if (!string.IsNullOrWhiteSpace(text) && text != (string)SearchBox.Tag)
                ApplyFilter();
        }

        private void ColumnCheckBox_Click(object sender, RoutedEventArgs e)
        {
            var cb = sender as CheckBox;
            var key = cb != null ? cb.Tag as string : null;
            if (string.IsNullOrWhiteSpace(key)) return;

            if (cb.IsChecked == true) _selectedColumnKeys.Add(key);
            else _selectedColumnKeys.Remove(key);
        }

        private void SelectAllColumns_Click(object sender, RoutedEventArgs e)
        {
            _selectedColumnKeys.Clear();
            foreach (var child in FindPopupCheckBoxes()) child.IsChecked = true;
            for (int i = 0; i < AllColumnKeys.Length; i++) _selectedColumnKeys.Add(AllColumnKeys[i]);
        }

        private void ClearAllColumns_Click(object sender, RoutedEventArgs e)
        {
            _selectedColumnKeys.Clear(); // empty => default behavior
            foreach (var child in FindPopupCheckBoxes()) child.IsChecked = false;
        }

        private IEnumerable<CheckBox> FindPopupCheckBoxes()
        {
            var border = ColumnPopup.Child as Border;
            if (border == null) yield break;

            var sp = border.Child as StackPanel;
            if (sp == null) yield break;

            var sv = sp.Children.OfType<ScrollViewer>().FirstOrDefault();
            if (sv == null) yield break;

            var inner = sv.Content as StackPanel;
            if (inner == null) yield break;

            foreach (var child in inner.Children)
            {
                var cb = child as CheckBox;
                if (cb != null) yield return cb;
            }
        }

        private void UpdateColumnFilterButtonContent()
        {
            if (_selectedColumnKeys.Count == 0)
                ColumnFilterButton.Content = "All ▾";
            else
                ColumnFilterButton.Content = _selectedColumnKeys.Count + " selected ▾";
        }
    }
}
