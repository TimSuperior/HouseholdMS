using System;
using System.Data.SQLite;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HouseholdMS.Model;

namespace HouseholdMS.View.UserControls
{
    public partial class AddServiceRecordControl : UserControl
    {
        private readonly bool _detailsOnly;
        private readonly object _rowOrNull; // kept for compatibility with your ServiceRow use

        public event EventHandler OnSavedSuccessfully;
        public event EventHandler OnCancelRequested;

        // ===== Form mode (default) =====
        public AddServiceRecordControl()
        {
            InitializeComponent();

            // header text (no resx dependency here to avoid namespace issues)
            FormHeader.Text = "➕ Add Service Record";

            // Form mode visible by default
            DetailsPanel.Visibility = Visibility.Collapsed;
            FormPanel.Visibility = Visibility.Visible;

            SaveButton.Visibility = Visibility.Visible;
            DeleteButton.Visibility = Visibility.Collapsed;
            CancelButton.Content = "Close";
        }

        // ===== Details mode from an existing ServiceRow (kept) =====
        public AddServiceRecordControl(HouseholdMS.View.ServiceRow row) : this()
        {
            if (row == null) throw new ArgumentNullException(nameof(row));
            _rowOrNull = row;
            _detailsOnly = true;

            // Switch to details layout
            FormHeader.Text = $"Service #{row.ServiceID} — Details";
            DetailsPanel.Visibility = Visibility.Visible;
            FormPanel.Visibility = Visibility.Collapsed;

            // Buttons setup
            SaveButton.Visibility = Visibility.Collapsed;
            DeleteButton.Visibility = Visibility.Collapsed;
            CancelButton.Content = "Close";

            // Populate details
            ServiceIdValue.Text = row.ServiceID.ToString();
            HouseholdValue.Text = row.HouseholdText;
            var techs = string.IsNullOrWhiteSpace(row.AllTechnicians) ? row.PrimaryTechName : row.AllTechnicians;
            TechniciansValue.Text = string.IsNullOrWhiteSpace(techs) ? "—" : techs;

            ProblemValue.Text = string.IsNullOrWhiteSpace(row.Problem) ? "—" : row.Problem;
            ActionValue.Text = string.IsNullOrWhiteSpace(row.Action) ? "—" : row.Action;
            InventoryValue.Text = string.IsNullOrWhiteSpace(row.InventorySummary) ? "—" : row.InventorySummary;
            StartedValue.Text = string.IsNullOrWhiteSpace(row.StartDateText) ? "—" : row.StartDateText;
            FinishedValue.Text = string.IsNullOrWhiteSpace(row.FinishDateText) ? "—" : row.FinishDateText;

            ApplyStatusPill(row.StatusText);
        }

        // ===== Details mode by ServiceID =====
        public AddServiceRecordControl(int serviceId) : this()
        {
            _detailsOnly = true;

            // Switch to details layout
            FormHeader.Text = $"Service #{serviceId} — Details";
            DetailsPanel.Visibility = Visibility.Visible;
            FormPanel.Visibility = Visibility.Collapsed;

            // Buttons setup
            SaveButton.Visibility = Visibility.Collapsed;
            DeleteButton.Visibility = Visibility.Collapsed;
            CancelButton.Content = "Close";

            // Load from DB
            LoadAndPopulate(serviceId);
        }

        private void LoadAndPopulate(int serviceId)
        {
            try
            {
                using (var conn = DatabaseHelper.GetConnection())
                {
                    conn.Open();

                    int householdId = 0;
                    string owner = "", contact = "", muni = "", dist = "";
                    string primaryTech = "", problem = "", action = "";
                    string start = "", finish = "";
                    string status = "Open";

                    // Base info
                    using (var cmd = new SQLiteCommand(@"
                        SELECT 
                            s.ServiceID, s.HouseholdID, s.TechnicianID,
                            s.Problem, s.Action, s.StartDate, s.FinishDate,
                            h.OwnerName, h.ContactNum, h.Municipality, h.District,
                            vt.Name AS PrimaryTech
                        FROM Service s
                        LEFT JOIN Households h ON h.HouseholdID = s.HouseholdID
                        LEFT JOIN v_Technicians vt ON vt.TechnicianID = s.TechnicianID
                        WHERE s.ServiceID = @sid;", conn))
                    {
                        cmd.Parameters.AddWithValue("@sid", serviceId);
                        using (var r = cmd.ExecuteReader())
                        {
                            if (r.Read())
                            {
                                householdId = GetInt(r, "HouseholdID");
                                owner = GetString(r, "OwnerName");
                                contact = GetString(r, "ContactNum");
                                muni = GetString(r, "Municipality");
                                dist = GetString(r, "District");

                                primaryTech = GetString(r, "PrimaryTech");
                                problem = GetString(r, "Problem");
                                action = GetString(r, "Action");

                                start = NormalizeDate(GetString(r, "StartDate"));
                                finish = NormalizeDate(GetString(r, "FinishDate"));

                                status = string.IsNullOrWhiteSpace(finish) ? "Open" : "Finished";
                            }
                        }
                    }

                    // All technicians (aggregated)
                    string allTechs = "";
                    using (var cmd = new SQLiteCommand(@"
                        SELECT group_concat(vt.Name, ', ')
                        FROM ServiceTechnicians st
                        JOIN v_Technicians vt ON vt.TechnicianID = st.TechnicianID
                        WHERE st.ServiceID = @sid;", conn))
                    {
                        cmd.Parameters.AddWithValue("@sid", serviceId);
                        allTechs = cmd.ExecuteScalar() as string ?? "";
                    }

                    // Inventory summary
                    string inventory = "";
                    using (var cmd = new SQLiteCommand(@"
                        SELECT group_concat(si.QuantityUsed || 'x ' || i.ItemType, ', ')
                        FROM ServiceInventory si
                        JOIN StockInventory i ON i.ItemID = si.ItemID
                        WHERE si.ServiceID = @sid;", conn))
                    {
                        cmd.Parameters.AddWithValue("@sid", serviceId);
                        inventory = cmd.ExecuteScalar() as string ?? "";
                    }

                    // Fill UI
                    ServiceIdValue.Text = serviceId.ToString();
                    HouseholdValue.Text = BuildHouseholdText(householdId, owner, contact, muni, dist);
                    TechniciansValue.Text = string.IsNullOrWhiteSpace(allTechs)
                        ? (string.IsNullOrWhiteSpace(primaryTech) ? "—" : primaryTech)
                        : allTechs;

                    ProblemValue.Text = string.IsNullOrWhiteSpace(problem) ? "—" : problem;
                    ActionValue.Text = string.IsNullOrWhiteSpace(action) ? "—" : action;
                    InventoryValue.Text = string.IsNullOrWhiteSpace(inventory) ? "—" : inventory;

                    StartedValue.Text = string.IsNullOrWhiteSpace(start) ? "—" : start;
                    FinishedValue.Text = string.IsNullOrWhiteSpace(finish) ? "—" : finish;

                    ApplyStatusPill(status);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to load service details.\n" + ex.Message, "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string BuildHouseholdText(int id, string owner, string contact, string muni, string dist)
        {
            var sb = new StringBuilder();
            sb.Append('#').Append(id);
            if (!string.IsNullOrWhiteSpace(owner))
                sb.Append(" — ").Append(owner);
            if (!string.IsNullOrWhiteSpace(contact))
                sb.Append(" (").Append(contact).Append(')');
            if (!string.IsNullOrWhiteSpace(muni) || !string.IsNullOrWhiteSpace(dist))
                sb.Append(" — ").Append(muni)
                  .Append(string.IsNullOrWhiteSpace(muni) || string.IsNullOrWhiteSpace(dist) ? "" : ", ")
                  .Append(dist);
            return sb.ToString();
        }

        private static string NormalizeDate(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dt))
                return dt.ToString("yyyy-MM-dd");
            return s;
        }

        private static string GetString(System.Data.IDataRecord r, string col)
        {
            int i; try { i = r.GetOrdinal(col); } catch { return ""; }
            if (i < 0 || r.IsDBNull(i)) return "";
            try { return r.GetString(i); } catch { return Convert.ToString(r.GetValue(i)); }
        }

        private static int GetInt(System.Data.IDataRecord r, string col)
        {
            int i; try { i = r.GetOrdinal(col); } catch { return 0; }
            if (i < 0 || r.IsDBNull(i)) return 0;
            try { return r.GetInt32(i); } catch { return Convert.ToInt32(r.GetValue(i)); }
        }

        private void ApplyStatusPill(string status)
        {
            var s = (status ?? "").Trim().ToLowerInvariant();

            Brush bg = (Brush)FindResource("Pill.DefaultBg");
            Brush fg = (Brush)FindResource("Pill.DefaultFg");

            if (s == "open")
            {
                bg = (Brush)FindResource("Pill.OpenBg");
                fg = (Brush)FindResource("Pill.OpenFg");
            }
            else if (s == "finished" || s == "closed")
            {
                bg = (Brush)FindResource("Pill.FinishBg");
                fg = (Brush)FindResource("Pill.FinishFg");
            }
            else if (s == "canceled" || s == "cancelled")
            {
                bg = (Brush)FindResource("Pill.CancelBg");
                fg = (Brush)FindResource("Pill.CancelFg");
            }

            StatusPill.Background = bg;
            StatusPillText.Foreground = fg;
            StatusPillText.Text = string.IsNullOrWhiteSpace(status) ? "Open" : status;
        }

        // ===== Buttons =====
        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (_detailsOnly)
            {
                MessageBox.Show("This view is read-only. Close to return.", "Info",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            MessageBox.Show("Save not implemented in this build.", "Not Implemented",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (_detailsOnly)
            {
                MessageBox.Show("This view is read-only.", "Info",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            MessageBox.Show("Delete not implemented in this build.", "Not Implemented",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            OnCancelRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
