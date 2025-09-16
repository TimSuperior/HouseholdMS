using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace HouseholdMS.View.Measurement.Controls
{
    public partial class MeasurementsTab : UserControl
    {
        public MeasurementsTab() { InitializeComponent(); }

        public string MeasureSource
        {
            get { var it = CmbSource.SelectedItem as ComboBoxItem; return it != null && it.Content != null ? it.Content.ToString() : "CH1"; }
        }

        public int PollIntervalMs { get { return (int)SldPoll.Value; } }

        public event EventHandler PollIntervalChanged;
        private void SldPoll_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        { var h = PollIntervalChanged; if (h != null) h(this, EventArgs.Empty); }

        private static string F(double? v) => v.HasValue ? v.Value.ToString("G5", CultureInfo.InvariantCulture) : "-";

        public void UpdateValues(HouseholdMS.View.Measurement.MeasurementValues m)
        {
            ValVpp.Text = (M_Vpp.IsChecked == true) ? F(m.Vpp) : "-";
            ValVavg.Text = (M_Vavg.IsChecked == true) ? F(m.Vavg) : "-";
            ValVrms.Text = (M_Vrms.IsChecked == true) ? F(m.Vrms) : "-";
            ValVmax.Text = (M_Vmax.IsChecked == true) ? F(m.Vmax) : "-";
            ValVmin.Text = (M_Vmin.IsChecked == true) ? F(m.Vmin) : "-";
            ValVpos.Text = (M_Vpos.IsChecked == true) ? F(m.Vpos) : "-";
            ValVneg.Text = (M_Vneg.IsChecked == true) ? F(m.Vneg) : "-";
            ValFreq.Text = (M_Freq.IsChecked == true) ? F(m.Freq) : "-";
            ValPer.Text = (M_Per.IsChecked == true) ? F(m.Period) : "-";
            ValDuty.Text = (M_Duty.IsChecked == true) ? F(m.Duty) : "-";
            ValRise.Text = (M_Rise.IsChecked == true) ? F(m.Rise) : "-";
            ValFall.Text = (M_Fall.IsChecked == true) ? F(m.Fall) : "-";
            ValPwPos.Text = (M_PwPos.IsChecked == true) ? F(m.PwPos) : "-";
            ValPwNeg.Text = (M_PwNeg.IsChecked == true) ? F(m.PwNeg) : "-";
            ValRipple.Text = (M_Ripple.IsChecked == true) ? F(m.RipplePct) : "-";
            ValCrest.Text = (M_Crest.IsChecked == true) ? F(m.Crest) : "-";
            ValOver.Text = (M_Overshoot.IsChecked == true) ? F(m.OvershootPct) : "-";
            ValUnder.Text = (M_Undershoot.IsChecked == true) ? F(m.UndershootPct) : "-";
            ValZeroCross.Text = (M_ZeroCross.IsChecked == true) ? F(m.ZeroCrossRate) : "-";
        }
    }
}
