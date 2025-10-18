using System;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using HouseholdMS.Resources; // Strings.*

namespace HouseholdMS.View.UserControls
{
    public partial class PvEnergyTileControl : UserControl
    {
        // -------- Config (DependencyProperties) --------
        public static readonly DependencyProperty LatitudeProperty =
            DependencyProperty.Register(nameof(Latitude), typeof(double), typeof(PvEnergyTileControl),
                new PropertyMetadata(double.NaN, OnParamsChanged));

        public static readonly DependencyProperty LongitudeProperty =
            DependencyProperty.Register(nameof(Longitude), typeof(double), typeof(PvEnergyTileControl),
                new PropertyMetadata(double.NaN, OnParamsChanged));

        public static readonly DependencyProperty PeakKwProperty =
            DependencyProperty.Register(nameof(PeakKw), typeof(double), typeof(PvEnergyTileControl),
                new PropertyMetadata(5.0, OnParamsChanged));

        public static readonly DependencyProperty TiltProperty =
            DependencyProperty.Register(nameof(Tilt), typeof(int), typeof(PvEnergyTileControl),
                new PropertyMetadata(30, OnParamsChanged));

        public static readonly DependencyProperty AzimuthProperty =
            DependencyProperty.Register(nameof(Azimuth), typeof(int), typeof(PvEnergyTileControl),
                new PropertyMetadata(180, OnParamsChanged));

        public static readonly DependencyProperty LossesPctProperty =
            DependencyProperty.Register(nameof(LossesPct), typeof(double), typeof(PvEnergyTileControl),
                new PropertyMetadata(14.0, OnParamsChanged));

        public double Latitude { get { return (double)GetValue(LatitudeProperty); } set { SetValue(LatitudeProperty, value); } }
        public double Longitude { get { return (double)GetValue(LongitudeProperty); } set { SetValue(LongitudeProperty, value); } }
        public double PeakKw { get { return (double)GetValue(PeakKwProperty); } set { SetValue(PeakKwProperty, value); } }
        public int Tilt { get { return (int)GetValue(TiltProperty); } set { SetValue(TiltProperty, value); } }
        public int Azimuth { get { return (int)GetValue(AzimuthProperty); } set { SetValue(AzimuthProperty, value); } }
        public double LossesPct { get { return (double)GetValue(LossesPctProperty); } set { SetValue(LossesPctProperty, value); } }

        // -------- UI state / text (DependencyProperties) --------
        public static readonly DependencyProperty IsBusyProperty =
            DependencyProperty.Register(nameof(IsBusy), typeof(bool), typeof(PvEnergyTileControl),
                new PropertyMetadata(false));

        public bool IsBusy
        {
            get { return (bool)GetValue(IsBusyProperty); }
            set { SetValue(IsBusyProperty, value); }
        }

        public static readonly DependencyProperty StatusTextProperty =
            DependencyProperty.Register(nameof(StatusText), typeof(string), typeof(PvEnergyTileControl),
                new PropertyMetadata(Strings.PVET_Status_Loading));

        public static readonly DependencyProperty MonthlyTextProperty =
            DependencyProperty.Register(nameof(MonthlyText), typeof(string), typeof(PvEnergyTileControl),
                new PropertyMetadata("—"));

        public static readonly DependencyProperty AnnualTextProperty =
            DependencyProperty.Register(nameof(AnnualText), typeof(string), typeof(PvEnergyTileControl),
                new PropertyMetadata("—"));

        public static readonly DependencyProperty ConfigTextProperty =
            DependencyProperty.Register(nameof(ConfigText), typeof(string), typeof(PvEnergyTileControl),
                new PropertyMetadata(string.Empty));

        public string StatusText { get { return (string)GetValue(StatusTextProperty); } set { SetValue(StatusTextProperty, value); } }
        public string MonthlyText { get { return (string)GetValue(MonthlyTextProperty); } set { SetValue(MonthlyTextProperty, value); } }
        public string AnnualText { get { return (string)GetValue(AnnualTextProperty); } set { SetValue(AnnualTextProperty, value); } }
        public string ConfigText { get { return (string)GetValue(ConfigTextProperty); } set { SetValue(ConfigTextProperty, value); } }

        public PvEnergyTileControl()
        {
            InitializeComponent();
            Loaded += PvEnergyTileControl_Loaded;
        }

        private void PvEnergyTileControl_Loaded(object sender, RoutedEventArgs e)
        {
            var _ = RefreshAsync();
        }

        private static void OnParamsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var ctrl = d as PvEnergyTileControl;
            if (ctrl != null && ctrl.IsLoaded)
            {
                var _ = ctrl.RefreshAsync();
            }
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            await RefreshAsync();
        }

        private async Task RefreshAsync()
        {
            try
            {
                IsBusy = true;
                StatusText = Strings.PVET_Status_Loading;

                if (double.IsNaN(Latitude) || double.IsNaN(Longitude) || PeakKw <= 0)
                {
                    MonthlyText = Strings.PVET_NoConfig;
                    AnnualText = Strings.PVET_AnnualDash;
                    ConfigText = string.Empty;
                    StatusText = string.Empty;
                    IsBusy = false;
                    return;
                }

                var res = await SolarApiClient.GetPvGisPvcalcAsync(Latitude, Longitude, PeakKw, Tilt, Azimuth, LossesPct);
                if (res != null && res.Monthly != null && res.Monthly.Length == 12)
                {
                    double avg = 0;
                    int cnt = 0;
                    for (int i = 0; i < 12; i++)
                    {
                        if (!double.IsNaN(res.Monthly[i])) { avg += res.Monthly[i]; cnt++; }
                    }

                    if (cnt > 0)
                    {
                        MonthlyText = string.Format(
                            CultureInfo.InvariantCulture,
                            Strings.PVET_MonthlyAvgFmt, (avg / cnt));
                    }
                    else
                    {
                        MonthlyText = Strings.PVET_MonthlyAvgDash;
                    }

                    AnnualText = double.IsNaN(res.AnnualKWh)
                        ? Strings.PVET_AnnualDash
                        : string.Format(CultureInfo.InvariantCulture, Strings.PVET_AnnualFmt, res.AnnualKWh);
                }
                else
                {
                    MonthlyText = Strings.PVET_MonthlyAvgDash;
                    AnnualText = Strings.PVET_AnnualDash;
                }

                ConfigText = string.Format(
                    CultureInfo.InvariantCulture,
                    Strings.PVET_ConfigFmt,
                    PeakKw, Tilt, Azimuth, LossesPct);

                StatusText = string.Empty;
            }
            catch
            {
                StatusText = Strings.PVET_Status_Error;
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}
