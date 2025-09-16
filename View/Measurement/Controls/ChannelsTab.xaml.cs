using System.Windows;
using System.Windows.Controls;

namespace HouseholdMS.View.Measurement.Controls
{
    public partial class ChannelsTab : UserControl
    {
        public ChannelsTab()
        {
            InitializeComponent();
            CmbCoupling.SelectedIndex = 0;
            CmbVScale.SelectedIndex = 6; // 1 V/div
        }

        public bool PlotCh1 { get { return ChkCh1.IsChecked == true; } }
        public bool PlotCh2 { get { return ChkCh2.IsChecked == true; } }
        public bool PlotMath { get { return ChkMath.IsChecked == true; } }
        public string Coupling { get { var it = CmbCoupling.SelectedItem as ComboBoxItem; return it != null && it.Content != null ? it.Content.ToString() : "DC"; } }
        public string Scale { get { var it = CmbVScale.SelectedItem as ComboBoxItem; return it != null && it.Content != null ? it.Content.ToString() : "1"; } }
        public string Probe { get { var it = CmbProbe.SelectedItem as ComboBoxItem; return it != null && it.Content != null ? it.Content.ToString() : "10"; } }

        public event RoutedEventHandler ApplyCh1Requested;
        public event RoutedEventHandler ApplyCh2Requested;
        public event RoutedEventHandler SetProbeRequested;

        private void ApplyCh1_Click(object s, RoutedEventArgs e) => ApplyCh1Requested?.Invoke(this, e);
        private void ApplyCh2_Click(object s, RoutedEventArgs e) => ApplyCh2Requested?.Invoke(this, e);
        private void SetProbe_Click(object s, RoutedEventArgs e) => SetProbeRequested?.Invoke(this, e);
    }
}
