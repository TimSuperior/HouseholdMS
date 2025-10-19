using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Input;
using System.Linq; // for Linq helpers
using System.Data; // NEW: for GetSchemaTable rows
using System.Windows.Threading; // NEW: for debounce
using HouseholdMS.Model;
using HouseholdMS.View.UserControls;     // AddHouseholdControl
using HouseholdMS.Resources;             // Strings.*

namespace HouseholdMS.View.Dashboard
{
    public partial class AllHouseholdsView : UserControl
    {
        private const string OPERATIONAL = "Operational";
        private const string OUT_OF_SERVICE = "Out of Service";

        private readonly ObservableCollection<Household> allHouseholds = new ObservableCollection<Household>();
        private ICollectionView view;
        private GridViewColumnHeader _lastHeaderClicked;
        private ListSortDirection _lastDirection = ListSortDirection.Ascending;

        private bool _modalOpen = false;

        // GridView header → Household property map (uses header text)
        private Dictionary<string, string> _headerToProperty;

        private readonly string _currentUserRole;

        // ************* ONLY CHANGE: treat Technician like Admin *************
        private static readonly StringComparison OrdIC = StringComparison.OrdinalIgnoreCase;
        private static bool HasAdminOrTechnicianAccess(string role)
        {
            if (string.IsNullOrWhiteSpace(role)) return false;
            role = role.Trim();
            return role.Equals("Admin", OrdIC)
                || role.Equals("Administrator", OrdIC)
                || role.Equals("Technician", OrdIC)
                || role.Equals("Tech", OrdIC);
        }

        private bool IsAdmin => HasAdminOrTechnicianAccess(_currentUserRole);
        // ********************************************************************

        // Filters
        private string _normalizedStatusFilter = string.Empty;
        private bool _categoryFilterActive = false;
        private string _searchText = string.Empty;

        // NEW: debounce to prevent UI churn for large lists
        private DispatcherTimer _searchDebounce;

        // ===== Column chooser state (empty = default behavior) =====
        private static readonly string[] AllColumnKeys = new[]
        {
            nameof(Household.HouseholdID),
            nameof(Household.OwnerName),
            nameof(Household.DNI),
            nameof(Household.Municipality),
            nameof(Household.District),
            nameof(Household.X),
            nameof(Household.Y),
            nameof(Household.ContactNum),
            "InstallDateText",
            "LastInspectText",
            nameof(Household.SP),
            nameof(Household.SMI),
            nameof(Household.SB),
            nameof(Household.Statuss),
            nameof(Household.UserComm)
        };

        // Default set = preserves old behavior when none selected
        private static readonly string[] DefaultColumnKeys = new[]
        {
            nameof(Household.OwnerName),
            nameof(Household.DNI),
            nameof(Household.Municipality),
            nameof(Household.District),
            nameof(Household.ContactNum)
        };

        private readonly HashSet<string> _selectedColumnKeys = new HashSet<string>(StringComparer.Ordinal);

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

            // NEW: initialize debounce timer (does not change results, only timing)
            _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _searchDebounce.Tick += (s, e) => { _searchDebounce.Stop(); ApplyFilter(); };

            ApplyAccessRestrictions();
            UpdateSearchPlaceholder();   // sets Tag only (watermark handles visuals)
            UpdateColumnFilterButtonContent();
            ApplyFilter();
        }

        private void BuildHeaderMap()
        {
            _headerToProperty = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { Strings.AHV_Column_ID,          "HouseholdID" },
                { Strings.AHV_Column_OwnerName,   "OwnerName" },
                { "DNI",                           "DNI" },
                { Strings.AHV_Column_Municipality,"Municipality" },
                { Strings.AHV_Column_District,    "District" },
                { "X",                             "X" },
                { "Y",                             "Y" },
                { Strings.AHV_Column_Contact,     "ContactNum" },
                { Strings.AHV_Column_Installed,   "InstallDate" },
                { Strings.AHV_Column_LastInspect, "LastInspect" },
                { "SP",                            "SP" },
                { "SMI",                           "SMI" },
                { "SB",                            "SB" },
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
                    "SELECT HouseholdID, OwnerName, DNI, Municipality, District, X, Y, ContactNum, InstallDate, LastInspect, UserComm, Statuss, SP, SMI, SB FROM Households;", conn))
                using (var reader = cmd.ExecuteReader())
                {
                    var schemaCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var schema = reader.GetSchemaTable();
                    if (schema != null)
                    {
                        foreach (DataRow r in schema.Rows)
                        {
                            schemaCols.Add(Convert.ToString(r["ColumnName"]));
                        }
                    }

                    double? readDouble(string col)
                    {
                        if (!schemaCols.Contains(col)) return null;
                        var o = reader[col];
                        if (o == DBNull.Value) return null;
                        try { return Convert.ToDouble(o, System.Globalization.CultureInfo.InvariantCulture); }
                        catch
                        {
                            double d;
                            return double.TryParse(Convert.ToString(o), out d) ? d : (double?)null;
                        }
                    }

                    while (reader.Read())
                    {
                        var installRaw = reader["InstallDate"] == DBNull.Value ? null : Convert.ToString(reader["InstallDate"]);
                        var lastRaw = reader["LastInspect"] == DBNull.Value ? null : Convert.ToString(reader["LastInspect"]);

                        var installDate = DateTime.TryParse(installRaw, out var dt1) ? dt1 : DateTime.MinValue;
                        var lastInspect = DateTime.TryParse(lastRaw, out var dt2) ? dt2 : DateTime.MinValue;

                        var statusRaw = reader["Statuss"] == DBNull.Value ? string.Empty : Convert.ToString(reader["Statuss"]);
                        var normalizedStatus = NormalizeStatus(statusRaw);

                        var h = new Household
                        {
                            HouseholdID = Convert.ToInt32(reader["HouseholdID"]),
                            OwnerName = reader["OwnerName"] == DBNull.Value ? string.Empty : Convert.ToString(reader["OwnerName"]),
                            DNI = reader["DNI"] == DBNull.Value ? string.Empty : Convert.ToString(reader["DNI"]),
                            Municipality = reader["Municipality"] == DBNull.Value ? string.Empty : Convert.ToString(reader["Municipality"]),
                            District = reader["District"] == DBNull.Value ? string.Empty : Convert.ToString(reader["District"]),
                            ContactNum = reader["ContactNum"] == DBNull.Value ? string.Empty : Convert.ToString(reader["ContactNum"]),
                            InstallDate = installDate,
                            LastInspect = lastInspect,
                            UserComm = reader["UserComm"] == DBNull.Value ? string.Empty : Convert.ToString(reader["UserComm"]),
                            Statuss = normalizedStatus,
                            X = readDouble("X"),
                            Y = readDouble("Y"),
                            SP = reader["SP"] == DBNull.Value ? null : Convert.ToString(reader["SP"]),
                            SMI = reader["SMI"] == DBNull.Value ? null : Convert.ToString(reader["SMI"]),
                            SB = reader["SB"] == DBNull.Value ? null : Convert.ToString(reader["SB"])
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

            if (t.StartsWith("in service") || t.StartsWith("service")) return OUT_OF_SERVICE;
            if (t.StartsWith("not operational") || t.StartsWith("notoperational")) return OUT_OF_SERVICE;
            if (t.StartsWith("out of service") || t.StartsWith("outofservice")) return OUT_OF_SERVICE;

            return OUT_OF_SERVICE;
        }

        // === Search & filter ===
        private void UpdateSearchPlaceholder()
        {
            if (SearchBox == null) return;
            SearchBox.Tag = Strings.AHV_SearchPlaceholder;
        }

        private void ApplyFilter()
        {
            if (view == null) return;

            string search = string.IsNullOrWhiteSpace(_searchText) ? string.Empty : _searchText.Trim().ToLowerInvariant();
            bool useCategory = _categoryFilterActive && !string.IsNullOrWhiteSpace(_normalizedStatusFilter);

            var keys = _selectedColumnKeys.Count == 0 ? DefaultColumnKeys : _selectedColumnKeys.ToArray();

            using (view.DeferRefresh())
            {
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

                    int idValue;
                    if (int.TryParse(search, out idValue) && h.HouseholdID == idValue) return true;

                    for (int i = 0; i < keys.Length; i++)
                    {
                        string cell = GetCellString(h, keys[i]);
                        if (!string.IsNullOrEmpty(cell) && cell.ToLowerInvariant().Contains(search))
                            return true;
                    }

                    return false;
                };
            }
        }

        private static string GetCellString(Household h, string key)
        {
            switch (key)
            {
                case nameof(Household.HouseholdID): return h.HouseholdID.ToString();
                case nameof(Household.OwnerName): return h.OwnerName ?? string.Empty;
                case nameof(Household.DNI): return h.DNI ?? string.Empty;
                case nameof(Household.Municipality): return h.Municipality ?? string.Empty;
                case nameof(Household.District): return h.District ?? string.Empty;
                case nameof(Household.ContactNum): return h.ContactNum ?? string.Empty;
                case "InstallDateText": return h.InstallDate == DateTime.MinValue ? string.Empty : h.InstallDate.ToString("yyyy-MM-dd");
                case "LastInspectText": return h.LastInspect == DateTime.MinValue ? string.Empty : h.LastInspect.ToString("yyyy-MM-dd");
                case nameof(Household.Statuss): return h.Statuss ?? string.Empty;
                case nameof(Household.UserComm): return h.UserComm ?? string.Empty;
                case nameof(Household.X): return h.X?.ToString() ?? string.Empty;
                case nameof(Household.Y): return h.Y?.ToString() ?? string.Empty;
                case nameof(Household.SP): return h.SP ?? string.Empty;
                case nameof(Household.SMI): return h.SMI ?? string.Empty;
                case nameof(Household.SB): return h.SB ?? string.Empty;
                default: return string.Empty;
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _searchText = SearchBox?.Text ?? string.Empty;

            if (_searchDebounce != null)
            {
                _searchDebounce.Stop();
                _searchDebounce.Start();
            }
            else
            {
                ApplyFilter();
            }
        }

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

        // === Add new household (Admins or Technicians) ===
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

            form.OnSavedSuccessfully += (object s, EventArgs args) =>
            {
                try { dialog.DialogResult = true; } catch { }
                dialog.Close();
                LoadHouseholds();
                ApplyFilter();
                RaiseParentRefresh();
            };
            form.OnCancelRequested += (object s, EventArgs args) =>
            {
                try { dialog.DialogResult = false; } catch { }
                dialog.Close();
            };

            ShowDialogSafe(dialog);
        }

        // === Double-click row: open AddHouseholdControl (admin/tech edit, others read-only) ===
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

            form.OnSavedSuccessfully += (object s, EventArgs args) =>
            {
                try { dlg.DialogResult = true; } catch { }
                dlg.Close();
                LoadHouseholds();
                ApplyFilter();
                RaiseParentRefresh();
            };
            form.OnCancelRequested += (object s, EventArgs args) =>
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
            if (HouseholdListView != null) HouseholdListView.IsEnabled = false;

            try
            {
                Mouse.Capture(null);
                dlg.ShowDialog();
            }
            finally
            {
                if (HouseholdListView != null) HouseholdListView.IsEnabled = true;
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

        // ===== Column chooser UI handlers =====
        private void ColumnFilterButton_Click(object sender, RoutedEventArgs e)
        {
            ColumnPopup.IsOpen = true;
        }

        private void ColumnPopup_Closed(object sender, EventArgs e)
        {
            UpdateColumnFilterButtonContent();

            string text = SearchBox != null ? (SearchBox.Text ?? string.Empty) : string.Empty;
            if (!string.IsNullOrWhiteSpace(text))
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

        private void OkColumns_Click(object sender, RoutedEventArgs e)
        {
            UpdateColumnFilterButtonContent();

            var tagText = SearchBox?.Tag as string ?? string.Empty;
            var text = SearchBox?.Text ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(text) && !string.Equals(text, tagText, StringComparison.Ordinal))
            {
                ApplyFilter();
            }

            ColumnPopup.IsOpen = false;
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
