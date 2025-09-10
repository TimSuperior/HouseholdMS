using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using HouseholdMS.View.UserControls; // AddHouseholdControl (edit form)
using HouseholdMS.Model;
using System.Data.SQLite;
using System.Windows.Media;

namespace HouseholdMS.View.Dashboard
{
    public partial class OutOfServiceHouseholdsView : UserControl
    {
        // DB canonical labels (match your schema/triggers)
        private const string OPERATIONAL = "Operational";
        private const string IN_SERVICE = "In Service";        // This is your "Out of Service" bucket in UI
        private const string NOT_OPERATIONAL = "Not Operational";

        private readonly ObservableCollection<Household> allHouseholds = new ObservableCollection<Household>();
        private ICollectionView view;
        private GridViewColumnHeader _lastHeaderClicked;
        private ListSortDirection _lastDirection = ListSortDirection.Ascending;

        private readonly Dictionary<string, string> _headerToProperty = new Dictionary<string, string>
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

        private string _currentUserRole = "Admin";

        // Default to only show "In Service" (out-of-service) in this view
        private string _normalizedStatusFilter = IN_SERVICE;
        private bool _categoryFilterActive = true;
        private string _searchText = string.Empty;

        // ===== Optional parent refresh hook (no extra files) =====
        private Action _notifyParent;
        public Action NotifyParent { get { return _notifyParent; } set { _notifyParent = value; } }
        public void SetParentRefreshCallback(Action cb) { _notifyParent = cb; }
        public event EventHandler RefreshRequested;

        private void RaiseParentRefresh()
        {
            var cb = _notifyParent;
            if (cb != null)
            {
                try { cb(); } catch { }
            }

            var h = RefreshRequested;
            if (h != null)
            {
                try { h(this, EventArgs.Empty); } catch { }
            }
        }
        // ========================================================

        public OutOfServiceHouseholdsView(string userRole)
        {
            InitializeComponent();

            if (!string.IsNullOrWhiteSpace(userRole))
                _currentUserRole = userRole;

            LoadHouseholds();

            // Sorting via column headers
            HouseholdListView.AddHandler(GridViewColumnHeader.ClickEvent, new RoutedEventHandler(GridViewColumnHeader_Click));

            // Double-click = open appropriate modal (ServiceCall for In Service; Edit otherwise)
            HouseholdListView.MouseDoubleClick += HouseholdListView_MouseDoubleClick;

            UpdateSearchPlaceholder();
            ApplyFilter();
        }

        // Overloads so SitesView can pass a callback directly (optional)
        public OutOfServiceHouseholdsView(string userRole, Action notifyParent) : this(userRole)
        {
            _notifyParent = notifyParent;
        }
        public OutOfServiceHouseholdsView(Action notifyParent) : this("Admin")
        {
            _notifyParent = notifyParent;
        }

        public void LoadHouseholds()
        {
            allHouseholds.Clear();

            using (var conn = DatabaseHelper.GetConnection())
            {
                conn.Open();

                // Pull all; UI filter will scope to "In Service" for this view.
                using (var cmd = new SQLiteCommand("SELECT HouseholdID, OwnerName, UserName, Municipality, District, ContactNum, InstallDate, LastInspect, UserComm, Statuss FROM Households;", conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        DateTime installDate;
                        DateTime lastInspect;

                        var installRaw = reader["InstallDate"] == DBNull.Value ? null : Convert.ToString(reader["InstallDate"]);
                        var lastRaw = reader["LastInspect"] == DBNull.Value ? null : Convert.ToString(reader["LastInspect"]);

                        installDate = DateTime.TryParse(installRaw, out var dt1) ? dt1 : DateTime.MinValue;
                        lastInspect = DateTime.TryParse(lastRaw, out var dt2) ? dt2 : DateTime.MinValue;

                        var h = new Household
                        {
                            HouseholdID = Convert.ToInt32(reader["HouseholdID"]),
                            OwnerName = reader["OwnerName"] == DBNull.Value ? string.Empty : Convert.ToString(reader["OwnerName"]),
                            UserName = reader["UserName"] == DBNull.Value ? string.Empty : Convert.ToString(reader["UserName"]),
                            Municipality = reader["Municipality"] == DBNull.Value ? string.Empty : Convert.ToString(reader["Municipality"]),
                            District = reader["District"] == DBNull.Value ? string.Empty : Convert.ToString(reader["District"]),
                            ContactNum = reader["ContactNum"] == DBNull.Value ? string.Empty : Convert.ToString(reader["ContactNum"]),
                            InstallDate = installDate,
                            LastInspect = lastInspect,
                            UserComm = reader["UserComm"] == DBNull.Value ? string.Empty : Convert.ToString(reader["UserComm"]),
                            Statuss = reader["Statuss"] == DBNull.Value ? string.Empty : Convert.ToString(reader["Statuss"])
                        };

                        allHouseholds.Add(h);
                    }
                }
            }

            view = CollectionViewSource.GetDefaultView(allHouseholds);
            HouseholdListView.ItemsSource = view;
        }

        private static string NormalizeStatus(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;

            var t = s.Trim().ToLowerInvariant();
            t = t.Replace('_', ' ').Replace('-', ' ');
            while (t.Contains("  ")) t = t.Replace("  ", " ");

            if (t.StartsWith("operational")) return OPERATIONAL;
            if (t.StartsWith("in service") || t.StartsWith("service")) return IN_SERVICE;
            if (t.StartsWith("not operational") || t.StartsWith("notoperational")) return NOT_OPERATIONAL;

            // Fallback: return original trimmed
            return s.Trim();
        }

        private static string DisplayStatusLabel(string normalized)
        {
            // UI-friendly label (your product language)
            return string.Equals(normalized, IN_SERVICE, StringComparison.Ordinal) ? "Out of Service" : normalized;
        }

        private void UpdateSearchPlaceholder()
        {
            if (SearchBox == null) return;

            string ph = _categoryFilterActive
                ? "Search within \"" + DisplayStatusLabel(_normalizedStatusFilter) + "\""
                : "Search all households";

            SearchBox.Tag = ph;

            if (string.IsNullOrWhiteSpace(SearchBox.Text) ||
                SearchBox.Text == "Search by owner, user, area or contact" ||
                SearchBox.Text == "Search all households")
            {
                SearchBox.Text = ph;
                SearchBox.Foreground = Brushes.Gray;
                SearchBox.FontStyle = FontStyles.Italic;
            }
        }

        private void ApplyFilter()
        {
            if (view == null) return;

            string search = _searchText == null ? string.Empty : _searchText.Trim().ToLowerInvariant();
            bool useCategory = _categoryFilterActive && !string.IsNullOrWhiteSpace(_normalizedStatusFilter);

            view.Filter = delegate (object obj)
            {
                var h = obj as Household;
                if (h == null) return false;

                bool categoryOk = true;
                if (useCategory)
                {
                    var hn = NormalizeStatus(h.Statuss);
                    categoryOk = hn == _normalizedStatusFilter;
                }
                if (!categoryOk) return false;

                if (string.IsNullOrEmpty(search)) return true;

                // string contains search on several fields
                if (!string.IsNullOrEmpty(h.OwnerName) && h.OwnerName.ToLowerInvariant().Contains(search)) return true;
                if (!string.IsNullOrEmpty(h.UserName) && h.UserName.ToLowerInvariant().Contains(search)) return true;
                if (!string.IsNullOrEmpty(h.Municipality) && h.Municipality.ToLowerInvariant().Contains(search)) return true;
                if (!string.IsNullOrEmpty(h.District) && h.District.ToLowerInvariant().Contains(search)) return true;
                if (!string.IsNullOrEmpty(h.ContactNum) && h.ContactNum.ToLowerInvariant().Contains(search)) return true;

                // Numeric search for ID (optional)
                int id;
                if (int.TryParse(search, out id) && h.HouseholdID == id) return true;

                return false;
            };

            view.Refresh();
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (SearchBox != null && SearchBox.Text == (SearchBox.Tag as string))
                _searchText = string.Empty;
            else
                _searchText = SearchBox == null ? string.Empty : SearchBox.Text;

            ApplyFilter();
        }

        private void ResetText(object sender, RoutedEventArgs e)
        {
            var box = sender as TextBox;
            if (box != null && string.IsNullOrWhiteSpace(box.Text))
            {
                box.Text = box.Tag as string;
                box.Foreground = Brushes.Gray;
                box.FontStyle = FontStyles.Italic;
                _searchText = string.Empty;
                ApplyFilter();
            }
        }

        private void ClearText(object sender, RoutedEventArgs e)
        {
            var box = sender as TextBox;
            if (box == null) return;

            if (box.Text == box.Tag as string)
                box.Text = string.Empty;

            box.Foreground = Brushes.Black;
            box.FontStyle = FontStyles.Normal;

            _searchText = string.Empty;
            ApplyFilter();
        }

        private void GridViewColumnHeader_Click(object sender, RoutedEventArgs e)
        {
            var header = e.OriginalSource as GridViewColumnHeader;
            if (header == null) return;

            string headerText = header.Content == null ? null : header.Content.ToString();
            if (string.IsNullOrEmpty(headerText) || !_headerToProperty.ContainsKey(headerText)) return;

            string sortBy = _headerToProperty[headerText];

            ListSortDirection direction =
                (_lastHeaderClicked == header && _lastDirection == ListSortDirection.Ascending)
                ? ListSortDirection.Descending
                : ListSortDirection.Ascending;

            _lastHeaderClicked = header;
            _lastDirection = direction;

            view.SortDescriptions.Clear();
            view.SortDescriptions.Add(new SortDescription(sortBy, direction));
            view.Refresh();
        }

        // ===================== Double-click behavior =====================
        private void HouseholdListView_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var selected = HouseholdListView.SelectedItem as Household;
            if (selected == null) return;

            string norm = NormalizeStatus(selected.Statuss);

            if (norm == IN_SERVICE)
            {
                // Out-of-Service bucket → open ServiceCallDetailControl as modal
                var ctl = new HouseholdMS.View.Dashboard.ServiceCallDetailControl(selected, _currentUserRole);

                var win = new Window
                {
                    Title = "Service Call Details — #" + selected.HouseholdID + " • " + selected.OwnerName,
                    Content = ctl,
                    Owner = Window.GetWindow(this),
                    Width = 1100,
                    Height = 760,
                    MinWidth = 900,
                    MinHeight = 600,
                    ResizeMode = ResizeMode.CanResize,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Background = Brushes.White
                };

                // Close when service finished (event raised by control)
                ctl.ServiceFinished += delegate { try { win.DialogResult = true; } catch { } win.Close(); };

                win.ShowDialog();

                // Reload & notify after closing
                LoadHouseholds();
                ApplyFilter();
                HouseholdListView.SelectedItem = null;
                RaiseParentRefresh();
            }
            else
            {
                // Non "In Service": open edit form
                var ctl = new AddHouseholdControl(selected); // edit mode when instance is passed

                var win = new Window
                {
                    Title = "Edit Household — #" + selected.HouseholdID + " • " + selected.OwnerName,
                    Content = ctl,
                    Owner = Window.GetWindow(this),
                    Width = 1000,
                    Height = 720,
                    MinWidth = 820,
                    MinHeight = 560,
                    ResizeMode = ResizeMode.CanResize,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Background = Brushes.White
                };

                ctl.OnSavedSuccessfully += delegate { try { win.DialogResult = true; } catch { } win.Close(); };
                ctl.OnCancelRequested += delegate { win.Close(); };

                bool? result = win.ShowDialog();
                if (result == true)
                {
                    LoadHouseholds();
                    ApplyFilter();
                    RaiseParentRefresh();
                }
                HouseholdListView.SelectedItem = null;
            }
        }

        // ===================== Status change context menu =====================
        private void StatusText_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var tb = sender as TextBlock;
            if (tb == null) return;
            var h = tb.DataContext as Household;
            if (h == null) return;

            var cm = new ContextMenu();

            Action<string> addItem = delegate (string text)
            {
                var mi = new MenuItem { Header = text };
                mi.Click += delegate { ChangeStatus(h, text); };
                cm.Items.Add(mi);
            };

            addItem(OPERATIONAL);
            addItem(IN_SERVICE);
            addItem(NOT_OPERATIONAL);

            cm.PlacementTarget = tb;
            cm.IsOpen = true;
        }

        private void ChangeStatus(Household h, string newStatus)
        {
            // Avoid unnecessary writes
            if (string.Equals(NormalizeStatus(h.Statuss), NormalizeStatus(newStatus), StringComparison.Ordinal))
                return;

            // Warn when moving into "In Service" because a Service ticket will be opened by trigger
            if (NormalizeStatus(newStatus) == IN_SERVICE)
            {
                var confirm = MessageBox.Show(
                    "Move household \"" + h.OwnerName + "\" to In Service?\n\n" +
                    "This will create/open a Service ticket.",
                    "Confirm Status Change", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (confirm != MessageBoxResult.Yes) return;
            }

            try
            {
                using (var conn = DatabaseHelper.GetConnection())
                {
                    conn.Open();
                    using (var tx = conn.BeginTransaction())
                    {
                        using (var cmd = new SQLiteCommand(
                            "UPDATE Households SET Statuss=@st WHERE HouseholdID=@id;", conn, tx))
                        {
                            cmd.Parameters.AddWithValue("@st", newStatus);
                            cmd.Parameters.AddWithValue("@id", h.HouseholdID);
                            cmd.ExecuteNonQuery();
                        }

                        // The DB trigger will auto-create a Service row when Statuss becomes 'In Service'.
                        tx.Commit();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to change status.\n" + ex.Message, "Error",
                                MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            LoadHouseholds();
            ApplyFilter();
            RaiseParentRefresh();
        }
    }
}
