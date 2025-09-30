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
using HouseholdMS.Resources; // <-- for Strings (localized resources)

namespace HouseholdMS.View.Dashboard
{
    public partial class OutOfServiceHouseholdsView : UserControl
    {
        // DB canonical labels (match your schema/triggers)
        private const string OPERATIONAL = "Operational";
        private const string IN_SERVICE = "In Service";        // UI bucket "Out of Service"
        private const string NOT_OPERATIONAL = "Not Operational";

        private readonly ObservableCollection<Household> allHouseholds = new ObservableCollection<Household>();
        private ICollectionView view;
        private GridViewColumnHeader _lastHeaderClicked;
        private ListSortDirection _lastDirection = ListSortDirection.Ascending;

        // NOTE: header text is localized now; sorting uses Header.Tag instead (see click handler).
        private readonly string _currentUserRoleDefault = "Admin";
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
            else
                _currentUserRole = _currentUserRoleDefault;

            // Ensure placeholder is localized & not treated as a real query
            UpdateSearchPlaceholder();  // sets Tag and (if empty) sets Text = Tag

            LoadHouseholds();

            // Sorting via column headers
            HouseholdListView.AddHandler(GridViewColumnHeader.ClickEvent, new RoutedEventHandler(GridViewColumnHeader_Click));

            // Double-click = open appropriate modal (ServiceCall for In Service; Edit otherwise)
            HouseholdListView.MouseDoubleClick += HouseholdListView_MouseDoubleClick;

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
            // keep original behavior
            return string.Equals(normalized, IN_SERVICE, StringComparison.Ordinal) ? "Out of Service" : normalized;
        }

        // ===== Placeholder helpers (locale-safe) =====
        private void UpdateSearchPlaceholder()
        {
            if (SearchBox == null) return;

            string ph = Strings.OOSV_SearchPlaceholder; // localized resource
            SearchBox.Tag = ph;

            if (string.IsNullOrWhiteSpace(SearchBox.Text))
            {
                SetPlaceholder(SearchBox, ph);
                _searchText = string.Empty;
            }
            else if (IsPlaceholder(SearchBox.Text, ph))
            {
                SetPlaceholder(SearchBox, ph);
                _searchText = string.Empty;
            }
            // else: user already typed something; leave it.
        }

        private static void SetPlaceholder(TextBox box, string text)
        {
            box.Text = text;
            box.Foreground = Brushes.Gray;
            box.FontStyle = FontStyles.Italic;
        }

        private static bool IsPlaceholder(string text, string tagValue)
            => string.Equals(text ?? string.Empty, tagValue ?? string.Empty, StringComparison.Ordinal);

        // ===== Filtering =====
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

                if (!string.IsNullOrEmpty(h.OwnerName) && h.OwnerName.ToLowerInvariant().Contains(search)) return true;
                if (!string.IsNullOrEmpty(h.UserName) && h.UserName.ToLowerInvariant().Contains(search)) return true;
                if (!string.IsNullOrEmpty(h.Municipality) && h.Municipality.ToLowerInvariant().Contains(search)) return true;
                if (!string.IsNullOrEmpty(h.District) && h.District.ToLowerInvariant().Contains(search)) return true;
                if (!string.IsNullOrEmpty(h.ContactNum) && h.ContactNum.ToLowerInvariant().Contains(search)) return true;

                int id;
                if (int.TryParse(search, out id) && h.HouseholdID == id) return true;

                return false;
            };

            view.Refresh();
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var tagText = SearchBox?.Tag as string ?? string.Empty;

            // If Text equals Tag (placeholder), treat as empty search
            if (SearchBox != null && IsPlaceholder(SearchBox.Text, tagText))
                _searchText = string.Empty;
            else
                _searchText = SearchBox == null ? string.Empty : SearchBox.Text;

            ApplyFilter();
        }

        private void ResetText(object sender, RoutedEventArgs e)
        {
            var box = sender as TextBox;
            if (box == null) return;

            if (string.IsNullOrWhiteSpace(box.Text))
            {
                SetPlaceholder(box, box.Tag as string ?? string.Empty);
                _searchText = string.Empty;
                ApplyFilter();
            }
        }

        private void ClearText(object sender, RoutedEventArgs e)
        {
            var box = sender as TextBox;
            if (box == null) return;

            if (IsPlaceholder(box.Text, box.Tag as string ?? string.Empty))
                box.Text = string.Empty;

            box.Foreground = Brushes.Black;
            box.FontStyle = FontStyles.Normal;

            _searchText = string.Empty;
            ApplyFilter();
        }

        // ===== Locale-safe sorting using Header.Tag (property name) =====
        private void GridViewColumnHeader_Click(object sender, RoutedEventArgs e)
        {
            var header = e.OriginalSource as GridViewColumnHeader;
            if (header == null) return;

            string sortBy = null;

            if (header.Content is FrameworkElement fe && fe.Tag is string tag && !string.IsNullOrWhiteSpace(tag))
            {
                sortBy = tag;
            }

            if (string.IsNullOrWhiteSpace(sortBy)) return;

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

                ctl.ServiceFinished += delegate { try { win.DialogResult = true; } catch { } win.Close(); };

                win.ShowDialog();

                LoadHouseholds();
                ApplyFilter();
                HouseholdListView.SelectedItem = null;
                RaiseParentRefresh();
            }
            else
            {
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

            void AddItem(string text)
            {
                var mi = new MenuItem { Header = text };
                mi.Click += delegate { ChangeStatus(h, text); };
                cm.Items.Add(mi);
            }

            AddItem(OPERATIONAL);
            AddItem(IN_SERVICE);
            AddItem(NOT_OPERATIONAL);

            cm.PlacementTarget = tb;
            cm.IsOpen = true;
        }

        private void ChangeStatus(Household h, string newStatus)
        {
            if (string.Equals(NormalizeStatus(h.Statuss), NormalizeStatus(newStatus), StringComparison.Ordinal))
                return;

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
