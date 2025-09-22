using HouseholdMS.Model;
using System;
using System.Collections.Generic;
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
            LoadGrids();
        }

        private void LoadReportDetails()
        {
            if (_report == null) return;

            HouseholdIDBox.Text = _report.HouseholdID.ToString();
            TechnicianIDBox.Text = _report.TechnicianID.ToString();
            TestDateBox.Text = _report.TestDate != DateTime.MinValue
                ? _report.TestDate.ToString("yyyy-MM-dd")
                : string.Empty;
            DeviceStatusBox.Text = _report.DeviceStatus ?? string.Empty;

            // Preserve your original strings (for the hidden boxes)
            InspectionItemsBox.Text = FormatInspectionItems(_report.InspectionItems);
            AnnotationsBox.Text = FormatStringList(_report.Annotations);
            SettingsVerificationBox.Text = FormatSettingsVerification(_report.SettingsVerification);
            ImagePathsBox.Text = FormatStringList(_report.ImagePaths);
        }

        private void LoadGrids()
        {
            // Bind visible DataGrids/ListBox for better readability
            if (_report != null)
            {
                if (_report.InspectionItems != null)
                    InspectionGrid.ItemsSource = _report.InspectionItems;
                else
                    InspectionGrid.ItemsSource = new List<object>();

                if (_report.SettingsVerification != null)
                    SettingsGrid.ItemsSource = _report.SettingsVerification;
                else
                    SettingsGrid.ItemsSource = new List<object>();

                if (_report.ImagePaths != null)
                    ImagePathsList.ItemsSource = _report.ImagePaths;
                else
                    ImagePathsList.ItemsSource = new List<string>();
            }
        }

        // Display InspectionItems as table-like text (kept for compatibility)
        private string FormatInspectionItems(List<InspectionItem> items)
        {
            if (items == null || items.Count == 0) return string.Empty;
            var sb = new StringBuilder();
            foreach (var item in items)
                sb.AppendLine(string.Format("{0} | {1} | {2}", item.Name, item.Result, item.Annotation));
            return sb.ToString();
        }

        // Display SettingsVerification as multi-line (kept for compatibility)
        private string FormatSettingsVerification(List<SettingsVerificationItem> items)
        {
            if (items == null || items.Count == 0) return string.Empty;
            var sb = new StringBuilder();
            foreach (var item in items)
                sb.AppendLine(string.Format("{0}: {1} [{2}]", item.Parameter, item.Value, item.Status));
            return sb.ToString();
        }

        // Display a list of strings, one per line (kept for compatibility)
        private string FormatStringList(List<string> list)
        {
            if (list == null || list.Count == 0) return string.Empty;
            return string.Join(Environment.NewLine, list.ToArray());
        }

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "PDF files (*.pdf)|*.pdf",
                FileName = string.Format("TestReport_{0}_{1}.pdf",
                    _report.ReportID,
                    _report.TestDate == DateTime.MinValue ? "unknown" : _report.TestDate.ToString("yyyyMMdd"))
            };

            if (dlg.ShowDialog() == true)
            {
                string filePath = dlg.FileName;

                PdfReportGenerator.GenerateTestReportPDF(_report, filePath);

                try
                {
                    if (System.IO.File.Exists(filePath) && new System.IO.FileInfo(filePath).Length > 100)
                    {
                        MessageBox.Show("PDF created successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                        try { System.Diagnostics.Process.Start(filePath); } catch { /* ignore */ }
                    }
                    else
                    {
                        MessageBox.Show("PDF creation failed!", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                catch
                {
                    MessageBox.Show("PDF creation failed!", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        // Optional close hook (unchanged)
        private void CloseButton_Click(object sender, EventArgs e)
        {
            if (OnCloseRequested != null)
                OnCloseRequested(this, EventArgs.Empty);
        }
    }
}
