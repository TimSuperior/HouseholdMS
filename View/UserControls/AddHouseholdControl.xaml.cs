using System;
using System.Data.SQLite;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using HouseholdMS;
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

            // Initial chip sync (in case resources/styles load later)
            UpdateStatusChip();
            HideIdChip();
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

            // Map DB status -> UI combobox
            SelectStatus(householdToEdit.Statuss);
            UpdateStatusChip();
        }

        // ========= Optional public initializers if host prefers explicit calls =========
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
        }

        public void InitializeForEdit(int householdId)
        {
            EditingHouseholdID = householdId;
            FormHeader.Text = $"✏ Edit Household #{householdId}";
            SaveButton.Content = "✅ Update";
            DeleteButton.Visibility = Visibility.Visible;

            ShowIdChip(householdId);
            UpdateStatusChip();
        }

        // ========= UI Events (wired from XAML) =========
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

                // Strict duplicate by contact number
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

                // Soft duplicate check by OwnerName
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

            // simple numeric validation for contact (keep existing rule)
            int dummy;
            if (!int.TryParse(ContactBox.Text, out dummy))
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
            // Operational -> green
            // In Service  -> blue (if ever shown)
            // Out of Service -> gray/muted
            Brush bg;

            if (uiStatus.StartsWith("Operational", StringComparison.OrdinalIgnoreCase))
            {
                bg = SafeFindBrush("SuccessBrush", new SolidColorBrush(Color.FromRgb(76, 175, 80)));
            }
            else if (uiStatus.StartsWith("In Service", StringComparison.OrdinalIgnoreCase))
            {
                bg = SafeFindBrush("AccentBrush", new SolidColorBrush(Color.FromRgb(25, 118, 210)));
            }
            else // Out of Service or anything else
            {
                bg = SafeFindBrush("MutedBrush", new SolidColorBrush(Color.FromRgb(158, 158, 158)));
            }

            if (StatusChip != null)
                StatusChip.Background = bg;

            if (StatusChipText != null)
                StatusChipText.Text = string.IsNullOrWhiteSpace(uiStatus) ? "Operational" : uiStatus;

            // Info bar showing only for Out of Service
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

        // ========= Exposed properties for field values =========
        public string OwnerName => OwnerBox.Text.Trim();
        public string UserName => UserNameBox.Text.Trim();
        public string Municipality => MunicipalityBox.Text.Trim();
        public string District => DistrictBox.Text.Trim();
        public string Contact => ContactBox.Text.Trim();
        public string Note => NoteBox.Text.Trim();

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

        public DateTime InstallDate => InstDatePicker.SelectedDate.Value;
        public DateTime LastInspect => LastInspPicker.SelectedDate.Value;
    }
}
