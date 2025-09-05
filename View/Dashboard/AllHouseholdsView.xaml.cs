using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using HouseholdMS.Model;
using HouseholdMS.View.UserControls;

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
        private bool IsAdmin => string.Equals(_currentUserRole, "Admin", StringComparison.OrdinalIgnoreCase);

        private string _normalizedStatusFilter = string.Empty;
        private bool _categoryFilterActive = false;
        private string _searchText = string.Empty;

        // === Minimal parent refresh hook (no new files) ===
        private Action _notifyParent;
        public Action NotifyParent           // optional property for reflection-based wiring
        {
            get { return _notifyParent; }
            set { _notifyParent = value; }
        }
        public void SetParentRefreshCallback(Action cb) // optional method for reflection-based wiring
        {
            _notifyParent = cb;
        }
        private void RaiseParentRefresh()
        {
            var cb = _notifyParent;
            if (cb != null)
            {
                try { cb(); } catch { /* ignore */ }
            }
        }

        // === Constructors ===
        public AllHouseholdsView(string userRole)
        {
            _currentUserRole = string.IsNullOrWhiteSpace(userRole) ? "User" : userRole.Trim();
            InitializeAndLoad();
        }

        // New overload so SitesView can pass a callback directly
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
            HouseholdListView.AddHandler(GridViewColumnHeader.ClickEvent, new RoutedEventHandler(GridViewColumnHeader_Click));
            // NOTE: no SelectionChanged handler -> single click only selects

            ApplyAccessRestrictions();
            UpdateSearchPlaceholder();
            ApplyFilter();
        }

        private void ApplyAccessRestrictions()
        {
            var addBtn = FindName("AddHouseholdButton") as Button;
            if (addBtn != null) addBtn.Visibility = IsAdmin ? Visibility.Visible : Visibility.Collapsed;
        }

        public void LoadHouseholds()
        {
            allHouseholds.Clear();

            using (var conn = DatabaseHelper.GetConnection())
            {
                conn.Open();
                using (var cmd = new System.Data.SQLite.SQLiteCommand("SELECT * FROM Households", conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        DateTime installDate = DateTime.TryParse(reader["InstallDate"] == DBNull.Value ? null : reader["InstallDate"].ToString(), out DateTime dt1) ? dt1 : DateTime.MinValue;
                        DateTime lastInspect = DateTime.TryParse(reader["LastInspect"] == DBNull.Value ? null : reader["LastInspect"].ToString(), out DateTime dt2) ? dt2 : DateTime.MinValue;

                        allHouseholds.Add(new Household
                        {
                            HouseholdID = Convert.ToInt32(reader["HouseholdID"]),
                            OwnerName = reader["OwnerName"]?.ToString(),
                            UserName = reader["UserName"]?.ToString(),
                            Municipality = reader["Municipality"]?.ToString(),
                            District = reader["District"]?.ToString(),
                            ContactNum = reader["ContactNum"]?.ToString(),
                            InstallDate = installDate,
                            LastInspect = lastInspect,
                            UserComm = reader["UserComm"] != DBNull.Value ? reader["UserComm"].ToString() : string.Empty,
                            Statuss = reader["Statuss"] != DBNull.Value ? reader["Statuss"].ToString() : string.Empty
                        });
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
            return s.Trim();
        }

        private static string DisplayStatusLabel(string normalized)
        {
            if (string.Equals(normalized, IN_SERVICE, StringComparison.Ordinal)) return "Out of Service";
            return normalized;
        }

        private void UpdateSearchPlaceholder()
        {
            if (SearchBox == null) return;

            string ph = _categoryFilterActive
                ? $"Search within \"{DisplayStatusLabel(_normalizedStatusFilter)}\""
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

            view.Filter = obj =>
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
            if (SearchBox != null && SearchBox.Text == (SearchBox.Tag as string))
            {
                _searchText = string.Empty;
            }
            else
            {
                _searchText = SearchBox?.Text ?? string.Empty;
            }
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
            {
                box.Text = string.Empty;
            }
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
            if (string.IsNullOrEmpty(headerText) || !_headerToProperty.ContainsKey(headerText))
                return;

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

        private void AddHouseholdButton_Click(object sender, RoutedEventArgs e)
        {
            if (!IsAdmin)
            {
                MessageBox.Show("Only admins can add households.", "Access Denied", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var form = new AddHouseholdControl();
            form.Loaded += (_, __) => LockStatusInputsInForm(form);

            Window dialog = CreateWideDialog(form, "Add Household");
            form.OnSavedSuccessfully += delegate
            {
                dialog.DialogResult = true;
                dialog.Close();
                LoadHouseholds();
                ApplyFilter();
                // notify parent tiles
                RaiseParentRefresh();
            };
            form.OnCancelRequested += delegate
            {
                dialog.DialogResult = false;
                dialog.Close();
            };

            dialog.ShowDialog();
        }

        // === Double-click to open ===
        private void HouseholdListView_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var selected = HouseholdListView.SelectedItem as Household;
            if (selected == null)
                return;

            if (IsAdmin)
            {
                var form = new AddHouseholdControl(selected);
                form.Loaded += (_, __) => LockStatusInputsInForm(form);

                Window dialog = CreateWideDialog(form, $"Edit Household #{selected.HouseholdID}");
                form.OnSavedSuccessfully += delegate
                {
                    dialog.DialogResult = true;
                    dialog.Close();
                    LoadHouseholds();
                    ApplyFilter();
                    // notify parent tiles (status/fields may have changed)
                    RaiseParentRefresh();
                };
                form.OnCancelRequested += delegate
                {
                    dialog.DialogResult = false;
                    dialog.Close();
                };

                dialog.ShowDialog();
            }
            else
            {
                var dt = (DataTemplate)FindResource("HouseholdReadOnlyTemplate");
                var content = (FrameworkElement)dt.LoadContent();
                content.DataContext = selected;

                var scroller = new ScrollViewer
                {
                    Content = content,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                };

                var dialog = CreateWideDialog(scroller, $"Household #{selected.HouseholdID} — Details");
                dialog.ShowDialog();
            }
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

        private void LockStatusInputsInForm(UserControl form)
        {
            if (form == null) return;

            void DFS(DependencyObject node)
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
                    DFS(child);
                }
            }

            DFS(form);
        }
    }
}
