using System;
using System.Data.SQLite;
using System.Windows;
using System.Windows.Controls;
using HouseholdMS.Model;
using HouseholdMS.Resources; // <-- Strings.*

namespace HouseholdMS.View.Dashboard
{
    public partial class AddServiceControl : UserControl
    {
        private readonly string _userRole;    // "Admin" | "Technician" | "User"
        private readonly int _householdId;

        // Track if an open service already exists for this household
        private bool _hasOpenService;
        public int? LastOpenServiceId { get; private set; }

        public event EventHandler ServiceCreated; // parent refreshes list after this
        public event EventHandler CancelRequested;

        public AddServiceControl(Household household, string userRole)
        {
            InitializeComponent();

            if (household == null) throw new ArgumentNullException(nameof(household));
            _householdId = household.HouseholdID;
            _userRole = string.IsNullOrWhiteSpace(userRole) ? "User" : userRole.Trim();

            // Fill read-only texts
            HHIdText.Text = household.HouseholdID.ToString();
            OwnerText.Text = household.OwnerName ?? string.Empty;
            UserText.Text = household.DNI ?? string.Empty;           // DB uses DNI now
            MunicipalityText.Text = household.Municipality ?? string.Empty;
            DistrictText.Text = household.District ?? string.Empty;
            ContactText.Text = household.ContactNum ?? string.Empty;
            StatusText.Text = ToUiStatus(household.Statuss);            // localized

            // Permissions: only Admin/Technician can confirm
            bool canProceed =
                string.Equals(_userRole, "Admin", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(_userRole, "Technician", StringComparison.OrdinalIgnoreCase);

            ConfirmBtn.IsEnabled = canProceed;
            if (!canProceed)
                ConfirmBtn.ToolTip = L("SCDC_Permission_Tip", "Only Admin or Technician can perform this action.");

            // Check if there is already an open service ticket
            RefreshOpenServiceState();
            UpdateConfirmButtonUi();
        }

        // Localized string getter with safe fallback (compiles even if the key isn't in .resx yet)
        private static string L(string key, string fallback)
        {
            var s = Strings.ResourceManager.GetString(key, Strings.Culture);
            return string.IsNullOrEmpty(s) ? fallback : s;
        }

        private static string Lf(string key, string fallback, params object[] args)
        {
            string fmt = L(key, fallback);
            try { return string.Format(fmt, args); } catch { return fmt; }
        }

        private string ToUiStatus(string dbStatus)
        {
            var s = (dbStatus ?? string.Empty).Trim();

            // DB values are English; map to localized UI strings
            if (s.Equals("In Service", StringComparison.OrdinalIgnoreCase))
                return Strings.Common_Status_OutOfService ?? "Out of Service";

            if (s.Equals("Operational", StringComparison.OrdinalIgnoreCase))
                return Strings.Common_Status_Operational ?? "Operational";

            if (s.Equals("Not Operational", StringComparison.OrdinalIgnoreCase))
                return Strings.Common_Status_OutOfService ?? "Out of Service";

            // Fallback to raw if unknown
            return s;
        }

        private void RefreshOpenServiceState()
        {
            _hasOpenService = false;
            LastOpenServiceId = null;

            try
            {
                using (var conn = DatabaseHelper.GetConnection())
                {
                    conn.Open();
                    using (var cmd = new SQLiteCommand(
                        "SELECT ServiceID FROM Service WHERE HouseholdID=@id AND FinishDate IS NULL LIMIT 1;", conn))
                    {
                        cmd.Parameters.AddWithValue("@id", _householdId);
                        var val = cmd.ExecuteScalar();
                        if (val != null && val != DBNull.Value)
                        {
                            _hasOpenService = true;
                            LastOpenServiceId = Convert.ToInt32(val);
                        }
                    }
                }
            }
            catch
            {
                // Non-fatal; allow user to proceed
            }
        }

        private void UpdateConfirmButtonUi()
        {
            if (_hasOpenService)
            {
                ConfirmBtn.Content = L("AS_Btn_UpdateOpen", "🛠 Update Problem on Open Service");
                SetInfoText(
                    LastOpenServiceId.HasValue
                        ? Lf("AS_Info_HasOpenFmt", "An open service (#{0}) already exists for this household.", LastOpenServiceId.Value)
                        : L("AS_Info_HasOpen", "An open service already exists for this household.")
                );
            }
            else
            {
                ConfirmBtn.Content = L("AS_Btn_StartService", "🚀 Start Service (Move to Out of Service)");
                SetInfoText(L("AS_Info_StartNew", "This will move the household to Out of Service and open a service ticket."));
            }
        }

        private void SetInfoText(string text)
        {
            var tb = FindName("ConfirmInfoText") as TextBlock;
            if (tb != null) tb.Text = text ?? string.Empty;
        }

        private void CancelBtn_Click(object sender, RoutedEventArgs e)
        {
            CancelRequested?.Invoke(this, EventArgs.Empty);
        }

        private void ConfirmBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!ConfirmBtn.IsEnabled) return;

            string problem = (ProblemBox.Text ?? string.Empty).Trim();

            try
            {
                using (var conn = DatabaseHelper.GetConnection())
                {
                    conn.Open();

                    if (_hasOpenService && LastOpenServiceId.HasValue)
                    {
                        // Only update the problem text on the existing open service
                        using (var tx = conn.BeginTransaction())
                        using (var cmd = new SQLiteCommand(
                            "UPDATE Service SET Problem = COALESCE(NULLIF(@p,''), Problem) WHERE ServiceID=@sid;", conn, tx))
                        {
                            cmd.Parameters.AddWithValue("@p", problem);
                            cmd.Parameters.AddWithValue("@sid", LastOpenServiceId.Value);
                            cmd.ExecuteNonQuery();
                            tx.Commit();
                        }

                        MessageBox.Show(
                            Lf("AS_Update_SuccessFmt", "Updated open service #{0}.", LastOpenServiceId.Value),
                            L("AS_Title_Updated", "Service Updated"),
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        using (var tx = conn.BeginTransaction())
                        {
                            // 1) Move household to "In Service" (DB). DB trigger may auto-create a Service row.
                            using (var cmd1 = new SQLiteCommand(
                                "UPDATE Households SET Statuss='In Service' WHERE HouseholdID=@id;", conn, tx))
                            {
                                cmd1.Parameters.AddWithValue("@id", _householdId);
                                cmd1.ExecuteNonQuery();
                            }

                            // 2) Create/open service row (guarded by UNIQUE open-per-household index)
                            using (var cmd2 = new SQLiteCommand(
                                "INSERT OR IGNORE INTO Service (HouseholdID, Problem) VALUES (@id, @p);", conn, tx))
                            {
                                cmd2.Parameters.AddWithValue("@id", _householdId);
                                cmd2.Parameters.AddWithValue("@p", problem);
                                cmd2.ExecuteNonQuery();
                            }

                            // 3) Ensure problem text saved on the open service
                            using (var cmd3 = new SQLiteCommand(
                                "UPDATE Service SET Problem = COALESCE(NULLIF(@p,''), Problem) " +
                                "WHERE HouseholdID=@id AND FinishDate IS NULL;", conn, tx))
                            {
                                cmd3.Parameters.AddWithValue("@id", _householdId);
                                cmd3.Parameters.AddWithValue("@p", problem);
                                cmd3.ExecuteNonQuery();
                            }

                            tx.Commit();
                        }

                        // Retrieve the open ServiceID after commit
                        using (var cmd4 = new SQLiteCommand(
                            "SELECT ServiceID FROM Service WHERE HouseholdID=@id AND FinishDate IS NULL LIMIT 1;", conn))
                        {
                            cmd4.Parameters.AddWithValue("@id", _householdId);
                            var val = cmd4.ExecuteScalar();
                            LastOpenServiceId = (val == null || val == DBNull.Value) ? (int?)null : Convert.ToInt32(val);
                        }

                        MessageBox.Show(
                            L("AS_Create_Success", "Service call started. Household moved to 'Out of Service'."),
                            L("AS_Title_Created", "Service Created"),
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }

                // Let parent refresh lists / tiles
                var handler = ServiceCreated;
                if (handler != null) handler(this, EventArgs.Empty);

                // Refresh local state (so subsequent clicks behave correctly)
                RefreshOpenServiceState();
                UpdateConfirmButtonUi();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    (Strings.Error_UnexpectedPrefix ?? "An unexpected error occurred:") + "\n" + ex.Message,
                    Strings.Error_Title ?? "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
