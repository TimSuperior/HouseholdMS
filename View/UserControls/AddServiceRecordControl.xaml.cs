using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HouseholdMS.View; // for ServiceRow

namespace HouseholdMS.View.UserControls
{
    public partial class AddServiceRecordControl : UserControl
    {
        private readonly bool _detailsOnly;
        private readonly ServiceRow _row;

        public event EventHandler OnSavedSuccessfully;
        public event EventHandler OnCancelRequested;

        // Form mode (kept)
        public AddServiceRecordControl()
        {
            InitializeComponent();
            FormHeader.Text = "➕ Add Service Record";

            DetailsPanel.Visibility = Visibility.Collapsed;
            FormPanel.Visibility = Visibility.Visible;

            SaveButton.Visibility = Visibility.Visible;
            DeleteButton.Visibility = Visibility.Collapsed;
            CancelButton.Content = "✖ Close";
        }

        // Details mode (used from ServiceRecordsView)
        public AddServiceRecordControl(ServiceRow row) : this()
        {
            _row = row ?? throw new ArgumentNullException(nameof(row));
            _detailsOnly = true;

            FormHeader.Text = $"Service #{row.ServiceID} — Details";
            DetailsPanel.Visibility = Visibility.Visible;
            FormPanel.Visibility = Visibility.Collapsed;

            // Buttons
            SaveButton.Visibility = Visibility.Collapsed;
            DeleteButton.Visibility = Visibility.Collapsed;
            CancelButton.Content = "✖ Close";

            // Populate details (Primary Tech removed; Team -> Technicians)
            ServiceIdValue.Text = row.ServiceID.ToString();
            HouseholdValue.Text = row.HouseholdText;
            TechniciansValue.Text = string.IsNullOrWhiteSpace(row.AllTechnicians) ? "—" : row.AllTechnicians;
            ProblemValue.Text = string.IsNullOrWhiteSpace(row.Problem) ? "—" : row.Problem;
            ActionValue.Text = string.IsNullOrWhiteSpace(row.Action) ? "—" : row.Action;
            InventoryValue.Text = string.IsNullOrWhiteSpace(row.InventorySummary) ? "—" : row.InventorySummary;
            StartedValue.Text = string.IsNullOrWhiteSpace(row.StartDateText) ? "—" : row.StartDateText;
            FinishedValue.Text = string.IsNullOrWhiteSpace(row.FinishDateText) ? "—" : row.FinishDateText;

            ApplyStatusPill(row.StatusText);
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
            else if (s == "finished")
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
            // Visibility handled by XAML trigger (shows when DetailsPanel is Visible)
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (_detailsOnly)
            {
                MessageBox.Show("This view is read-only.", "Info",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            // Implement create/update here if needed by your app.
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
