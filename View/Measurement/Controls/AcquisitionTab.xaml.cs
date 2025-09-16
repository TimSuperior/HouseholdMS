using System.Windows;
using System.Windows.Controls;

namespace HouseholdMS.View.Measurement.Controls
{
    public partial class AcquisitionTab : UserControl
    {
        public AcquisitionTab()
        {
            InitializeComponent();
            CmbTimebase.SelectedIndex = 9; // 1e-3
        }

        public string SelectedTimebase
        {
            get { var it = CmbTimebase.SelectedItem as ComboBoxItem; return it != null && it.Content != null ? it.Content.ToString() : "1e-3"; }
        }

        public event RoutedEventHandler RunRequested;
        public event RoutedEventHandler StopRequested;
        public event RoutedEventHandler SingleRequested;
        public event RoutedEventHandler AutosetRequested;
        public event RoutedEventHandler SetTimebaseRequested;
        public event RoutedEventHandler ReadRecordLengthRequested;

        private void Run_Click(object s, RoutedEventArgs e) => RunRequested?.Invoke(this, e);
        private void Stop_Click(object s, RoutedEventArgs e) => StopRequested?.Invoke(this, e);
        private void Single_Click(object s, RoutedEventArgs e) => SingleRequested?.Invoke(this, e);
        private void Autoset_Click(object s, RoutedEventArgs e) => AutosetRequested?.Invoke(this, e);
        private void SetTimebase_Click(object s, RoutedEventArgs e) => SetTimebaseRequested?.Invoke(this, e);
        private void ReadRecordLength_Click(object s, RoutedEventArgs e) => ReadRecordLengthRequested?.Invoke(this, e);
    }
}
