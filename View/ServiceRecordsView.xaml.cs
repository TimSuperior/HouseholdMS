using HouseholdMS.Model;
using HouseholdMS.View.UserControls;   // <-- add this
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
            { "Household", "OwnerName" },
            { "Technicians", "AllTechnicians"},
            { "Problem", "Problem"},
            { "Action", "Action"},
            { "Inv. Used", "InventorySummary"},
            { "Start", "StartDate"},
            { "Finish", "FinishDate"},
            { "Status", "StatusRank"}
        };

        public ServiceRecordsView(string userRole = "User")
        {
            _currentUserRole = string.IsNullOrWhiteSpace(userRole) ? "User" : userRole.Trim();

            InitializeComponent();
            InitializeAndLoad();
        }

        private void InitializeAndLoad()
        {
            LoadServiceRecords();
            ServiceListView.AddHandler(GridViewColumnHeader.ClickEvent, new RoutedEventHandler(GridViewColumnHeader_Click));
            UpdateSearchPlaceholder();
        }

        public void LoadServiceRecords()
        {
            _all.Clear();

            using (var conn = DatabaseHelper.GetConnection())
            {
                conn.Open();

                string sql = @"
SELECT
  s.ServiceID,
  s.HouseholdID,
  h.OwnerName,
  h.UserName,
  s.TechnicianID,
  COALESCE(vt.Name,'') AS PrimaryTechName,
  COALESCE(s.Problem,'') AS Problem,
  COALESCE(s.Action,'')  AS Action,
  COALESCE(s.StartDate,'') AS StartDate,
  s.FinishDate AS FinishDate,
  COALESCE(s.Status,'') AS Status,

  (
    SELECT group_concat(vt2.Name, ', ')
    FROM ServiceTechnicians st
    JOIN v_Technicians vt2 ON vt2.TechnicianID = st.TechnicianID
    WHERE st.ServiceID = s.ServiceID
  ) AS AllTechnicians,

  (
    SELECT group_concat((si.QuantityUsed || '× ' || inv.ItemType), ', ')
    FROM ServiceInventory si
    JOIN StockInventory inv ON inv.ItemID = si.ItemID
    WHERE si.ServiceID = s.ServiceID
  ) AS InventorySummary

FROM Service s
JOIN Households h ON h.HouseholdID = s.HouseholdID
LEFT JOIN v_Technicians vt ON vt.TechnicianID = s.TechnicianID
ORDER BY datetime(COALESCE(s.FinishDate, s.StartDate)) DESC, s.ServiceID DESC;";

                using (var cmd = new SQLiteCommand(sql, conn))
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        var startStr = Convert.ToString(r["StartDate"] ?? "");
                        var finishObj = r["FinishDate"];
                        DateTime startDt;
                        DateTime? finishDt = null;

                        if (!DateTime.TryParse(startStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out startDt))
                            startDt = DateTime.MinValue;

                        if (finishObj != DBNull.Value)
                        {
                            if (DateTime.TryParse(Convert.ToString(finishObj), CultureInfo.InvariantCulture, DateTimeStyles.None, out var tmp))
                                finishDt = tmp;
                        }

                        var statusRaw = Convert.ToString(r["Status"] ?? "").Trim();
                        if (string.IsNullOrEmpty(statusRaw))
                            statusRaw = finishDt.HasValue ? "Finished" : "Open";

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
                            Status = statusRaw
                        };

                        _all.Add(row);
                    }
                }
            }

            _view = CollectionViewSource.GetDefaultView(_all);
            ServiceListView.ItemsSource = _view;
        }

        // ===== Search with placeholder =====
        private void UpdateSearchPlaceholder()
        {
            if (SearchBox == null) return;

            string ph = "Search by household/tech/problem/action/status";
            SearchBox.Tag = ph;

            if (string.IsNullOrWhiteSpace(SearchBox.Text) || SearchBox.Text == ph)
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
            _view.Filter = obj =>
            {
                var s = obj as ServiceRow;
                if (s == null) return false;

                if ((s.HouseholdText ?? "").ToLowerInvariant().Contains(search)) return true;
                if ((s.AllTechnicians ?? "").ToLowerInvariant().Contains(search)) return true;
                if ((s.Problem ?? "").ToLowerInvariant().Contains(search)) return true;
                if ((s.Action ?? "").ToLowerInvariant().Contains(search)) return true;
                if ((s.InventorySummary ?? "").ToLowerInvariant().Contains(search)) return true;
                if ((s.StatusText ?? "").ToLowerInvariant().Contains(search)) return true;
                if (s.ServiceID.ToString().Contains(search)) return true;
                if (s.HouseholdID.ToString().Contains(search)) return true;

                return false;
            };
            _view.Refresh();
        }

        private void ClearText(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox box)
            {
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
        }

        private void ResetText(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox box && string.IsNullOrWhiteSpace(box.Text))
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

            var direction =
                (_lastHeaderClicked == header && _lastDirection == ListSortDirection.Ascending)
                ? ListSortDirection.Descending
                : ListSortDirection.Ascending;

            _lastHeaderClicked = header;
            _lastDirection = direction;

            _view.SortDescriptions.Clear();
            _view.SortDescriptions.Add(new SortDescription(sortBy, direction));
            _view.Refresh();
        }

        // ===== Double-click → open AddServiceRecordControl (details mode) =====
        private void ServiceListView_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var selected = ServiceListView.SelectedItem as ServiceRow;
            if (selected == null) return;

            var control = new AddServiceRecordControl(selected);
            var scroller = new ScrollViewer
            {
                Content = control,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            var dialog = CreateDialog(scroller, $"Service #{selected.ServiceID} — Details", 860, 620);
            control.OnCancelRequested += (_, __) => dialog.Close();
            dialog.ShowDialog();
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

    // ===== Row model =====
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

        public string Status { get; set; }

        public string HouseholdText => $"#{HouseholdID} — {OwnerName} ({UserName})";
        public string StartDateText => StartDate == DateTime.MinValue ? "" : StartDate.ToString("yyyy-MM-dd HH:mm");
        public string FinishDateText => FinishDate.HasValue ? FinishDate.Value.ToString("yyyy-MM-dd HH:mm") : "";

        public string StatusText
        {
            get
            {
                var s = (Status ?? "").Trim();
                if (string.IsNullOrEmpty(s))
                    return FinishDate.HasValue ? "Finished" : "Open";
                return char.ToUpper(s[0]) + (s.Length > 1 ? s.Substring(1).ToLowerInvariant() : "");
            }
        }

        public bool IsOpen => string.Equals(StatusText, "Open", StringComparison.OrdinalIgnoreCase);

        public int StatusRank
        {
            get
            {
                var s = StatusText.ToLowerInvariant();
                if (s == "open") return 0;
                if (s == "finished") return 1;
                if (s == "canceled" || s == "cancelled") return 2;
                return 3;
            }
        }
    }
}
