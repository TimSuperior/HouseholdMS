using System;
using System.Collections.ObjectModel;
using System.Data.SQLite;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using HouseholdMS.Model;

namespace HouseholdMS.View.UserControls
{
    public partial class AddHouseholdControl : UserControl
    {
        // ===== Canonical DB values =====
        private const string DB_OPERATIONAL = "Operational";
        private const string DB_IN_SERVICE = "In Service";

        // ===== UI labels =====
        private const string UI_OUT_OF_SERVICE = "Out of Service";

        // ===== Legacy compatibility (treated as In Service / Out of Service in UI) =====
        private const string LEGACY_NOT_OPERATIONAL = "Not Operational";

        private int? EditingHouseholdID = null;

        // Service history rows (bound to DataGrid)
        private readonly ObservableCollection<ServiceShortRow> _serviceRows = new ObservableCollection<ServiceShortRow>();

        // Optional: bubble up when a service row is double-clicked
        public event Action<int> OpenServiceRecordRequested;

        public event EventHandler OnSavedSuccessfully;
        public event EventHandler OnCancelRequested;

        public AddHouseholdControl()
        {
            InitializeComponent();

            // Default "Add" mode visuals
            FormHeader.Text = "➕ Add Household";
            SaveButton.Content = "💾 Save";
            DeleteButton.Visibility = Visibility.Collapsed;

            // Default dates
            InstDatePicker.SelectedDate = DateTime.Today;
            LastInspPicker.SelectedDate = DateTime.Today;

            // Default status UI = Operational
            if (StatusCombo != null)
                StatusCombo.SelectedIndex = 0;

            // Initial chip sync
            UpdateStatusChip();
            HideIdChip();

            // Bind service grid
            if (ServiceHistoryGrid != null)
                ServiceHistoryGrid.ItemsSource = _serviceRows;

            UpdateServiceHistoryVisibility();
        }

        public AddHouseholdControl(Household householdToEdit) : this()
        {
            if (householdToEdit == null) return;

            // Switch to Edit mode
            EditingHouseholdID = householdToEdit.HouseholdID;
            FormHeader.Text = $"✏ Edit Household #{EditingHouseholdID}";
            SaveButton.Content = "✅ Update";
            DeleteButton.Visibility = Visibility.Visible;

            // Set ID chip
            ShowIdChip(EditingHouseholdID.Value);

            // Populate fields
            OwnerBox.Text = householdToEdit.OwnerName;
            UserNameBox.Text = householdToEdit.UserName;
            MunicipalityBox.Text = householdToEdit.Municipality;
            DistrictBox.Text = householdToEdit.District;
            ContactBox.Text = householdToEdit.ContactNum;
            NoteBox.Text = householdToEdit.UserComm;

            InstDatePicker.SelectedDate = householdToEdit.InstallDate;
            LastInspPicker.SelectedDate = householdToEdit.LastInspect;

            // Map DB status -> UI
            SelectStatus(householdToEdit.Statuss);
            UpdateStatusChip();

            // Load service history
            UpdateServiceHistoryVisibility();
            LoadServiceHistory();
        }

        // ========= Optional public initializers =========
        public void InitializeForAdd()
        {
            EditingHouseholdID = null;
            FormHeader.Text = "➕ Add Household";
            SaveButton.Content = "💾 Save";
            DeleteButton.Visibility = Visibility.Collapsed;

            InstDatePicker.SelectedDate = DateTime.Today;
            LastInspPicker.SelectedDate = DateTime.Today;
            StatusCombo.SelectedIndex = 0;

            HideIdChip();
            UpdateStatusChip();

            _serviceRows.Clear();
            UpdateServiceHistoryVisibility();
        }

        public void InitializeForEdit(int householdId)
        {
            EditingHouseholdID = householdId;
            FormHeader.Text = $"✏ Edit Household #{householdId}";
            SaveButton.Content = "✅ Update";
            DeleteButton.Visibility = Visibility.Visible;

            ShowIdChip(householdId);
            UpdateStatusChip();

            UpdateServiceHistoryVisibility();
            LoadServiceHistory();
        }

        // ========= UI Events =========
        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateStatusChip();
        }

        private void StatusCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateStatusChip();
        }

        private void Root_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // ENTER = Save (but allow newline in NoteBox)
            if (e.Key == Key.Enter)
            {
                if (Keyboard.FocusedElement is TextBox tb && tb.Name == "NoteBox" && tb.AcceptsReturn)
                    return;

                SaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                e.Handled = true;
                return;
            }

            // ESC = Cancel
            if (e.Key == Key.Escape)
            {
                Cancel_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
        }

        // ========= Core Save / Cancel / Delete =========
        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateFields()) return;

            using (var conn = DatabaseHelper.GetConnection())
            {
                conn.Open();

                // Strict duplicate by contact number (keep existing behavior)
                using (var contactCheck = new SQLiteCommand(@"
                    SELECT COUNT(*) FROM Households
                    WHERE ContactNum = @Contact
                      AND (@ID IS NULL OR HouseholdID != @ID)", conn))
                {
                    contactCheck.Parameters.AddWithValue("@Contact", Contact);
                    contactCheck.Parameters.AddWithValue("@ID", (object)EditingHouseholdID ?? DBNull.Value);

                    if (Convert.ToInt32(contactCheck.ExecuteScalar()) > 0)
                    {
                        MessageBox.Show("A household with this contact number already exists.",
                                        "Duplicate Contact", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }

                // Soft duplicate check by OwnerName (advisory)
                using (var softCheck = new SQLiteCommand(@"
                    SELECT COUNT(*) FROM Households
                    WHERE OwnerName = @Owner
                      AND (@ID IS NULL OR HouseholdID != @ID)", conn))
                {
                    softCheck.Parameters.AddWithValue("@Owner", OwnerName);
                    softCheck.Parameters.AddWithValue("@ID", (object)EditingHouseholdID ?? DBNull.Value);

                    if (Convert.ToInt32(softCheck.ExecuteScalar()) > 0)
                    {
                        var confirm = MessageBox.Show(
                            "A similar household already exists.\nDo you still want to save?",
                            "Possible Duplicate",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Warning);

                        if (confirm != MessageBoxResult.Yes) return;
                    }
                }

                // Align with DB UNIQUE (OwnerName, UserName, ContactNum) to avoid trigger/constraint aborts
                using (var uqCheck = new SQLiteCommand(@"
                    SELECT COUNT(*) FROM Households
                    WHERE OwnerName = @Owner AND UserName = @UserName AND ContactNum = @Contact
                      AND (@ID IS NULL OR HouseholdID != @ID);", conn))
                {
                    uqCheck.Parameters.AddWithValue("@Owner", OwnerName);
                    uqCheck.Parameters.AddWithValue("@UserName", UserName);
                    uqCheck.Parameters.AddWithValue("@Contact", Contact);
                    uqCheck.Parameters.AddWithValue("@ID", (object)EditingHouseholdID ?? DBNull.Value);
                    if (Convert.ToInt32(uqCheck.ExecuteScalar()) > 0)
                    {
                        MessageBox.Show("That owner, user name and contact combination already exists.",
                                        "Duplicate Household", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }

                string query =
                    (EditingHouseholdID == null)
                    ? @"INSERT INTO Households 
                           (OwnerName, UserName, Municipality, District, ContactNum, InstallDate, LastInspect, UserComm, Statuss)
                        VALUES 
                           (@Owner, @UserName, @Municipality, @District, @Contact, @Inst, @Last, @UserComm, @Status)"
                    : @"UPDATE Households SET 
                           OwnerName   = @Owner,
                           UserName    = @UserName,
                           Municipality= @Municipality,
                           District    = @District,
                           ContactNum  = @Contact,
                           InstallDate = @Inst,
                           LastInspect = @Last,
                           UserComm    = @UserComm,
                           Statuss     = @Status
                        WHERE HouseholdID = @ID";

                using (var cmd = new SQLiteCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@Owner", OwnerName);
                    cmd.Parameters.AddWithValue("@UserName", UserName);
                    cmd.Parameters.AddWithValue("@Municipality", Municipality);
                    cmd.Parameters.AddWithValue("@District", District);
                    cmd.Parameters.AddWithValue("@Contact", Contact);

                    // store dates as yyyy-MM-dd
                    cmd.Parameters.AddWithValue("@Inst", InstDatePicker.SelectedDate.Value.ToString("yyyy-MM-dd"));
                    cmd.Parameters.AddWithValue("@Last", LastInspPicker.SelectedDate.Value.ToString("yyyy-MM-dd"));

                    cmd.Parameters.AddWithValue("@UserComm", string.IsNullOrWhiteSpace(Note) ? (object)DBNull.Value : Note);
                    cmd.Parameters.AddWithValue("@Status", Status); // maps UI -> canonical DB value

                    if (EditingHouseholdID != null)
                        cmd.Parameters.AddWithValue("@ID", EditingHouseholdID);

                    cmd.ExecuteNonQuery();
                }
            }

            MessageBox.Show("Household saved successfully.", "Success",
                            MessageBoxButton.OK, MessageBoxImage.Information);

            // Refresh history if editing existing record
            if (EditingHouseholdID != null)
                LoadServiceHistory();

            OnSavedSuccessfully?.Invoke(this, EventArgs.Empty);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            OnCancelRequested?.Invoke(this, EventArgs.Empty);
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (EditingHouseholdID == null) return;

            var confirm = MessageBox.Show(
                "Are you sure you want to delete this household?",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            using (var conn = DatabaseHelper.GetConnection())
            {
                conn.Open();
                using (var cmd = new SQLiteCommand("DELETE FROM Households WHERE HouseholdID = @id", conn))
                {
                    cmd.Parameters.AddWithValue("@id", EditingHouseholdID);
                    cmd.ExecuteNonQuery();
                }
            }

            MessageBox.Show("Household deleted successfully.", "Deleted",
                            MessageBoxButton.OK, MessageBoxImage.Information);
            OnSavedSuccessfully?.Invoke(this, EventArgs.Empty);
        }

        // ========= Validation =========
        private bool ValidateFields()
        {
            bool hasError = false;
            OwnerBox.Tag = null;
            ContactBox.Tag = null;

            if (string.IsNullOrWhiteSpace(OwnerName))
            {
                OwnerBox.Tag = "error"; hasError = true;
            }
            if (string.IsNullOrWhiteSpace(Contact))
            {
                ContactBox.Tag = "error"; hasError = true;
            }

            // simple numeric validation for contact (retain existing rule)
            if (!int.TryParse(ContactBox.Text, out _))
            {
                MessageBox.Show("Please enter valid contact number!", "Validation Error",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (InstDatePicker.SelectedDate == null || InstDatePicker.SelectedDate > DateTime.Today)
            {
                MessageBox.Show("Installation date cannot be in the future or empty.", "Invalid Date",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (LastInspPicker.SelectedDate == null || LastInspPicker.SelectedDate > DateTime.Today)
            {
                MessageBox.Show("Inspection date cannot be in the future or empty.", "Invalid Date",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (InstDatePicker.SelectedDate > LastInspPicker.SelectedDate)
            {
                MessageBox.Show("Inspection date cannot be earlier than installation date.", "Invalid Date Order",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (hasError)
            {
                MessageBox.Show("Please correct the highlighted fields.", "Validation Error",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            return true;
        }

        // ========= Status mapping & visuals =========
        private void SelectStatus(string statusFromDb)
        {
            var s = (statusFromDb ?? string.Empty).Trim();

            if (s.Equals(DB_OPERATIONAL, StringComparison.OrdinalIgnoreCase))
            {
                StatusCombo.SelectedIndex = 0; // Operational
            }
            else if (s.Equals(DB_IN_SERVICE, StringComparison.OrdinalIgnoreCase) ||
                     s.Equals(UI_OUT_OF_SERVICE, StringComparison.OrdinalIgnoreCase) ||
                     s.Equals(LEGACY_NOT_OPERATIONAL, StringComparison.OrdinalIgnoreCase))
            {
                StatusCombo.SelectedIndex = 1; // Out of Service (UI) -> DB "In Service"
            }
            else
            {
                StatusCombo.SelectedIndex = 0; // fallback
            }
        }

        private void UpdateStatusChip()
        {
            string uiStatus = CurrentUiStatusText();

            // Chip color mapping:
            Brush bg;
            if (uiStatus.StartsWith("Operational", StringComparison.OrdinalIgnoreCase))
            {
                bg = SafeFindBrush("SuccessBrush", new SolidColorBrush(Color.FromRgb(76, 175, 80)));
            }
            else if (uiStatus.StartsWith("In Service", StringComparison.OrdinalIgnoreCase) ||
                     uiStatus.StartsWith(UI_OUT_OF_SERVICE, StringComparison.OrdinalIgnoreCase))
            {
                bg = SafeFindBrush("AccentBrush", new SolidColorBrush(Color.FromRgb(25, 118, 210)));
            }
            else
            {
                bg = SafeFindBrush("MutedBrush", new SolidColorBrush(Color.FromRgb(158, 158, 158)));
            }

            if (StatusChip != null)
                StatusChip.Background = bg;

            if (StatusChipText != null)
                StatusChipText.Text = string.IsNullOrWhiteSpace(uiStatus) ? "Operational" : uiStatus;

            // Info bar only for Out of Service
            if (StatusInfoBar != null)
            {
                StatusInfoBar.Visibility =
                    uiStatus.StartsWith(UI_OUT_OF_SERVICE, StringComparison.OrdinalIgnoreCase)
                    ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private string CurrentUiStatusText()
        {
            if (StatusCombo?.SelectedItem is ComboBoxItem item && item.Content != null)
                return item.Content.ToString().Trim();
            return "Operational";
        }

        private Brush SafeFindBrush(string key, Brush fallback)
        {
            var b = TryFindResource(key) as Brush;
            return b ?? fallback;
        }

        private void ShowIdChip(int id)
        {
            if (IdChip != null)
                IdChip.Visibility = Visibility.Visible;
            if (IdChipText != null)
                IdChipText.Text = $"#{id}";
        }

        private void HideIdChip()
        {
            if (IdChip != null)
                IdChip.Visibility = Visibility.Collapsed;
            if (IdChipText != null)
                IdChipText.Text = string.Empty;
        }

        // ========= Service history logic (uses your Service/* tables; updated to v_Technicians) =========
        private void UpdateServiceHistoryVisibility()
        {
            if (ServiceHistoryPanel == null) return;
            ServiceHistoryPanel.Visibility = (EditingHouseholdID.HasValue ? Visibility.Visible : Visibility.Collapsed);
        }

        private void LoadServiceHistory()
        {
            if (!EditingHouseholdID.HasValue) return;

            _serviceRows.Clear();

            try
            {
                using (var conn = DatabaseHelper.GetConnection())
                {
                    conn.Open();

                    // Use v_Technicians (approved active technicians) instead of legacy Technicians table
                    using (var cmd = new SQLiteCommand(@"
                        SELECT
                            s.ServiceID,
                            COALESCE(s.FinishDate, s.StartDate) AS At,
                            vt.Name AS PrimaryTech,  -- from Service.TechnicianID
                            (
                                SELECT group_concat(vt2.Name, ', ')
                                FROM ServiceTechnicians st2
                                JOIN v_Technicians vt2 ON vt2.TechnicianID = st2.TechnicianID
                                WHERE st2.ServiceID = s.ServiceID
                            ) AS TeamTechs,
                            COALESCE(NULLIF(TRIM(s.Action), ''), s.Problem, '') AS Summary,
                            CASE WHEN s.FinishDate IS NULL THEN 'Open' ELSE 'Closed' END AS State
                        FROM Service s
                        LEFT JOIN v_Technicians vt ON vt.TechnicianID = s.TechnicianID
                        WHERE s.HouseholdID = @hid
                        ORDER BY datetime(COALESCE(s.FinishDate, s.StartDate)) DESC, s.ServiceID DESC;", conn))
                    {
                        cmd.Parameters.AddWithValue("@hid", EditingHouseholdID.Value);

                        using (var rdr = cmd.ExecuteReader())
                        {
                            while (rdr.Read())
                            {
                                var row = new ServiceShortRow
                                {
                                    ServiceID = SafeGetInt(rdr, "ServiceID"),
                                    Date = NormalizeDate(SafeGetString(rdr, "At")),
                                    Status = SafeGetString(rdr, "State")
                                };

                                // Pick technician display: team technicians > primary > empty
                                string team = SafeGetString(rdr, "TeamTechs");
                                string primary = SafeGetString(rdr, "PrimaryTech");
                                row.Technician = !string.IsNullOrWhiteSpace(team)
                                                 ? team
                                                 : (!string.IsNullOrWhiteSpace(primary) ? primary : "");

                                // Summary from Action/Problem; if empty, attach items summary below
                                row.Summary = SafeGetString(rdr, "Summary");

                                // Optional: compact items used summary for this service (e.g., "2x Fuse, 1x Cable")
                                row.Summary = AppendItemsIfEmptyOrAddon(conn, row.ServiceID, row.Summary);

                                _serviceRows.Add(row);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("LoadServiceHistory error: " + ex.Message);
            }

            // Update header count + empty text
            if (ServiceHistoryCountText != null)
                ServiceHistoryCountText.Text = _serviceRows.Count + " records";

            if (ServiceHistoryEmptyText != null)
                ServiceHistoryEmptyText.Visibility = (_serviceRows.Count == 0) ? Visibility.Visible : Visibility.Collapsed;
        }

        private static string AppendItemsIfEmptyOrAddon(SQLiteConnection conn, int serviceId, string currentSummary)
        {
            try
            {
                using (var cmd = new SQLiteCommand(@"
                    SELECT group_concat(si.QuantityUsed || 'x ' || i.ItemType, ', ')
                    FROM ServiceInventory si
                    JOIN StockInventory i ON i.ItemID = si.ItemID
                    WHERE si.ServiceID = @sid;", conn))
                {
                    cmd.Parameters.AddWithValue("@sid", serviceId);
                    var items = cmd.ExecuteScalar() as string;

                    if (!string.IsNullOrWhiteSpace(items))
                    {
                        if (string.IsNullOrWhiteSpace(currentSummary))
                            return items; // summary was empty → use items summary
                        else
                            return currentSummary + " | " + items; // add as addon
                    }
                }
            }
            catch
            {
                // ignore items summary if view/tables not present
            }
            return currentSummary ?? "";
        }

        private static int SafeGetOrdinal(System.Data.IDataRecord r, string name)
        {
            try { return r.GetOrdinal(name); } catch { return -1; }
        }

        private static string SafeGetString(System.Data.IDataRecord r, string name)
        {
            int i = SafeGetOrdinal(r, name);
            if (i < 0 || r.IsDBNull(i)) return string.Empty;
            try { return r.GetString(i); } catch { return Convert.ToString(r.GetValue(i)); }
        }

        private static int SafeGetInt(System.Data.IDataRecord r, string name)
        {
            int i = SafeGetOrdinal(r, name);
            if (i < 0 || r.IsDBNull(i)) return 0;
            try { return r.GetInt32(i); } catch { return Convert.ToInt32(r.GetValue(i)); }
        }

        private static string NormalizeDate(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            // Accept "yyyy-MM-dd", "yyyy-MM-dd HH:mm:ss", etc.
            if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dt))
                return dt.ToString("yyyy-MM-dd");
            return s;
        }

        private void ServiceHistoryGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var row = (sender as DataGrid)?.SelectedItem as ServiceShortRow;
            if (row == null) return;

            if (OpenServiceRecordRequested != null)
                OpenServiceRecordRequested(row.ServiceID);
        }

        // ========= Exposed properties for field values =========
        public string OwnerName { get { return OwnerBox.Text.Trim(); } }
        public string UserName { get { return UserNameBox.Text.Trim(); } }
        public string Municipality { get { return MunicipalityBox.Text.Trim(); } }
        public string District { get { return DistrictBox.Text.Trim(); } }
        public string Contact { get { return ContactBox.Text.Trim(); } }
        public string Note { get { return NoteBox.Text.Trim(); } }

        /// <summary>
        /// Returns the DB canonical value for Status:
        /// - "Operational" (UI Operational)
        /// - "In Service"  (UI Out of Service)
        /// </summary>
        public string Status
        {
            get
            {
                var selectedText = CurrentUiStatusText();
                if (string.Equals(selectedText, UI_OUT_OF_SERVICE, StringComparison.OrdinalIgnoreCase))
                    return DB_IN_SERVICE;
                return DB_OPERATIONAL;
            }
        }

        public DateTime InstallDate { get { return InstDatePicker.SelectedDate.Value; } }
        public DateTime LastInspect { get { return LastInspPicker.SelectedDate.Value; } }

        // Row model for service history grid
        public class ServiceShortRow
        {
            public int ServiceID { get; set; }
            public string Date { get; set; }        // yyyy-MM-dd
            public string Technician { get; set; }  // aggregated names
            public string Summary { get; set; }     // Action/Problem + items
            public string Status { get; set; }      // Open/Closed
        }
    }
}
