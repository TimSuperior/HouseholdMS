using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Input;
using HouseholdMS.Model;
using HouseholdMS.View.UserControls;     // AddHouseholdControl

namespace HouseholdMS.View.Dashboard
{
    public partial class AllHouseholdsView : UserControl
    {
        // Canonical DB labels (match your schema)
        private const string OPERATIONAL = "Operational";
        private const string IN_SERVICE = "In Service";          // UI shows this as "Out of Service"
        private const string NOT_OPERATIONAL = "Not Operational";

        private readonly ObservableCollection<Household> allHouseholds = new ObservableCollection<Household>();
        private ICollectionView view;
        private GridViewColumnHeader _lastHeaderClicked;
        private ListSortDirection _lastDirection = ListSortDirection.Ascending;

        // Prevent re-entrancy / reopen when closing dialogs
        private bool _modalOpen = false;

        // GridView header → Household property map
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

        private readonly string _currentUserRole;
        private bool IsAdmin { get { return string.Equals(_currentUserRole, "Admin", StringComparison.OrdinalIgnoreCase); } }

        // Filters
        private string _normalizedStatusFilter = string.Empty; // empty → all statuses
        private bool _categoryFilterActive = false;
        private string _searchText = string.Empty;

        // === Optional parent refresh hook (no extra files) ===
        private Action _notifyParent;
        public Action NotifyParent
        {
            get { return _notifyParent; }
            set { _notifyParent = value; }
        }
        public void SetParentRefreshCallback(Action cb)
        {
            _notifyParent = cb;
        }
        private void RaiseParentRefresh()
        {
            var cb = _notifyParent;
            if (cb != null) { try { cb(); } catch { } }
        }

        // === Constructors ===
        public AllHouseholdsView(string userRole)
        {
            _currentUserRole = string.IsNullOrWhiteSpace(userRole) ? "User" : userRole.Trim();
            InitializeAndLoad();
        }
        public AllHouseholdsView(string userRole, Action notifyParent)
        {
            _currentUserRole = string.IsNullOrWhiteSpace(userRole) ? "User" : userRole.Trim();
            _notifyParent = notifyParent;
            InitializeAndLoad();
        }
        public AllHouseholdsView() : this("User") { }
        public AllHouseholdsView(Action notifyParent) : this("User", notifyParent) { }

        private void InitializeAndLoad()
        {
            InitializeComponent();

            LoadHouseholds();

            // Sort on header click
            HouseholdListView.AddHandler(GridViewColumnHeader.ClickEvent, new RoutedEventHandler(GridViewColumnHeader_Click));

            // Double-click open → ALWAYS open AddHouseholdControl (admin=edit, non-admin=read-only)
            HouseholdListView.MouseDoubleClick += HouseholdListView_MouseDoubleClick;

            ApplyAccessRestrictions();
            UpdateSearchPlaceholder();
            ApplyFilter();
        }

        private void ApplyAccessRestrictions()
        {
            var addBtn = FindName("AddHouseholdButton") as Button;
            if (addBtn != null) addBtn.Visibility = IsAdmin ? Visibility.Visible : Visibility.Collapsed;
        }

        // === Data Load ===
        public void LoadHouseholds()
        {
            allHouseholds.Clear();

            using (var conn = DatabaseHelper.GetConnection())
            {
                conn.Open();
                using (var cmd = new System.Data.SQLite.SQLiteCommand(
                    "SELECT HouseholdID, OwnerName, UserName, Municipality, District, ContactNum, InstallDate, LastInspect, UserComm, Statuss FROM Households;", conn))
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

        // === Status helpers ===
        private static string NormalizeStatus(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;

            var t = s.Trim().ToLowerInvariant();
            t = t.Replace('_', ' ').Replace('-', ' ');
            while (t.Contains("  ")) t = t.Replace("  ", " ");

            if (t.StartsWith("operational")) return OPERATIONAL;
            if (t.StartsWith("in service") || t.StartsWith("service")) return IN_SERVICE;
            if (t.StartsWith("not operational") || t.StartsWith("notoperational")) return NOT_OPERATIONAL;

            return s.Trim();
        }

        private static string DisplayStatusLabel(string normalized)
        {
            // Product copy: show "Out of Service" for the DB value "In Service"
            return string.Equals(normalized, IN_SERVICE, StringComparison.Ordinal) ? "Out of Service" : normalized;
        }

        // === Search & filter ===
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

                // String contains on several text fields
                if (!string.IsNullOrEmpty(h.OwnerName) && h.OwnerName.ToLowerInvariant().Contains(search)) return true;
                if (!string.IsNullOrEmpty(h.UserName) && h.UserName.ToLowerInvariant().Contains(search)) return true;
                if (!string.IsNullOrEmpty(h.Municipality) && h.Municipality.ToLowerInvariant().Contains(search)) return true;
                if (!string.IsNullOrEmpty(h.District) && h.District.ToLowerInvariant().Contains(search)) return true;
                if (!string.IsNullOrEmpty(h.ContactNum) && h.ContactNum.ToLowerInvariant().Contains(search)) return true;

                // Numeric search for exact ID
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

        // === Sorting ===
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

        // === Add new household (Admins only) ===
        private void AddHouseholdButton_Click(object sender, RoutedEventArgs e)
        {
            if (!IsAdmin)
            {
                MessageBox.Show("Only admins can add households.", "Access Denied",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_modalOpen) return; // guard against re-entry

            var form = new AddHouseholdControl();
            form.Loaded += delegate { LockStatusInputsInForm(form); };

            var dialog = CreateWideDialog(form, "Add Household");

            form.OnSavedSuccessfully += delegate
            {
                try { dialog.DialogResult = true; } catch { }
                dialog.Close();
                LoadHouseholds();
                ApplyFilter();
                RaiseParentRefresh();
            };
            form.OnCancelRequested += delegate
            {
                try { dialog.DialogResult = false; } catch { }
                dialog.Close();
            };

            ShowDialogSafe(dialog);
        }

        // === Double-click open → ALWAYS open AddHouseholdControl ===
        private void HouseholdListView_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (_modalOpen) return;

            var selected = HouseholdListView.SelectedItem as Household;
            if (selected == null) return;

            var form = new AddHouseholdControl(selected); // edit mode when entity is passed
            form.Loaded += delegate
            {
                // Keep existing lock for status inputs
                LockStatusInputsInForm(form);

                // Non-admins: open the same control but read-only (preserves previous "read-only details" behavior)
                if (!IsAdmin)
                {
                    SetFormReadOnly(form);
                }
            };

            var dlg = CreateWideDialog(form, "Edit Household #" + selected.HouseholdID);
            form.OnSavedSuccessfully += delegate
            {
                // Only fires for admins (Save shown). Non-admins won't see Save/Delete.
                try { dlg.DialogResult = true; } catch { }
                dlg.Close();
                LoadHouseholds();
                ApplyFilter();
                RaiseParentRefresh();
            };
            form.OnCancelRequested += delegate
            {
                try { dlg.DialogResult = false; } catch { }
                dlg.Close();
            };

            ShowDialogSafe(dlg);
        }

        // === Utilities ===
        private Window CreateWideDialog(FrameworkElement content, string title)
        {
            var host = new Grid { Margin = new Thickness(16) };
            host.Children.Add(content);

            var owner = Window.GetWindow(this);

            var dlg = new Window
            {
                Title = title,
                Owner = owner,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.CanResize,
                ShowInTaskbar = false,
                Content = host,
                Width = 980,
                Height = 700,
                MinWidth = 820,
                MinHeight = 560,
                Background = Brushes.White
            };

            try { if (owner != null) dlg.Icon = owner.Icon; } catch { }
            return dlg;
        }

        // Centralized, safe ShowDialog with re-entrancy guard and input capture release
        private void ShowDialogSafe(Window dlg)
        {
            if (dlg == null) return;
            if (_modalOpen) return;

            _modalOpen = true;
            HouseholdListView.IsEnabled = false;  // avoid extra interactions while modal is up

            try
            {
                Mouse.Capture(null);
                dlg.ShowDialog();
            }
            finally
            {
                HouseholdListView.IsEnabled = true;
                _modalOpen = false;
            }
        }

        // Disable any status input inside AddHouseholdControl (we manage status via workflows elsewhere)
        private void LockStatusInputsInForm(UserControl form)
        {
            if (form == null) return;

            Action<DependencyObject> dfs = null;
            dfs = delegate (DependencyObject node)
            {
                if (node == null) return;
                int count = VisualTreeHelper.GetChildrenCount(node);
                for (int i = 0; i < count; i++)
                {
                    var child = VisualTreeHelper.GetChild(node, i);
                    var fe = child as FrameworkElement;
                    if (fe != null)
                    {
                        var name = fe.Name ?? string.Empty;
                        var tagStr = (fe.Tag as string) ?? string.Empty;

                        bool looksLikeStatus =
                            (!string.IsNullOrEmpty(name) && name.IndexOf("status", StringComparison.OrdinalIgnoreCase) >= 0)
                            || string.Equals(tagStr, "status-field", StringComparison.OrdinalIgnoreCase);

                        if (looksLikeStatus)
                        {
                            fe.IsEnabled = false;
                            fe.IsHitTestVisible = false;
                            fe.Opacity = 0.6;
                        }
                    }
                    dfs(child);
                }
            };

            dfs(form);
        }

        // Make the AddHouseholdControl read-only for non-admin users
        private void SetFormReadOnly(AddHouseholdControl form)
        {
            // Hide Save/Delete, rename Cancel to Close
            var saveBtn = form.FindName("SaveButton") as Button;
            if (saveBtn != null) saveBtn.Visibility = Visibility.Collapsed;

            var delBtn = form.FindName("DeleteButton") as Button;
            if (delBtn != null) delBtn.Visibility = Visibility.Collapsed;

            var cancelBtn = form.FindName("CancelButton") as Button;
            if (cancelBtn != null) cancelBtn.Content = "Close";

            // Disable inputs (TextBox read-only; ComboBox/DatePicker disabled)
            Action<DependencyObject> dfs = null;
            dfs = delegate (DependencyObject node)
            {
                if (node == null) return;
                int count = VisualTreeHelper.GetChildrenCount(node);
                for (int i = 0; i < count; i++)
                {
                    var child = VisualTreeHelper.GetChild(node, i);

                    if (child is TextBox tb)
                    {
                        tb.IsReadOnly = true;
                        tb.IsHitTestVisible = false;
                        tb.Background = new SolidColorBrush(Color.FromRgb(248, 248, 248));
                    }
                    else if (child is ComboBox cb)
                    {
                        cb.IsEnabled = false;
                        cb.IsHitTestVisible = false;
                        cb.Opacity = 0.7;
                    }
                    else if (child is DatePicker dp)
                    {
                        dp.IsEnabled = false;
                        dp.IsHitTestVisible = false;
                        dp.Opacity = 0.7;
                    }

                    dfs(child);
                }
            };
            dfs(form);
        }
    }
}
