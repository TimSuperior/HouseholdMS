using HouseholdMS.Model;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data.SQLite;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace HouseholdMS.View
{
    public partial class ServiceRecordsView : UserControl
    {
        private readonly ObservableCollection<ServiceRow> _all = new ObservableCollection<ServiceRow>();
        private ICollectionView _view;

        private readonly string _currentUserRole;
        private GridViewColumnHeader _lastHeaderClicked;
        private ListSortDirection _lastDirection = ListSortDirection.Ascending;

        private readonly Dictionary<string, string> _headerToProperty = new Dictionary<string, string>
        {
            { "ID", "ServiceID" },
            { "Household", "OwnerName" },     // sort by owner name
            { "Technicians", "AllTechnicians"},
            { "Problem", "Problem"},
            { "Action", "Action"},
            { "Inv. Used", "InventorySummary"},
            { "Start", "StartDate"},
            { "Finish", "FinishDate"},
            { "Status", "IsOpen"}            // false<true ordering
        };

        public ServiceRecordsView(string userRole = "User")
        {
            _currentUserRole = string.IsNullOrWhiteSpace(userRole) ? "User" : userRole.Trim();

            InitializeComponent();
            InitializeAndLoad();
        }

        private bool IsAdmin()
        {
            return string.Equals(_currentUserRole, "Admin", StringComparison.OrdinalIgnoreCase);
        }

        private void InitializeAndLoad()
        {
            LoadServiceRecords();

            // Header click sorting like your InventoryView
            ServiceListView.AddHandler(GridViewColumnHeader.ClickEvent, new RoutedEventHandler(GridViewColumnHeader_Click));

            // Search placeholder state
            UpdateSearchPlaceholder();
        }

        public void LoadServiceRecords()
        {
            _all.Clear();

            using (var conn = DatabaseHelper.GetConnection())
            {
                conn.Open();

                // Aggregate team technicians and inventory summary via GROUP_CONCAT
                string sql = @"
SELECT
  s.ServiceID,
  s.HouseholdID,
  h.OwnerName,
  h.UserName,
  s.TechnicianID,
  COALESCE(t.Name,'') AS PrimaryTechName,
  COALESCE(s.Problem,'') AS Problem,
  COALESCE(s.Action,'')  AS Action,
  COALESCE(s.StartDate,'') AS StartDate,
  s.FinishDate AS FinishDate,
  CASE WHEN s.FinishDate IS NULL THEN 1 ELSE 0 END AS IsOpenInt,

  -- all technicians attached (may include primary if also in link table)
  (
    SELECT group_concat(tt.Name, ', ')
    FROM ServiceTechnicians st
    JOIN Technicians tt ON tt.TechnicianID = st.TechnicianID
    WHERE st.ServiceID = s.ServiceID
  ) AS AllTechnicians,

  -- inventory summary with item names
  (
    SELECT group_concat((si.QuantityUsed || '× ' || inv.ItemType), ', ')
    FROM ServiceInventory si
    JOIN StockInventory inv ON inv.ItemID = si.ItemID
    WHERE si.ServiceID = s.ServiceID
  ) AS InventorySummary

FROM Service s
JOIN Households h ON h.HouseholdID = s.HouseholdID
LEFT JOIN Technicians t ON t.TechnicianID = s.TechnicianID
ORDER BY s.StartDate DESC;";

                using (var cmd = new SQLiteCommand(sql, conn))
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        var startStr = Convert.ToString(r["StartDate"] ?? "");
                        var finishObj = r["FinishDate"];
                        DateTime startDt;
                        DateTime? finishDt = null;

                        // Parse ISO-ish strings safely
                        if (!DateTime.TryParse(startStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out startDt))
                            startDt = DateTime.MinValue;

                        if (finishObj != DBNull.Value)
                        {
                            DateTime tmp;
                            if (DateTime.TryParse(Convert.ToString(finishObj), CultureInfo.InvariantCulture, DateTimeStyles.None, out tmp))
                                finishDt = tmp;
                        }

                        var row = new ServiceRow
                        {
                            ServiceID = Convert.ToInt32(r["ServiceID"]),
                            HouseholdID = Convert.ToInt32(r["HouseholdID"]),
                            OwnerName = Convert.ToString(r["OwnerName"] ?? ""),
                            UserName = Convert.ToString(r["UserName"] ?? ""),
                            PrimaryTechName = Convert.ToString(r["PrimaryTechName"] ?? ""),
                            AllTechnicians = Convert.ToString(r["AllTechnicians"] ?? ""),
                            Problem = Convert.ToString(r["Problem"] ?? ""),
                            Action = Convert.ToString(r["Action"] ?? ""),
                            InventorySummary = Convert.ToString(r["InventorySummary"] ?? ""),
                            StartDate = startDt,
                            FinishDate = finishDt,
                            IsOpen = Convert.ToInt32(r["IsOpenInt"]) == 1
                        };

                        _all.Add(row);
                    }
                }
            }

            _view = CollectionViewSource.GetDefaultView(_all);
            ServiceListView.ItemsSource = _view;
        }

        // ===== Search with placeholder (same behavior as your InventoryView) =====
        private void UpdateSearchPlaceholder()
        {
            if (SearchBox == null) return;

            string ph = "Search by household/tech/problem/action";
            SearchBox.Tag = ph;

            if (string.IsNullOrWhiteSpace(SearchBox.Text) ||
                SearchBox.Text == ph)
            {
                SearchBox.Text = ph;
                SearchBox.Foreground = Brushes.Gray;
                SearchBox.FontStyle = FontStyles.Italic;
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_view == null) return;

            string text = SearchBox.Text ?? string.Empty;
            if (text == (SearchBox.Tag as string))
            {
                _view.Filter = null;
                _view.Refresh();
                return;
            }

            string search = text.Trim().ToLowerInvariant();
            _view.Filter = delegate (object obj)
            {
                var s = obj as ServiceRow;
                if (s == null) return false;

                if ((s.HouseholdText ?? "").ToLowerInvariant().Contains(search)) return true;
                if ((s.AllTechnicians ?? "").ToLowerInvariant().Contains(search)) return true;
                if ((s.Problem ?? "").ToLowerInvariant().Contains(search)) return true;
                if ((s.Action ?? "").ToLowerInvariant().Contains(search)) return true;
                if ((s.InventorySummary ?? "").ToLowerInvariant().Contains(search)) return true;
                if (s.ServiceID.ToString().Contains(search)) return true;
                if (s.HouseholdID.ToString().Contains(search)) return true;

                return false;
            };
            _view.Refresh();
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

            if (_view != null)
            {
                _view.Filter = null;
                _view.Refresh();
            }
        }

        private void ResetText(object sender, RoutedEventArgs e)
        {
            var box = sender as TextBox;
            if (box != null && string.IsNullOrWhiteSpace(box.Text))
            {
                box.Text = box.Tag as string;
                box.Foreground = Brushes.Gray;
                box.FontStyle = FontStyles.Italic;

                if (_view != null)
                {
                    _view.Filter = null;
                    _view.Refresh();
                }
            }
        }

        // ===== Sorting on header click =====
        private void GridViewColumnHeader_Click(object sender, RoutedEventArgs e)
        {
            var header = e.OriginalSource as GridViewColumnHeader;
            if (header == null) return;

            string headerText = header.Content as string;
            if (string.IsNullOrEmpty(headerText) || !_headerToProperty.ContainsKey(headerText))
                return;

            string sortBy = _headerToProperty[headerText];

            ListSortDirection direction =
                (_lastHeaderClicked == header && _lastDirection == ListSortDirection.Ascending)
                ? ListSortDirection.Descending
                : ListSortDirection.Ascending;

            _lastHeaderClicked = header;
            _lastDirection = direction;

            _view.SortDescriptions.Clear();
            _view.SortDescriptions.Add(new SortDescription(sortBy, direction));
            _view.Refresh();
        }

        // ===== Double-click → read-only details popup =====
        private void ServiceListView_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var selected = ServiceListView.SelectedItem as ServiceRow;
            if (selected == null) return;

            var dt = (DataTemplate)FindResource("ServiceReadOnlyTemplate");
            var content = (FrameworkElement)dt.LoadContent();
            content.DataContext = selected;

            var scroller = new ScrollViewer
            {
                Content = content,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            var dialog = CreateDialog(scroller, "Service #" + selected.ServiceID + " — Details", 860, 620);
            dialog.ShowDialog();
        }

        // ===== Finish service (Admin only; safe-guarded) =====
        private void Finish_Click(object sender, RoutedEventArgs e)
        {
            if (!IsAdmin())
            {
                MessageBox.Show("Only admins can finish a service.", "Access Denied",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var btn = sender as Button;
            if (btn == null || btn.Tag == null) return;

            var row = btn.Tag as ServiceRow;
            if (row == null) return;

            if (!row.IsOpen)
            {
                MessageBox.Show("This service is already closed.", "Info",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var confirm = MessageBox.Show(
                string.Format("Finish Service #{0} now?", row.ServiceID),
                "Confirm Finish", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes) return;

            using (var conn = DatabaseHelper.GetConnection())
            {
                conn.Open();

                using (var cmd = new SQLiteCommand(
                    "UPDATE Service SET FinishDate = @now WHERE ServiceID = @id AND FinishDate IS NULL;", conn))
                {
                    cmd.Parameters.AddWithValue("@now", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    cmd.Parameters.AddWithValue("@id", row.ServiceID);
                    cmd.ExecuteNonQuery();
                }
            }

            LoadServiceRecords();
        }

        private Window CreateDialog(FrameworkElement content, string title, double width, double height)
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
                Width = width,
                Height = height,
                MinWidth = 720,
                MinHeight = 520,
                Background = Brushes.White
            };

            try { if (owner != null) dlg.Icon = owner.Icon; } catch { }
            return dlg;
        }
    }

    // ===== Row model (kept local) =====
    public class ServiceRow
    {
        public int ServiceID { get; set; }

        public int HouseholdID { get; set; }
        public string OwnerName { get; set; }
        public string UserName { get; set; }

        public string PrimaryTechName { get; set; }
        public string AllTechnicians { get; set; }

        public string Problem { get; set; }
        public string Action { get; set; }

        public string InventorySummary { get; set; }

        public DateTime StartDate { get; set; }
        public DateTime? FinishDate { get; set; }

        public bool IsOpen { get; set; }

        public string HouseholdText
        {
            get { return string.Format("#{0} — {1} ({2})", HouseholdID, OwnerName, UserName); }
        }

        public string StartDateText
        {
            get { return StartDate == DateTime.MinValue ? "" : StartDate.ToString("yyyy-MM-dd HH:mm"); }
        }

        public string FinishDateText
        {
            get { return FinishDate.HasValue ? FinishDate.Value.ToString("yyyy-MM-dd HH:mm") : ""; }
        }

        public string StatusText
        {
            get { return IsOpen ? "Open" : "Closed"; }
        }
    }
}
