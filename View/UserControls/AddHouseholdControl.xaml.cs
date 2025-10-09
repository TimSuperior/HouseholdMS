using System;
using System.Collections.ObjectModel;
using System.Data.SQLite;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using HouseholdMS.Model;
using HouseholdMS.Resources; // for Strings

namespace HouseholdMS.View.UserControls
{
    public partial class AddHouseholdControl : UserControl
    {
        private const string DB_OPERATIONAL = "Operational";
        private const string DB_IN_SERVICE = "In Service";
        private const string UI_OUT_OF_SERVICE = "Out of Service";
        private const string LEGACY_NOT_OPERATIONAL = "Not Operational";

        private int? EditingHouseholdID = null;

        private readonly ObservableCollection<ServiceShortRow> _serviceRows = new ObservableCollection<ServiceShortRow>();

        public event EventHandler OnSavedSuccessfully;
        public event EventHandler OnCancelRequested;

        public AddHouseholdControl()
        {
            InitializeComponent();

            FormHeader.Text = "➕ Add Household";
            SaveButton.Content = "💾 Save";
            DeleteButton.Visibility = Visibility.Collapsed;

            InstDatePicker.SelectedDate = DateTime.Today;
            LastInspPicker.SelectedDate = DateTime.Today;

            if (StatusCombo != null)
                StatusCombo.SelectedIndex = 0;

            UpdateStatusChip();
            HideIdChip();

            if (ServiceHistoryGrid != null)
                ServiceHistoryGrid.ItemsSource = _serviceRows;

            UpdateServiceHistoryVisibility();

            DataObject.AddPastingHandler(ContactBox, ContactBox_Pasting);
            ContactBox.PreviewTextInput += ContactBox_PreviewTextInput;
        }

        public AddHouseholdControl(Household householdToEdit) : this()
        {
            if (householdToEdit == null) return;

            EditingHouseholdID = householdToEdit.HouseholdID;
            FormHeader.Text = $"✏ Edit Household #{EditingHouseholdID}";
            SaveButton.Content = "✅ Update";
            DeleteButton.Visibility = Visibility.Visible;

            ShowIdChip(EditingHouseholdID.Value);

            OwnerBox.Text = householdToEdit.OwnerName;
            UserNameBox.Text = householdToEdit.UserName;
            MunicipalityBox.Text = householdToEdit.Municipality;
            DistrictBox.Text = householdToEdit.District;
            ContactBox.Text = householdToEdit.ContactNum;
            NoteBox.Text = householdToEdit.UserComm;

            InstDatePicker.SelectedDate = householdToEdit.InstallDate;
            LastInspPicker.SelectedDate = householdToEdit.LastInspect;

            SelectStatus(householdToEdit.Statuss);
            UpdateStatusChip();

            UpdateServiceHistoryVisibility();
            LoadServiceHistory();
        }

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
            ClearAllErrors();
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
            ClearAllErrors();
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateStatusChip();
        }

        private void StatusCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateStatusChip();
            if (StatusCombo.SelectedItem is ComboBoxItem) ClearError(StatusCombo, StatusErr);
        }

        private void Root_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (Keyboard.FocusedElement is TextBox tb && tb.Name == "NoteBox" && tb.AcceptsReturn)
                    return;

                SaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape)
            {
                Cancel_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
        }

        private void DatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender == InstDatePicker) ClearError(InstDatePicker, InstDateErr);
            if (sender == LastInspPicker) ClearError(LastInspPicker, LastInspErr);

            if (InstDatePicker.SelectedDate != null && LastInspPicker.SelectedDate != null)
            {
                if (InstDatePicker.SelectedDate > LastInspPicker.SelectedDate)
                {
                    MarkError(LastInspPicker, LastInspErr, "Inspection date cannot be earlier than installation date.");
                }
                else
                {
                    ClearError(LastInspPicker, LastInspErr);
                }
            }
        }

        private void OnAnyTextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender == OwnerBox) { ClearError(OwnerBox, OwnerErr); }
            else if (sender == UserNameBox) { ClearError(UserNameBox, UserNameErr); }
            else if (sender == MunicipalityBox) { ClearError(MunicipalityBox, MunicipalityErr); }
            else if (sender == DistrictBox) { ClearError(DistrictBox, DistrictErr); }
            else if (sender == ContactBox) { ClearError(ContactBox, ContactErr); }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!ValidateFields()) return;

                using (var conn = DatabaseHelper.GetConnection())
                {
                    conn.Open();

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
                        var inst = InstDatePicker.SelectedDate.Value;
                        var last = LastInspPicker.SelectedDate.Value;

                        cmd.Parameters.AddWithValue("@Owner", OwnerName);
                        cmd.Parameters.AddWithValue("@UserName", UserName);
                        cmd.Parameters.AddWithValue("@Municipality", Municipality);
                        cmd.Parameters.AddWithValue("@District", District);
                        cmd.Parameters.AddWithValue("@Contact", Contact);
                        cmd.Parameters.AddWithValue("@Inst", inst.ToString("yyyy-MM-dd"));
                        cmd.Parameters.AddWithValue("@Last", last.ToString("yyyy-MM-dd"));
                        cmd.Parameters.AddWithValue("@UserComm", string.IsNullOrWhiteSpace(Note) ? (object)DBNull.Value : Note);
                        cmd.Parameters.AddWithValue("@Status", Status);

                        if (EditingHouseholdID != null)
                            cmd.Parameters.AddWithValue("@ID", EditingHouseholdID);

                        cmd.ExecuteNonQuery();
                    }
                }

                MessageBox.Show("Household saved successfully.", "Success",
                                MessageBoxButton.OK, MessageBoxImage.Information);

                if (EditingHouseholdID != null)
                    LoadServiceHistory();

                try { OnSavedSuccessfully?.Invoke(this, EventArgs.Empty); } catch { /* ignore */ }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error while saving household:\n" + ex.Message,
                                "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            try { OnCancelRequested?.Invoke(this, EventArgs.Empty); } catch { /* ignore */ }
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

            try
            {
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

                try { OnSavedSuccessfully?.Invoke(this, EventArgs.Empty); } catch { /* ignore */ }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error while deleting household:\n" + ex.Message,
                                "Delete Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool ValidateFields()
        {
            ClearAllErrors();

            bool hasError = false;
            string requiredMsg = TryRes("AHC_FieldRequired", "This field is required.");

            if (string.IsNullOrWhiteSpace(OwnerName))
            { MarkError(OwnerBox, OwnerErr, requiredMsg); hasError = true; }

            if (string.IsNullOrWhiteSpace(UserName))
            { MarkError(UserNameBox, UserNameErr, requiredMsg); hasError = true; }

            if (string.IsNullOrWhiteSpace(Municipality))
            { MarkError(MunicipalityBox, MunicipalityErr, requiredMsg); hasError = true; }

            if (string.IsNullOrWhiteSpace(District))
            { MarkError(DistrictBox, DistrictErr, requiredMsg); hasError = true; }

            if (string.IsNullOrWhiteSpace(Contact))
            {
                MarkError(ContactBox, ContactErr, requiredMsg); hasError = true;
            }
            else if (!Regex.IsMatch(ContactBox.Text.Trim(), @"^\+?\d{5,}$"))
            {
                MarkError(ContactBox, ContactErr, "Please enter a valid contact number (digits only, min 5).");
                hasError = true;
            }

            if (InstDatePicker.SelectedDate == null)
            { MarkError(InstDatePicker, InstDateErr, "Installation date is required."); hasError = true; }
            else if (InstDatePicker.SelectedDate > DateTime.Today)
            { MarkError(InstDatePicker, InstDateErr, "Installation date cannot be in the future."); hasError = true; }

            if (LastInspPicker.SelectedDate == null)
            { MarkError(LastInspPicker, LastInspErr, "Inspection date is required."); hasError = true; }
            else if (LastInspPicker.SelectedDate > DateTime.Today)
            { MarkError(LastInspPicker, LastInspErr, "Inspection date cannot be in the future."); hasError = true; }

            if (InstDatePicker.SelectedDate != null && LastInspPicker.SelectedDate != null &&
                InstDatePicker.SelectedDate > LastInspPicker.SelectedDate)
            {
                MarkError(LastInspPicker, LastInspErr, "Inspection date cannot be earlier than installation date.");
                hasError = true;
            }

            if (!(StatusCombo.SelectedItem is ComboBoxItem))
            { MarkError(StatusCombo, StatusErr, requiredMsg); hasError = true; }

            if (hasError)
            {
                MessageBox.Show("Please correct the highlighted fields.", "Validation Error",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            return true;
        }

        private static string TryRes(string key, string fallback)
        {
            try
            {
                var s = Strings.ResourceManager.GetString(key, Strings.Culture);
                return string.IsNullOrWhiteSpace(s) ? fallback : s;
            }
            catch { return fallback; }
        }

        private void SelectStatus(string statusFromDb)
        {
            var s = (statusFromDb ?? string.Empty).Trim();

            if (s.Equals(DB_OPERATIONAL, StringComparison.OrdinalIgnoreCase))
            {
                StatusCombo.SelectedIndex = 0;
            }
            else if (s.Equals(DB_IN_SERVICE, StringComparison.OrdinalIgnoreCase) ||
                     s.Equals(UI_OUT_OF_SERVICE, StringComparison.OrdinalIgnoreCase) ||
                     s.Equals(LEGACY_NOT_OPERATIONAL, StringComparison.OrdinalIgnoreCase))
            {
                StatusCombo.SelectedIndex = 1;
            }
            else
            {
                StatusCombo.SelectedIndex = 0;
            }
        }

        private void UpdateStatusChip()
        {
            string uiStatus = CurrentUiStatusText();

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

                    using (var cmd = new SQLiteCommand(@"
                        SELECT
                            s.ServiceID,
                            COALESCE(s.FinishDate, s.StartDate) AS At,
                            vt.Name AS PrimaryTech,
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

                                string team = SafeGetString(rdr, "TeamTechs");
                                string primary = SafeGetString(rdr, "PrimaryTech");
                                row.Technician = !string.IsNullOrWhiteSpace(team)
                                                 ? team
                                                 : (!string.IsNullOrWhiteSpace(primary) ? primary : "");

                                row.Summary = SafeGetString(rdr, "Summary");
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
                            return items;
                        else
                            return currentSummary + " | " + items;
                    }
                }
            }
            catch
            {
                // ignore
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
            if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dt))
                return dt.ToString("yyyy-MM-dd");
            return s;
        }

        private void ServiceHistoryGrid_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var row = (sender as DataGrid)?.SelectedItem as ServiceShortRow;
            if (row == null) return;

            try { OpenServiceRecordRequested?.Invoke(row.ServiceID); } catch { /* ignore */ }
            OpenServiceRecordInDialog(row.ServiceID);
            e.Handled = true;
        }

        private void ServiceHistoryGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var row = (sender as DataGrid)?.SelectedItem as ServiceShortRow;
            if (row == null) return;

            try { OpenServiceRecordRequested?.Invoke(row.ServiceID); } catch { /* ignore */ }
            OpenServiceRecordInDialog(row.ServiceID);
            e.Handled = true;
        }

        private void OpenServiceRecordInDialog(int serviceId)
        {
            var ctrl = new AddServiceRecordControl(serviceId);
            var owner = Window.GetWindow(this);

            var win = new Window
            {
                Title = $"Service #{serviceId}",
                Content = ctrl,
                Owner = owner,
                Width = 560,
                Height = 700,
                ResizeMode = System.Windows.ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false
            };

            ctrl.OnCancelRequested += (_, __) => win.Close();

            win.ShowDialog();
        }

        private void ContactBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (e.Text == "+" && ContactBox.CaretIndex == 0 && !ContactBox.Text.StartsWith("+"))
            {
                e.Handled = false;
                ClearError(ContactBox, ContactErr);
                return;
            }

            if (!Regex.IsMatch(e.Text, @"^\d+$"))
            {
                e.Handled = true;
                return;
            }

            ClearError(ContactBox, ContactErr);
        }

        private void ContactBox_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            if (e.DataObject.GetDataPresent(DataFormats.Text))
            {
                var text = e.DataObject.GetData(DataFormats.Text) as string;
                if (!Regex.IsMatch(text ?? "", @"^\+?\d+$"))
                {
                    e.CancelCommand();
                }
            }
            else
            {
                e.CancelCommand();
            }
        }

        private void MarkError(Control input, TextBlock errorText, string message)
        {
            if (input != null) input.Tag = "error";
            if (errorText != null)
            {
                errorText.Text = message ?? TryRes("AHC_FieldRequired", "This field is required.");
                errorText.Visibility = Visibility.Visible;
            }
        }

        private void ClearError(Control input, TextBlock errorText)
        {
            if (input != null) input.Tag = null;
            if (errorText != null)
            {
                errorText.Text = "";
                errorText.Visibility = Visibility.Collapsed;
            }
        }

        private void ClearAllErrors()
        {
            ClearError(OwnerBox, OwnerErr);
            ClearError(UserNameBox, UserNameErr);
            ClearError(MunicipalityBox, MunicipalityErr);
            ClearError(DistrictBox, DistrictErr);
            ClearError(ContactBox, ContactErr);
            ClearError(InstDatePicker, InstDateErr);
            ClearError(LastInspPicker, LastInspErr);
            ClearError(StatusCombo, StatusErr);
        }

        public string OwnerName => OwnerBox.Text.Trim();
        public string UserName => UserNameBox.Text.Trim();
        public string Municipality => MunicipalityBox.Text.Trim();
        public string District => DistrictBox.Text.Trim();
        public string Contact => ContactBox.Text.Trim();
        public string Note => NoteBox.Text.Trim();

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

        public DateTime InstallDate => InstDatePicker.SelectedDate.Value;
        public DateTime LastInspect => LastInspPicker.SelectedDate.Value;

        public class ServiceShortRow
        {
            public int ServiceID { get; set; }
            public string Date { get; set; }
            public string Technician { get; set; }
            public string Summary { get; set; }
            public string Status { get; set; }
        }

        public event Action<int> OpenServiceRecordRequested;
    }
}
