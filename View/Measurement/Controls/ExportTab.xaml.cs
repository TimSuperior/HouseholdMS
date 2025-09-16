using System.Windows;
using System.Windows.Controls;

namespace HouseholdMS.View.Measurement.Controls
{
    public partial class ExportTab : UserControl
    {
        public ExportTab() { InitializeComponent(); }
        public event RoutedEventHandler SaveCsvRequested;
        public event RoutedEventHandler SavePngRequested;
        private void SaveCsv_Click(object s, RoutedEventArgs e) => SaveCsvRequested?.Invoke(this, e);
        private void SavePng_Click(object s, RoutedEventArgs e) => SavePngRequested?.Invoke(this, e);
        public void ShowResult(string text) { ResultBlock.Text = text ?? ""; }
    }
}
