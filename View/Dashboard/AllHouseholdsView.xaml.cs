// FILE: View/Dashboard/AllHouseholdsView.xaml.cs
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
using HouseholdMS.Resources;             // Strings.*

namespace HouseholdMS.View.Dashboard
{
    public partial class AllHouseholdsView : UserControl
    {
        private const string OPERATIONAL = "Operational";
        private const string IN_SERVICE = "In Service";
        private const string NOT_OPERATIONAL = "Not Operational";

        private readonly ObservableCollection<Household> allHouseholds = new ObservableCollection<Household>();
        private ICollectionView view;
        private GridViewColumnHeader _lastHeaderClicked;
        private ListSortDirection _lastDirection = ListSortDirection.Ascending;

        private bool _modalOpen = false;

        // GridView header → Household property map (uses localized captions)
        private Dictionary<string, string> _headerToProperty;

        private readonly string _currentUserRole;
        private bool IsAdmin => string.Equals(_currentUserRole, "Admin", StringComparison.OrdinalIgnoreCase);

        // Filters
        private string _normalizedStatusFilter = string.Empty;
        private bool _categoryFilterActive = false;
        private string _searchText = string.Empty;

        // Optional parent refresh hook
        private Action _notifyParent;
        public Action NotifyParent { get => _notifyParent; set => _notifyParent = value; }
        public void SetParentRefreshCallback(Action cb) => _notifyParent = cb;
        private void RaiseParentRefresh() { var cb = _notifyParent; if (cb != null) { try { cb(); } catch { } } }

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

            BuildHeaderMap();
            LoadHouseholds();

            HouseholdListView.AddHandler(GridViewColumnHeader.ClickEvent, new RoutedEventHandler(GridViewColumnHeader_Click));
            HouseholdListView.MouseDoubleClick += HouseholdListView_MouseDoubleClick;

            ApplyAccessRestrictions();
            UpdateSearchPlaceholder();   // sets Tag only (watermark handles visuals)
            ApplyFilter();
        }

        private void BuildHeaderMap()
        {
            _headerToProperty = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { Strings.AHV_Column_ID,          "HouseholdID" },
                { Strings.AHV_Column_OwnerName,   "OwnerName" },
                { Strings.AHV_Column_UserName,    "UserName" },
                { Strings.AHV_Column_Municipality,"Municipality" },
                { Strings.AHV_Column_District,    "District" },
                { Strings.AHV_Column_Contact,     "ContactNum" },
                { Strings.AHV_Column_Installed,   "InstallDate" },
                { Strings.AHV_Column_LastInspect, "LastInspect" },
                { Strings.AHV_Column_Status,      "Statuss" },
                { Strings.AHV_Column_Comment,     "UserComm" }
            };
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
                        var installRaw = reader["InstallDate"] == DBNull.Value ? null : Convert.ToString(reader["InstallDate"]);
                        var lastRaw = reader["LastInspect"] == DBNull.Value ? null : Convert.ToString(reader["LastInspect"]);

                        var installDate = DateTime.TryParse(installRaw, out var dt1) ? dt1 : DateTime.MinValue;
                        var lastInspect = DateTime.TryParse(lastRaw, out var dt2) ? dt2 : DateTime.MinValue;

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

        // === Status helpers (kept for future use) ===
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

        // === Search & filter ===
        private void UpdateSearchPlaceholder()
        {
            if (SearchBox == null) return;
            // Tag is the localized watermark text; template shows it when Text is empty.
            SearchBox.Tag = Strings.AHV_SearchPlaceholder;
        }

        private void ApplyFilter()
        {
            if (view == null) return;

            string search = string.IsNullOrWhiteSpace(_searchText) ? string.Empty : _searchText.Trim().ToLowerInvariant();
            bool useCategory = _categoryFilterActive && !string.IsNullOrWhiteSpace(_normalizedStatusFilter);

            view.Filter = delegate (object obj)
            {
                var h = obj as Household;
                if (h == null) return false;

                if (useCategory)
                {
                    var hn = NormalizeStatus(h.Statuss);
                    if (hn != _normalizedStatusFilter) return false;
                }

                if (string.IsNullOrEmpty(search)) return true;

                if (!string.IsNullOrEmpty(h.OwnerName) && h.OwnerName.ToLowerInvariant().Contains(search)) return true;
                if (!string.IsNullOrEmpty(h.UserName) && h.UserName.ToLowerInvariant().Contains(search)) return true;
                if (!string.IsNullOrEmpty(h.Municipality) && h.Municipality.ToLowerInvariant().Contains(search)) return true;
                if (!string.IsNullOrEmpty(h.District) && h.District.ToLowerInvariant().Contains(search)) return true;
                if (!string.IsNullOrEmpty(h.ContactNum) && h.ContactNum.ToLowerInvariant().Contains(search)) return true;

                if (int.TryParse(search, out int id) && h.HouseholdID == id) return true;

                return false;
            };

            view.Refresh();
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _searchText = SearchBox?.Text ?? string.Empty;
            ApplyFilter();
        }

        // (Handlers retained for compatibility; watermark makes them no-ops)
        private void ResetText(object sender, RoutedEventArgs e) { /* not used by watermark style */ }
        private void ClearText(object sender, RoutedEventArgs e) { /* not used by watermark style */ }

        // === Sorting ===
        private void GridViewColumnHeader_Click(object sender, RoutedEventArgs e)
        {
            var header = e.OriginalSource as GridViewColumnHeader;
            if (header == null) return;

            string headerText = header.Content?.ToString();
            if (string.IsNullOrEmpty(headerText) || _headerToProperty == null || !_headerToProperty.ContainsKey(headerText)) return;

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

            if (_modalOpen) return;

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

        // === Double-click row: open AddHouseholdControl (admin edit, others read-only) ===
        private void HouseholdListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (_modalOpen) return;

            var selected = HouseholdListView.SelectedItem as Household;
            if (selected == null) return;

            var form = new AddHouseholdControl(selected);
            form.Loaded += delegate
            {
                LockStatusInputsInForm(form);
                if (!IsAdmin) SetFormReadOnly(form);
            };

            var dlg = CreateWideDialog(form, "Edit Household #" + selected.HouseholdID);
            form.OnSavedSuccessfully += delegate
            {
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

        private void ShowDialogSafe(Window dlg)
        {
            if (dlg == null || _modalOpen) return;

            _modalOpen = true;
            HouseholdListView.IsEnabled = false;

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
                    if (child is FrameworkElement fe)
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

        private void SetFormReadOnly(AddHouseholdControl form)
        {
            var saveBtn = form.FindName("SaveButton") as Button;
            if (saveBtn != null) saveBtn.Visibility = Visibility.Collapsed;

            var delBtn = form.FindName("DeleteButton") as Button;
            if (delBtn != null) delBtn.Visibility = Visibility.Collapsed;

            var cancelBtn = form.FindName("CancelButton") as Button;
            if (cancelBtn != null) cancelBtn.Content = "Close";

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
