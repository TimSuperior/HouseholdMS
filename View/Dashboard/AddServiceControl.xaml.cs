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

        public event EventHandler ServiceCreated; // parent refreshes list after this
        public event EventHandler CancelRequested;

        public AddServiceControl(Household household, string userRole)
        {
            InitializeComponent();

            if (household == null) throw new ArgumentNullException("household");
            _householdId = household.HouseholdID;
            _userRole = string.IsNullOrWhiteSpace(userRole) ? "User" : userRole;

            // Fill read-only texts
            HHIdText.Text = household.HouseholdID.ToString();
            OwnerText.Text = household.OwnerName ?? "";
            UserText.Text = household.UserName ?? "";
            MunicipalityText.Text = household.Municipality ?? "";
            DistrictText.Text = household.District ?? "";
            ContactText.Text = household.ContactNum ?? "";
            StatusText.Text = string.IsNullOrWhiteSpace(household.Statuss) ? "Operational" : household.Statuss;

            // Permissions: only Admin/Technician can confirm
            bool canProceed = string.Equals(_userRole, "Admin", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(_userRole, "Technician", StringComparison.OrdinalIgnoreCase);

            ConfirmBtn.IsEnabled = canProceed;

            if (!canProceed)
            {
                ConfirmBtn.ToolTip = "Only Admin or Technician can start a service call.";
            }
        }

        private void CancelBtn_Click(object sender, RoutedEventArgs e)
        {
            if (CancelRequested != null) CancelRequested(this, EventArgs.Empty);
        }

        private void ConfirmBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!ConfirmBtn.IsEnabled) return;

            string problem = (ProblemBox.Text ?? string.Empty).Trim();

            try
            {
                using (SQLiteConnection conn = DatabaseHelper.GetConnection())
                {
                    conn.Open();

                    using (SQLiteTransaction tx = conn.BeginTransaction())
                    {
                        // 1) Move household to "In Service"
                        using (SQLiteCommand cmd1 = new SQLiteCommand(
                            "UPDATE Households SET Statuss='In Service' WHERE HouseholdID=@id;", conn, tx))
                        {
                            cmd1.Parameters.AddWithValue("@id", _householdId);
                            cmd1.ExecuteNonQuery();
                        }

                        // 2) Create/open service row
                        using (SQLiteCommand cmd2 = new SQLiteCommand(
                            "INSERT OR IGNORE INTO Service (HouseholdID, Problem) VALUES (@id, @p);", conn, tx))
                        {
                            cmd2.Parameters.AddWithValue("@id", _householdId);
                            cmd2.Parameters.AddWithValue("@p", problem);
                            cmd2.ExecuteNonQuery();
                        }

                        // 3) Ensure problem text saved on currently open service
                        using (SQLiteCommand cmd3 = new SQLiteCommand(
                            "UPDATE Service SET Problem = COALESCE(NULLIF(@p,''), Problem) " +
                            "WHERE HouseholdID=@id AND FinishDate IS NULL;", conn, tx))
                        {
                            cmd3.Parameters.AddWithValue("@id", _householdId);
                            cmd3.Parameters.AddWithValue("@p", problem);
                            cmd3.ExecuteNonQuery();
                        }

                        tx.Commit();
                    }
                }

                MessageBox.Show("Service call started. Household moved to 'Out of Service'.",
                    "Service", MessageBoxButton.OK, MessageBoxImage.Information);

                if (ServiceCreated != null) ServiceCreated(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to start service.\n" + ex.Message,
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
