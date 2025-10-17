using System;
using System.Data.SQLite;
using System.Windows;
using System.Windows.Controls;
using HouseholdMS.Model;

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
            OwnerText.Text = household.OwnerName ?? "";
            UserText.Text = household.DNI ?? "";                  // <-- changed from UserName to DNI
            MunicipalityText.Text = household.Municipality ?? "";
            DistrictText.Text = household.District ?? "";
            ContactText.Text = household.ContactNum ?? "";
            StatusText.Text = ToUiStatus(household.Statuss); // DB "In Service" → UI "Out of Service"

            // Permissions: only Admin/Technician can confirm
            bool canProceed =
                string.Equals(_userRole, "Admin", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(_userRole, "Technician", StringComparison.OrdinalIgnoreCase);

            ConfirmBtn.IsEnabled = canProceed;
            if (!canProceed)
                ConfirmBtn.ToolTip = "Only Admin or Technician can start/update a service call.";

            // Check if there is already an open service ticket
            RefreshOpenServiceState();
            UpdateConfirmButtonUi();
        }

        private string ToUiStatus(string dbStatus)
        {
            var s = (dbStatus ?? "").Trim();
            if (s.Equals("In Service", StringComparison.OrdinalIgnoreCase)) return "Out of Service";
            if (s.Equals("Operational", StringComparison.OrdinalIgnoreCase)) return "Operational";
            if (s.Equals("Not Operational", StringComparison.OrdinalIgnoreCase)) return "Out of Service";
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
                ConfirmBtn.Content = "🛠 Update Problem on Open Service";
                SetInfoText(LastOpenServiceId.HasValue
                    ? $"An open service (#{LastOpenServiceId}) already exists for this household."
                    : "An open service already exists for this household.");
            }
            else
            {
                ConfirmBtn.Content = "🚀 Start Service (Move to Out of Service)";
                SetInfoText("This will move the household to Out of Service and open a service ticket.");
            }
        }

        private void SetInfoText(string text)
        {
            // Optional TextBlock in XAML named ConfirmInfoText
            var tb = FindName("ConfirmInfoText") as TextBlock;
            if (tb != null) tb.Text = text ?? "";
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

                        MessageBox.Show($"Updated open service #{LastOpenServiceId}.",
                            "Service Updated", MessageBoxButton.OK, MessageBoxImage.Information);
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

                        MessageBox.Show("Service call started. Household moved to 'Out of Service'.",
                            "Service Created", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }

                // Let parent refresh lists / tiles
                ServiceCreated?.Invoke(this, EventArgs.Empty);

                // Refresh local state (so subsequent clicks behave correctly)
                RefreshOpenServiceState();
                UpdateConfirmButtonUi();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to start/update service.\n" + ex.Message,
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
