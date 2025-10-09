using HouseholdMS.Model;
using HouseholdMS.View.UserControls;   // AddServiceRecordControl
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data.SQLite;
using System.Globalization;
using System.Linq;
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

        // ===== Column-based search support (empty set = search all) =====
        private static readonly string[] AllColumnKeys = new[]
        {
            nameof(ServiceRow.ServiceID),
            nameof(ServiceRow.HouseholdID),
            nameof(ServiceRow.HouseholdText),
            nameof(ServiceRow.AllTechnicians),
            nameof(ServiceRow.PrimaryTechName),
            nameof(ServiceRow.Problem),
            nameof(ServiceRow.Action),
            nameof(ServiceRow.InventorySummary),
            nameof(ServiceRow.StartDateText),
            nameof(ServiceRow.FinishDateText),
            nameof(ServiceRow.StatusText)
        };

        private readonly HashSet<string> _selectedColumnKeys = new HashSet<string>(StringComparer.Ordinal);

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
            UpdateColumnFilterButtonContent();
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
                            DateTime tmp;
                            if (DateTime.TryParse(Convert.ToString(finishObj), CultureInfo.InvariantCulture, DateTimeStyles.None, out tmp))
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

        // ===== Search with placeholder (respects selected columns) =====
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

                var keys = _selectedColumnKeys.Count == 0 ? AllColumnKeys : _selectedColumnKeys.ToArray();
                for (int i = 0; i < keys.Length; i++)
                {
                    string cell = GetCellString(s, keys[i]);
                    if (!string.IsNullOrEmpty(cell) && cell.ToLowerInvariant().Contains(search))
                        return true;
                }
                return false;
            };
            _view.Refresh();
        }

        private static string GetCellString(ServiceRow s, string key)
        {
            switch (key)
            {
                case nameof(ServiceRow.ServiceID): return s.ServiceID.ToString();
                case nameof(ServiceRow.HouseholdID): return s.HouseholdID.ToString();
                case nameof(ServiceRow.HouseholdText): return s.HouseholdText ?? string.Empty;
                case nameof(ServiceRow.AllTechnicians): return s.AllTechnicians ?? string.Empty;
                case nameof(ServiceRow.PrimaryTechName): return s.PrimaryTechName ?? string.Empty;
                case nameof(ServiceRow.Problem): return s.Problem ?? string.Empty;
                case nameof(ServiceRow.Action): return s.Action ?? string.Empty;
                case nameof(ServiceRow.InventorySummary): return s.InventorySummary ?? string.Empty;
                case nameof(ServiceRow.StartDateText): return s.StartDateText ?? string.Empty;
                case nameof(ServiceRow.FinishDateText): return s.FinishDateText ?? string.Empty;
                case nameof(ServiceRow.StatusText): return s.StatusText ?? string.Empty;
                default: return string.Empty;
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

            var dialog = CreateDialog(scroller, "Service #" + selected.ServiceID + " — Details", 860, 620);
            control.OnCancelRequested += delegate { dialog.Close(); };
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

        // ===== Column chooser UI handlers =====
        private void ColumnFilterButton_Click(object sender, RoutedEventArgs e)
        {
            ColumnPopup.IsOpen = true;
        }

        private void ColumnPopup_Closed(object sender, EventArgs e)
        {
            UpdateColumnFilterButtonContent();

            string placeholder = SearchBox != null ? (SearchBox.Tag as string ?? string.Empty) : string.Empty;
            string text = SearchBox != null ? (SearchBox.Text ?? string.Empty) : string.Empty;

            if (!string.IsNullOrWhiteSpace(text) && text != placeholder)
                SearchBox_TextChanged(SearchBox, null);
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
            _selectedColumnKeys.Clear(); // empty => "All columns"
            foreach (var child in FindPopupCheckBoxes()) child.IsChecked = false;
        }

        private void OkColumns_Click(object sender, RoutedEventArgs e)
        {
            // reflect current selection count on the chip
            UpdateColumnFilterButtonContent();

            // if search box has user text (not placeholder), apply the filter now
            var tagText = SearchBox?.Tag as string ?? string.Empty;
            var text = SearchBox?.Text ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(text) && !string.Equals(text, tagText, StringComparison.Ordinal))
            {
                // use current search logic that respects selected columns
                SearchBox_TextChanged(SearchBox, null);
            }

            // close the popup
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

        public string HouseholdText { get { return "#" + HouseholdID + " — " + (OwnerName ?? "") + " (" + (UserName ?? "") + ")"; } }
        public string StartDateText { get { return StartDate == DateTime.MinValue ? "" : StartDate.ToString("yyyy-MM-dd HH:mm"); } }
        public string FinishDateText { get { return FinishDate.HasValue ? FinishDate.Value.ToString("yyyy-MM-dd HH:mm") : ""; } }

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

        public bool IsOpen { get { return string.Equals(StatusText, "Open", StringComparison.OrdinalIgnoreCase); } }

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
