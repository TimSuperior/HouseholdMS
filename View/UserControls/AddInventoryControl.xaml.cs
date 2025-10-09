using System;
using System.Data;
using System.Data.SQLite; // Use SQLite!
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using HouseholdMS.Model;
using HouseholdMS.View.UserControls; // for AddServiceRecordControl

namespace HouseholdMS.View.UserControls
{
    public partial class AddInventoryControl : UserControl
    {
        private InventoryItem _editingItem;
        private bool isEditMode = false;

        private int _totalQtyFromDb = 0;
        private int _usedQtyFromDb = 0;
        private int _thresholdCached = 0;
        private string _lastRestockedUtc = null;
        private string _lastUsedUtc = null;

        public event EventHandler OnSavedSuccessfully;
        public event EventHandler OnCancelRequested;

        public AddInventoryControl()
        {
            InitializeComponent();

            // Ensure the host window is large enough when this control is shown
            this.Loaded += (_, __) => EnsureHostWindowSize();

            FormHeader.Text = "➕ Add Inventory Item";
            SaveButton.Content = "➕ Add";

            // Create mode: show Initial Quantity, hide snapshot (snapshot is now placed BEFORE the form in XAML)
            InitialQtyPanel.Visibility = Visibility.Visible;
            SnapshotCard.Visibility = Visibility.Collapsed;
            StatusPill.Visibility = Visibility.Collapsed;

            ThresholdBox.TextChanged += (_, __) => UpdateStatusChip();
        }

        public AddInventoryControl(InventoryItem item) : this()
        {
            _editingItem = item ?? throw new ArgumentNullException(nameof(item));
            isEditMode = true;

            FormHeader.Text = $"✏ Edit Inventory #{item.ItemID}";
            SaveButton.Content = "✏ Save Changes";
            DeleteButton.Visibility = Visibility.Visible;

            ItemTypeBox.Text = item.ItemType;
            ThresholdBox.Text = item.LowStockThreshold.ToString(CultureInfo.InvariantCulture);
            NoteBox.Text = item.Note;

            // Edit mode: hide Initial Quantity (cannot modify), show snapshot + status
            InitialQtyPanel.Visibility = Visibility.Collapsed;
            SnapshotCard.Visibility = Visibility.Visible;
            StatusPill.Visibility = Visibility.Visible;

            Loaded += (_, __) =>
            {
                LoadSnapshot(item.ItemID);
                LoadUsageHistory(item.ItemID);
                LoadRestockHistory(item.ItemID);
                UpdateStatusChip();
            };
        }

        /// <summary>
        /// Make sure the window that hosts this control is big enough so all content is visible.
        /// (No functional logic changes — just sizing.)
        /// </summary>
        private void EnsureHostWindowSize()
        {
            var win = Window.GetWindow(this);
            if (win == null) return;

            // Desired sizes for comfortable viewing
            const double desiredWidth = 1200;
            const double desiredHeight = 860;

            if (double.IsNaN(win.Width) || win.Width < desiredWidth) win.Width = desiredWidth;
            if (double.IsNaN(win.Height) || win.Height < desiredHeight) win.Height = desiredHeight;

            // Respect any existing minimums but keep ours if larger
            win.MinWidth = Math.Max(win.MinWidth, 1120);
            win.MinHeight = Math.Max(win.MinHeight, 820);

            // Re-center on screen after resize if possible
            try
            {
                var wa = SystemParameters.WorkArea;
                win.Left = Math.Max(wa.Left, wa.Left + (wa.Width - win.Width) / 2);
                win.Top = Math.Max(wa.Top, wa.Top + (wa.Height - win.Height) / 2);
            }
            catch { /* best effort */ }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(ThresholdBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int threshold) || threshold < 0)
            {
                MessageBox.Show("Please enter a valid non-negative number for Low Stock Threshold.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string type = ItemTypeBox.Text.Trim();
            string note = NoteBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(type))
            {
                MessageBox.Show("Item Type is required.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            using (var conn = DatabaseHelper.GetConnection())
            {
                conn.Open();

                if (isEditMode)
                {
                    const string upd = @"
UPDATE StockInventory SET
    ItemType = @type,
    LowStockThreshold = @threshold,
    Note = @note
WHERE ItemID = @id";
                    using (var cmd = new SQLiteCommand(upd, conn))
                    {
                        cmd.Parameters.AddWithValue("@type", type);
                        cmd.Parameters.AddWithValue("@threshold", threshold);
                        cmd.Parameters.AddWithValue("@note", string.IsNullOrWhiteSpace(note) ? DBNull.Value : (object)note);
                        cmd.Parameters.AddWithValue("@id", _editingItem.ItemID);
                        cmd.ExecuteNonQuery();
                    }
                }
                else
                {
                    // Create mode: need initial quantity
                    if (!int.TryParse(InitialQtyBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int initialQty) || initialQty < 0)
                    {
                        MessageBox.Show("Please enter a valid non-negative number for Initial Quantity.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    const string ins = @"
INSERT INTO StockInventory (ItemType, TotalQuantity, UsedQuantity, LastRestockedDate, LowStockThreshold, Note)
VALUES (@type, @total, 0, @date, @threshold, @note)";
                    using (var cmd = new SQLiteCommand(ins, conn))
                    {
                        cmd.Parameters.AddWithValue("@type", type);
                        cmd.Parameters.AddWithValue("@total", initialQty);
                        cmd.Parameters.AddWithValue("@threshold", threshold);
                        cmd.Parameters.AddWithValue("@note", string.IsNullOrWhiteSpace(note) ? DBNull.Value : (object)note);
                        cmd.Parameters.AddWithValue("@date", DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                        cmd.ExecuteNonQuery();
                    }
                }
            }

            MessageBox.Show(isEditMode ? "Inventory item updated." : "Inventory item added.",
                            "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            OnSavedSuccessfully?.Invoke(this, EventArgs.Empty);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => OnCancelRequested?.Invoke(this, EventArgs.Empty);

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (!isEditMode || _editingItem == null) return;

            var result = MessageBox.Show($"Are you sure you want to delete item '{_editingItem.ItemType}'?",
                                         "Confirm Deletion", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    using (var conn = DatabaseHelper.GetConnection())
                    {
                        conn.Open();
                        using (var cmd = new SQLiteCommand("DELETE FROM StockInventory WHERE ItemID = @id", conn))
                        {
                            cmd.Parameters.AddWithValue("@id", _editingItem.ItemID);
                            cmd.ExecuteNonQuery();
                        }
                    }

                    MessageBox.Show("Inventory item deleted successfully.", "Deleted", MessageBoxButton.OK, MessageBoxImage.Information);
                    OnSavedSuccessfully?.Invoke(this, EventArgs.Empty);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error deleting item: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        // ===== Snapshot =====
        private void LoadSnapshot(int itemId)
        {
            _totalQtyFromDb = 0;
            _usedQtyFromDb = 0;
            _lastRestockedUtc = null;
            _lastUsedUtc = null;

            try
            {
                using (var conn = DatabaseHelper.GetConnection())
                {
                    conn.Open();

                    using (var cmd = new SQLiteCommand(@"
                        SELECT TotalQuantity, UsedQuantity, LastRestockedDate, LowStockThreshold
                        FROM StockInventory
                        WHERE ItemID = @id;", conn))
                    {
                        cmd.Parameters.AddWithValue("@id", itemId);
                        using (var r = cmd.ExecuteReader(CommandBehavior.SingleRow))
                        {
                            if (r.Read())
                            {
                                _totalQtyFromDb = r["TotalQuantity"] == DBNull.Value ? 0 : Convert.ToInt32(r["TotalQuantity"], CultureInfo.InvariantCulture);
                                _usedQtyFromDb = r["UsedQuantity"] == DBNull.Value ? 0 : Convert.ToInt32(r["UsedQuantity"], CultureInfo.InvariantCulture);
                                _lastRestockedUtc = r["LastRestockedDate"] == DBNull.Value ? null : Convert.ToString(r["LastRestockedDate"], CultureInfo.InvariantCulture);
                                if (int.TryParse(ThresholdBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var th))
                                    _thresholdCached = th;
                                else
                                    _thresholdCached = r["LowStockThreshold"] == DBNull.Value ? 0 : Convert.ToInt32(r["LowStockThreshold"], CultureInfo.InvariantCulture);
                            }
                        }
                    }

                    if (string.IsNullOrWhiteSpace(_lastRestockedUtc))
                    {
                        using (var cmd = new SQLiteCommand(@"SELECT MAX(RestockedAt) FROM ItemRestock WHERE ItemID = @id;", conn))
                        {
                            cmd.Parameters.AddWithValue("@id", itemId);
                            var val = cmd.ExecuteScalar();
                            _lastRestockedUtc = (val == null || val == DBNull.Value) ? null : Convert.ToString(val, CultureInfo.InvariantCulture);
                        }
                    }

                    using (var cmd = new SQLiteCommand(@"SELECT MAX(UsedAt) FROM v_ItemUsageHistory WHERE ItemID = @id;", conn))
                    {
                        cmd.Parameters.AddWithValue("@id", itemId);
                        var val = cmd.ExecuteScalar();
                        _lastUsedUtc = (val == null || val == DBNull.Value) ? null : Convert.ToString(val, CultureInfo.InvariantCulture);
                    }
                }
            }
            catch { /* leave defaults */ }

            var available = _totalQtyFromDb;
            AvailQtyInfoText.Text = available.ToString(CultureInfo.InvariantCulture);
            UsedQtyInfoText.Text = _usedQtyFromDb.ToString(CultureInfo.InvariantCulture);
            LastRestockedInfoText.Text = string.IsNullOrWhiteSpace(_lastRestockedUtc) ? "—" : ToKstString(_lastRestockedUtc);
            LastUsedInfoText.Text = string.IsNullOrWhiteSpace(_lastUsedUtc) ? "—" : ToKstString(_lastUsedUtc);

            UpdateStatusChip();
        }

        private void UpdateStatusChip()
        {
            if (StatusPill == null) return;

            int threshold = 0;
            int.TryParse(ThresholdBox.Text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out threshold);
            var available = _totalQtyFromDb;

            string label;
            Brush bg;
            var fg = Brushes.White;

            if (available <= 0)
            {
                label = "Out of Stock";
                bg = (Brush)(TryFindResource("Col.Danger") ?? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444")));
            }
            else if (available <= threshold)
            {
                label = "Low Stock";
                bg = (Brush)(TryFindResource("Col.Warn") ?? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B")));
            }
            else
            {
                label = "In Stock";
                bg = (Brush)(TryFindResource("Col.Success") ?? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981")));
            }

            StatusPill.Background = bg;
            StatusPillText.Foreground = fg;
            StatusPillText.Text = label;

            if (available <= 0)
            {
                StatusInfoBar.Visibility = Visibility.Visible;
                StatusInfoText.Text = "This item is out of stock.";
            }
            else if (available <= threshold)
            {
                StatusInfoBar.Visibility = Visibility.Visible;
                StatusInfoText.Text = "This item is low on stock.";
            }
            else
            {
                StatusInfoBar.Visibility = Visibility.Collapsed;
                StatusInfoText.Text = "";
            }
        }

        // ===== Usage History (service + manual) =====
        private void LoadUsageHistory(int itemId)
        {
            try
            {
                using (var conn = DatabaseHelper.GetConnection())
                {
                    conn.Open();

                    string sqlWithManual = @"
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
SELECT UsedAt, Quantity, ServiceID, HouseholdID, TechnicianID, TechnicianNames, UsedByName
FROM svc_ext
UNION ALL
SELECT UsedAt, Quantity, ServiceID, HouseholdID, TechnicianID, TechnicianNames, UsedByName
FROM manual
ORDER BY UsedAt DESC;";

                    string serviceOnly = @"
SELECT 
    COALESCE(s.FinishDate, s.StartDate) AS UsedAt,
    si.QuantityUsed AS Quantity,
    s.ServiceID,
    s.HouseholdID,
    s.TechnicianID,
    COALESCE(st_names.Names, vt.Name) AS TechnicianNames,
    NULL AS UsedByName
FROM ServiceInventory si
JOIN Service s ON s.ServiceID = si.ServiceID
LEFT JOIN (
    SELECT st.ServiceID, GROUP_CONCAT(vt2.Name, ', ') AS Names
    FROM ServiceTechnicians st
    JOIN v_Technicians vt2 ON vt2.TechnicianID = st.TechnicianID
    GROUP BY st.ServiceID
) AS st_names
    ON st_names.ServiceID = s.ServiceID
LEFT JOIN v_Technicians vt
    ON vt.TechnicianID = s.TechnicianID
WHERE si.ItemID = @id
ORDER BY UsedAt DESC;";

                    DataTable dt = new DataTable();

                    try
                    {
                        using (var cmd = new SQLiteCommand(sqlWithManual, conn))
                        {
                            cmd.Parameters.AddWithValue("@id", itemId);
                            using (var da = new SQLiteDataAdapter(cmd))
                                da.Fill(dt);
                        }
                    }
                    catch (SQLiteException)
                    {
                        using (var cmd = new SQLiteCommand(serviceOnly, conn))
                        {
                            cmd.Parameters.AddWithValue("@id", itemId);
                            using (var da = new SQLiteDataAdapter(cmd))
                                da.Fill(dt);
                        }
                    }

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
                        var sid = row["ServiceID"] == DBNull.Value ? "" : Convert.ToString(row["ServiceID"], CultureInfo.InvariantCulture);
                        row["ServiceCol"] = string.IsNullOrWhiteSpace(sid) ? "" : ("Service #" + sid);

                        var raw = row["UsedAt"] == DBNull.Value ? "" : Convert.ToString(row["UsedAt"], CultureInfo.InvariantCulture);
                        row["UsedAtLocal"] = ToKstString(raw);

                        var names = row.Table.Columns.Contains("TechnicianNames") && row["TechnicianNames"] != DBNull.Value
                            ? Convert.ToString(row["TechnicianNames"], CultureInfo.InvariantCulture)
                            : "";
                        row["TechnicianCol"] = string.IsNullOrWhiteSpace(names) ? "—" : names;

                        var who = row.Table.Columns.Contains("UsedByName") && row["UsedByName"] != DBNull.Value
                            ? Convert.ToString(row["UsedByName"], CultureInfo.InvariantCulture)
                            : "";
                        row["UsedByCol"] = string.IsNullOrWhiteSpace(who) ? "—" : who;
                    }

                    UsageGrid.ItemsSource = dt.DefaultView;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error loading usage history: " + ex.Message, "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadRestockHistory(int itemId)
        {
            try
            {
                using (var conn = DatabaseHelper.GetConnection())
                {
                    conn.Open();
                    using (var cmd = new SQLiteCommand(@"
SELECT
    RestockedAt,
    Quantity,
    CreatedByName,
    Note
FROM ItemRestock
WHERE ItemID = @id
ORDER BY RestockedAt DESC;", conn))
                    {
                        cmd.Parameters.AddWithValue("@id", itemId);
                        using (var da = new SQLiteDataAdapter(cmd))
                        {
                            var dt = new DataTable();
                            da.Fill(dt);

                            if (!dt.Columns.Contains("RestockedLocal"))
                                dt.Columns.Add("RestockedLocal", typeof(string));

                            foreach (DataRow row in dt.Rows)
                            {
                                var raw = row["RestockedAt"] == DBNull.Value ? "" : Convert.ToString(row["RestockedAt"], CultureInfo.InvariantCulture);
                                row["RestockedLocal"] = ToKstString(raw);
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
            if (UsageGrid?.SelectedItem is System.Data.DataRowView drv)
            {
                if (!drv.Row.Table.Columns.Contains("ServiceID")) return;
                var val = drv["ServiceID"];
                if (val == null || val == DBNull.Value) return;

                if (int.TryParse(Convert.ToString(val, CultureInfo.InvariantCulture), out int serviceId) && serviceId > 0)
                {
                    OpenServiceDetailsDialog(serviceId);
                    e.Handled = true;
                }
            }
        }

        private void OpenServiceDetailsDialog(int serviceId)
        {
            var ctrl = new AddServiceRecordControl(serviceId);
            var ownerWindow = Window.GetWindow(this);

            var dlg = new Window
            {
                Title = $"Service #{serviceId}",
                Content = ctrl,
                Owner = ownerWindow,
                Width = 560,
                Height = 700,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false
            };

            ctrl.OnCancelRequested += (_, __) => dlg.Close();
            dlg.ShowDialog();
        }

        // ===== Numeric-only guards =====
        private void ThresholdBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            var tb = sender as TextBox;
            var proposed = tb.Text.Remove(tb.SelectionStart, tb.SelectionLength)
                                  .Insert(tb.SelectionStart, e.Text);
            e.Handled = !IsAllDigits(proposed);
        }
        private void ThresholdBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Back || e.Key == Key.Delete ||
                e.Key == Key.Left || e.Key == Key.Right ||
                e.Key == Key.Tab || e.Key == Key.Home || e.Key == Key.End)
                return;
            if (e.Key == Key.Space) { e.Handled = true; return; }
            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                if (e.Key == Key.C || e.Key == Key.V || e.Key == Key.X || e.Key == Key.A)
                    return;
            }
        }
        private void ThresholdBox_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            if (!e.SourceDataObject.GetDataPresent(DataFormats.Text))
            {
                e.CancelCommand();
                return;
            }
            var pasteText = e.SourceDataObject.GetData(DataFormats.Text) as string ?? "";
            var tb = sender as TextBox;
            var proposed = tb.Text.Remove(tb.SelectionStart, tb.SelectionLength)
                                  .Insert(tb.SelectionStart, pasteText);
            if (!IsAllDigits(proposed))
                e.CancelCommand();
        }

        private void InitialQtyBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            var tb = sender as TextBox;
            var proposed = tb.Text.Remove(tb.SelectionStart, tb.SelectionLength)
                                  .Insert(tb.SelectionStart, e.Text);
            e.Handled = !IsAllDigits(proposed);
        }
        private void InitialQtyBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Back || e.Key == Key.Delete ||
                e.Key == Key.Left || e.Key == Key.Right ||
                e.Key == Key.Tab || e.Key == Key.Home || e.Key == Key.End)
                return;
            if (e.Key == Key.Space) { e.Handled = true; return; }
            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                if (e.Key == Key.C || e.Key == Key.V || e.Key == Key.X || e.Key == Key.A)
                    return;
            }
        }
        private void InitialQtyBox_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            if (!e.SourceDataObject.GetDataPresent(DataFormats.Text))
            {
                e.CancelCommand();
                return;
            }
            var pasteText = e.SourceDataObject.GetData(DataFormats.Text) as string ?? "";
            var tb = sender as TextBox;
            var proposed = tb.Text.Remove(tb.SelectionStart, tb.SelectionLength)
                                  .Insert(tb.SelectionStart, pasteText);
            if (!IsAllDigits(proposed))
                e.CancelCommand();
        }

        private static bool IsAllDigits(string s)
        {
            if (string.IsNullOrEmpty(s)) return true; // allow empty while typing
            for (int i = 0; i < s.Length; i++)
                if (!char.IsDigit(s[i])) return false;
            return true;
        }

        // ===== Utils =====
        private static string ToKstString(string dbValue)
        {
            if (string.IsNullOrWhiteSpace(dbValue)) return "—";

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
    }
}
