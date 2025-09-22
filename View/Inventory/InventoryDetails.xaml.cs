using System;
using System.Data;
using System.Data.SQLite;
using System.Globalization;
using System.Windows;
using System.Windows.Input;   // for MouseButtonEventArgs
using System.Windows.Media;   // Brush, ColorConverter
using HouseholdMS.Model;
using HouseholdMS.View.UserControls; // AddServiceRecordControl

namespace HouseholdMS.View.Inventory
{
    public partial class InventoryDetails : Window
    {
        private readonly int _itemId;
        private int _totalQty;
        private int _usedQty;
        private int _threshold;
        private string _lastRestocked;

        public InventoryDetails(int itemId)
        {
            InitializeComponent();
            _itemId = itemId;
        }

        public InventoryDetails() : this(0) { }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (_itemId <= 0)
            {
                MessageBox.Show("Invalid inventory item ID.", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
                return;
            }

            LoadItemFromDb(_itemId);
            UpdateComputedFieldsAndStatus();
            LoadUsageHistory();    // also sets LastUsedText
            LoadRestockHistory();  // show who restocked
        }

        private void LoadItemFromDb(int id)
        {
            try
            {
                using (var conn = DatabaseHelper.GetConnection())
                {
                    conn.Open();
                    using (var cmd = new SQLiteCommand(
                        "SELECT ItemID, ItemType, TotalQuantity, UsedQuantity, LastRestockedDate, LowStockThreshold, Note " +
                        "FROM StockInventory WHERE ItemID = @id", conn))
                    {
                        cmd.Parameters.AddWithValue("@id", id);
                        using (var r = cmd.ExecuteReader(CommandBehavior.SingleRow))
                        {
                            if (!r.Read())
                            {
                                MessageBox.Show("Inventory item not found.", "Not Found",
                                    MessageBoxButton.OK, MessageBoxImage.Information);
                                Close();
                                return;
                            }

                            int itemId = Convert.ToInt32(r["ItemID"]);
                            string itemType = r["ItemType"] == DBNull.Value ? "" : Convert.ToString(r["ItemType"]);
                            _totalQty = r["TotalQuantity"] == DBNull.Value ? 0 : Convert.ToInt32(r["TotalQuantity"]);
                            _usedQty = r["UsedQuantity"] == DBNull.Value ? 0 : Convert.ToInt32(r["UsedQuantity"]);
                            _threshold = r["LowStockThreshold"] == DBNull.Value ? 0 : Convert.ToInt32(r["LowStockThreshold"]);
                            _lastRestocked = r["LastRestockedDate"] == DBNull.Value ? null : Convert.ToString(r["LastRestockedDate"]);
                            string note = r["Note"] == DBNull.Value ? "" : Convert.ToString(r["Note"]);

                            IdChipText.Text = "ID #" + itemId;
                            FormHeader.Text = "Inventory #" + itemId;

                            ItemTypeBox.Text = itemType;
                            UsedQtyBox.Text = _usedQty.ToString(CultureInfo.InvariantCulture);

                            // If column was empty, try to infer from ItemRestock history
                            if (string.IsNullOrWhiteSpace(_lastRestocked))
                            {
                                _lastRestocked = GetLatestRestockUtcString(conn, id);
                            }

                            LastRestockedText.Text = string.IsNullOrWhiteSpace(_lastRestocked)
                                ? "—"
                                : ToKstString(_lastRestocked);

                            NoteBox.Text = note;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error loading inventory item: " + ex.Message, "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
            }
        }

        private static string GetLatestRestockUtcString(SQLiteConnection openConn, int itemId)
        {
            try
            {
                using (var cmd = new SQLiteCommand(
                    "SELECT MAX(RestockedAt) FROM ItemRestock WHERE ItemID = @id", openConn))
                {
                    cmd.Parameters.AddWithValue("@id", itemId);
                    var val = cmd.ExecuteScalar();
                    return val == null || val == DBNull.Value ? null : Convert.ToString(val);
                }
            }
            catch
            {
                return null;
            }
        }

        private Brush FallbackBrush(string hex)
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            return new SolidColorBrush(c);
        }

        private void UpdateComputedFieldsAndStatus()
        {
            int available = _totalQty;
            if (available < 0) available = 0;
            AvailQtyBox.Text = available.ToString(CultureInfo.InvariantCulture);

            var danger = (Brush)(TryFindResource("DangerBrush") ?? FallbackBrush("#F44336"));
            var warn = (Brush)(TryFindResource("WarningBrush") ?? FallbackBrush("#FF9800"));
            var ok = (Brush)(TryFindResource("SuccessBrush") ?? FallbackBrush("#4CAF50"));

            string status;
            if (available <= 0)
            {
                status = "Out of Stock";
                StatusChip.Background = danger;
                StatusInfoText.Text = "This item is out of stock.";
                StatusInfoBar.Visibility = Visibility.Visible;
            }
            else if (available <= _threshold)
            {
                status = "Low Stock";
                StatusChip.Background = warn;
                StatusInfoText.Text = "This item is low on stock.";
                StatusInfoBar.Visibility = Visibility.Visible;
            }
            else
            {
                status = "In Stock";
                StatusChip.Background = ok;
                StatusInfoBar.Visibility = Visibility.Collapsed;
            }

            StatusChipText.Text = status;
        }

        private static string ToKstString(string dbValue)
        {
            if (string.IsNullOrWhiteSpace(dbValue)) return "";

            if (DateTimeOffset.TryParse(dbValue, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto))
            {
                var utc = dto.ToUniversalTime();
                TimeZoneInfo tz;
                try { tz = TimeZoneInfo.FindSystemTimeZoneById("Korea Standard Time"); }
                catch
                {
                    try { tz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Seoul"); }
                    catch { tz = TimeZoneInfo.Local; }
                }
                var kst = TimeZoneInfo.ConvertTimeFromUtc(utc.UtcDateTime, tz);
                return kst.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            }

            if (DateTime.TryParse(dbValue, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt))
            {
                var utc = dt.Kind == DateTimeKind.Utc ? dt : DateTime.SpecifyKind(dt, DateTimeKind.Utc);
                TimeZoneInfo tz;
                try { tz = TimeZoneInfo.FindSystemTimeZoneById("Korea Standard Time"); }
                catch
                {
                    try { tz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Seoul"); }
                    catch { tz = TimeZoneInfo.Local; }
                }
                var kst = TimeZoneInfo.ConvertTimeFromUtc(utc, tz);
                return kst.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            }

            return dbValue;
        }

        private void LoadUsageHistory()
        {
            try
            {
                using (var conn = DatabaseHelper.GetConnection())
                {
                    conn.Open();

                    using (var cmd = new SQLiteCommand(@"
WITH svc AS (
    SELECT 
        COALESCE(s.FinishDate, s.StartDate) AS UsedAt,
        si.QuantityUsed AS Quantity,
        s.ServiceID,
        s.HouseholdID,
        s.TechnicianID
    FROM ServiceInventory si
    JOIN Service s ON s.ServiceID = si.ServiceID
    WHERE si.ItemID = @id
),
svc_ext AS (
    SELECT 
        svc.UsedAt                              AS UsedAt,
        svc.Quantity                            AS Quantity,
        svc.ServiceID                           AS ServiceID,
        svc.HouseholdID                         AS HouseholdID,
        svc.TechnicianID                        AS TechnicianID,
        COALESCE(st_names.Names, vt.Name)       AS TechnicianNames,
        NULL                                    AS UsedByName
    FROM svc
    LEFT JOIN (
        SELECT st.ServiceID, GROUP_CONCAT(vt2.Name, ', ') AS Names
        FROM ServiceTechnicians st
        JOIN v_Technicians vt2 ON vt2.TechnicianID = st.TechnicianID
        GROUP BY st.ServiceID
    ) AS st_names
        ON st_names.ServiceID = svc.ServiceID
    LEFT JOIN v_Technicians vt
        ON vt.TechnicianID = svc.TechnicianID
),
manual AS (
    SELECT
        u.UsedAt        AS UsedAt,
        u.Quantity      AS Quantity,
        NULL            AS ServiceID,
        NULL            AS HouseholdID,
        NULL            AS TechnicianID,
        NULL            AS TechnicianNames,
        u.UsedByName    AS UsedByName
    FROM ItemUsage u
    WHERE u.ItemID = @id
)
SELECT
    UsedAt, Quantity, ServiceID, HouseholdID, TechnicianID, TechnicianNames, UsedByName
FROM svc_ext
UNION ALL
SELECT
    UsedAt, Quantity, ServiceID, HouseholdID, TechnicianID, TechnicianNames, UsedByName
FROM manual
ORDER BY UsedAt DESC;", conn))
                    {
                        cmd.Parameters.AddWithValue("@id", _itemId);
                        using (var da = new SQLiteDataAdapter(cmd))
                        {
                            var dt = new DataTable();
                            da.Fill(dt);

                            if (!dt.Columns.Contains("ServiceCol"))
                                dt.Columns.Add("ServiceCol", typeof(string));
                            if (!dt.Columns.Contains("UsedAtLocal"))
                                dt.Columns.Add("UsedAtLocal", typeof(string));
                            if (!dt.Columns.Contains("TechnicianCol"))
                                dt.Columns.Add("TechnicianCol", typeof(string));
                            if (!dt.Columns.Contains("UsedByCol"))
                                dt.Columns.Add("UsedByCol", typeof(string));

                            foreach (DataRow row in dt.Rows)
                            {
                                var sid = row["ServiceID"] == DBNull.Value ? "" : Convert.ToString(row["ServiceID"]);
                                row["ServiceCol"] = string.IsNullOrWhiteSpace(sid) ? "" : ("Service #" + sid);

                                var raw = row["UsedAt"] == DBNull.Value ? "" : Convert.ToString(row["UsedAt"]);
                                row["UsedAtLocal"] = ToKstString(raw);

                                var techNames = dt.Columns.Contains("TechnicianNames") && row["TechnicianNames"] != DBNull.Value
                                                ? Convert.ToString(row["TechnicianNames"])
                                                : "";
                                row["TechnicianCol"] = string.IsNullOrWhiteSpace(techNames) ? "—" : techNames;

                                var who = dt.Columns.Contains("UsedByName") && row["UsedByName"] != DBNull.Value
                                          ? Convert.ToString(row["UsedByName"])
                                          : "";
                                row["UsedByCol"] = string.IsNullOrWhiteSpace(who) ? "—" : who;
                            }

                            UsageGrid.ItemsSource = dt.DefaultView;

                            if (dt.Rows.Count > 0)
                                LastUsedText.Text = Convert.ToString(dt.Rows[0]["UsedAtLocal"]) ?? "—";
                            else
                                LastUsedText.Text = "—";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error loading usage history: " + ex.Message, "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadRestockHistory()
        {
            try
            {
                using (var conn = DatabaseHelper.GetConnection())
                {
                    conn.Open();
                    using (var cmd = new SQLiteCommand(@"
SELECT RestockedAt, Quantity, CreatedByName, Note
FROM ItemRestock
WHERE ItemID = @id
ORDER BY datetime(RestockedAt) DESC;", conn))
                    {
                        cmd.Parameters.AddWithValue("@id", _itemId);
                        using (var da = new SQLiteDataAdapter(cmd))
                        {
                            var dt = new DataTable();
                            da.Fill(dt);

                            if (!dt.Columns.Contains("RestockedAtLocal"))
                                dt.Columns.Add("RestockedAtLocal", typeof(string));
                            if (!dt.Columns.Contains("RestockedByCol"))
                                dt.Columns.Add("RestockedByCol", typeof(string));

                            foreach (DataRow row in dt.Rows)
                            {
                                var at = row["RestockedAt"] == DBNull.Value ? "" : Convert.ToString(row["RestockedAt"]);
                                row["RestockedAtLocal"] = ToKstString(at);

                                var who = row["CreatedByName"] == DBNull.Value ? "" : Convert.ToString(row["CreatedByName"]);
                                row["RestockedByCol"] = string.IsNullOrWhiteSpace(who) ? "—" : who;
                            }

                            RestockGrid.ItemsSource = dt.DefaultView;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error loading restock history: " + ex.Message, "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UsageGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var drv = UsageGrid?.SelectedItem as DataRowView;
            if (drv == null) return;

            if (!drv.Row.Table.Columns.Contains("ServiceID")) return;
            var val = drv["ServiceID"];
            if (val == null || val == DBNull.Value) return;

            if (int.TryParse(Convert.ToString(val, CultureInfo.InvariantCulture), out int serviceId) && serviceId > 0)
            {
                OpenServiceDetailsDialog(serviceId);
                e.Handled = true;
            }
        }

        private void OpenServiceDetailsDialog(int serviceId)
        {
            var ctrl = new AddServiceRecordControl(serviceId);

            var detailsWin = new Window
            {
                Title = $"Service #{serviceId}",
                Content = ctrl,
                Owner = this,
                Width = 560,
                Height = 700,
                ResizeMode = System.Windows.ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false
            };

            ctrl.OnCancelRequested += (_, __) => detailsWin.Close();

            detailsWin.ShowDialog();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

        private void Root_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                e.Handled = true;
                Close();
            }
        }
    }
}
