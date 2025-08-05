using HouseholdMS.Model;
using System;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace HouseholdMS.View.UserControls
{
    public partial class TestReportDetailControl : UserControl
    {
        private readonly TestReport _report;

        public event EventHandler OnCloseRequested;

        public TestReportDetailControl(TestReport report)
        {
            InitializeComponent();
            _report = report;
            LoadReportDetails();
        }

        private void LoadReportDetails()
        {
            HouseholdIDBox.Text = _report.HouseholdID.ToString();
            TechnicianIDBox.Text = _report.TechnicianID.ToString();
            TestDateBox.Text = _report.TestDate != DateTime.MinValue
                ? _report.TestDate.ToString("yyyy-MM-dd")
                : "";
            DeviceStatusBox.Text = _report.DeviceStatus ?? "";

            // Convert object lists to multi-line readable strings:
            InspectionItemsBox.Text = FormatInspectionItems(_report.InspectionItems);
            AnnotationsBox.Text = FormatStringList(_report.Annotations);
            SettingsVerificationBox.Text = FormatSettingsVerification(_report.SettingsVerification);
            ImagePathsBox.Text = FormatStringList(_report.ImagePaths);
        }

        // Display InspectionItems as table-like text
        private string FormatInspectionItems(System.Collections.Generic.List<InspectionItem> items)
        {
            if (items == null || items.Count == 0) return "";
            var sb = new StringBuilder();
            foreach (var item in items)
            {
                sb.AppendLine($"{item.Name} | {item.Result} | {item.Annotation}");
            }
            return sb.ToString();
        }

        // Display SettingsVerification as multi-line
        private string FormatSettingsVerification(System.Collections.Generic.List<SettingsVerificationItem> items)
        {
            if (items == null || items.Count == 0) return "";
            var sb = new StringBuilder();
            foreach (var item in items)
            {
                sb.AppendLine($"{item.Parameter}: {item.Value} [{item.Status}]");
            }
            return sb.ToString();
        }

        // Display a list of strings, one per line
        private string FormatStringList(System.Collections.Generic.List<string> list)
        {
            if (list == null || list.Count == 0) return "";
            return string.Join(Environment.NewLine, list);
        }

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "PDF files (*.pdf)|*.pdf",
                FileName = $"TestReport_{_report.ReportID}_{_report.TestDate:yyyyMMdd}.pdf"
            };
            if (dlg.ShowDialog() == true)
            {
                string filePath = dlg.FileName;

                PdfReportGenerator.GenerateTestReportPDF(_report, filePath);

                if (System.IO.File.Exists(filePath) && new System.IO.FileInfo(filePath).Length > 100)
                {
                    MessageBox.Show("PDF created successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    try { System.Diagnostics.Process.Start(filePath); } catch { }
                }
                else
                {
                    MessageBox.Show("PDF creation failed!", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        // Optionally, provide a close button event handler
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            OnCloseRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
