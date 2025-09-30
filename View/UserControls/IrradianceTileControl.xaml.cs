using System;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace HouseholdMS.View.UserControls
{
    public partial class IrradianceTileControl : UserControl
    {
        // -------- Location (DependencyProperties) --------
        public static readonly DependencyProperty LatitudeProperty =
            DependencyProperty.Register(
                nameof(Latitude),
                typeof(double),
                typeof(IrradianceTileControl),
                new PropertyMetadata(double.NaN, OnParamsChanged));

        public static readonly DependencyProperty LongitudeProperty =
            DependencyProperty.Register(
                nameof(Longitude),
                typeof(double),
                typeof(IrradianceTileControl),
                new PropertyMetadata(double.NaN, OnParamsChanged));

        public double Latitude
        {
            get => (double)GetValue(LatitudeProperty);
            set => SetValue(LatitudeProperty, value);
        }

        public double Longitude
        {
            get => (double)GetValue(LongitudeProperty);
            set => SetValue(LongitudeProperty, value);
        }

        // -------- UI text (DependencyProperties) --------
        public static readonly DependencyProperty StatusTextProperty =
            DependencyProperty.Register(nameof(StatusText), typeof(string), typeof(IrradianceTileControl),
                new PropertyMetadata("(loading...)"));
        public static readonly DependencyProperty TodayGhiTextProperty =
            DependencyProperty.Register(nameof(TodayGhiText), typeof(string), typeof(IrradianceTileControl),
                new PropertyMetadata("—"));
        public static readonly DependencyProperty TomorrowPeakTextProperty =
            DependencyProperty.Register(nameof(TomorrowPeakText), typeof(string), typeof(IrradianceTileControl),
                new PropertyMetadata("—"));
        public static readonly DependencyProperty LastUpdatedTextProperty =
            DependencyProperty.Register(nameof(LastUpdatedText), typeof(string), typeof(IrradianceTileControl),
                new PropertyMetadata(string.Empty));

        public string StatusText
        {
            get => (string)GetValue(StatusTextProperty);
            set => SetValue(StatusTextProperty, value);
        }
        public string TodayGhiText
        {
            get => (string)GetValue(TodayGhiTextProperty);
            set => SetValue(TodayGhiTextProperty, value);
        }
        public string TomorrowPeakText
        {
            get => (string)GetValue(TomorrowPeakTextProperty);
            set => SetValue(TomorrowPeakTextProperty, value);
        }
        public string LastUpdatedText
        {
            get => (string)GetValue(LastUpdatedTextProperty);
            set => SetValue(LastUpdatedTextProperty, value);
        }

        public IrradianceTileControl()
        {
            InitializeComponent();
            Loaded += IrradianceTileControl_Loaded;
        }

        private void IrradianceTileControl_Loaded(object sender, RoutedEventArgs e)
        {
            var _ = RefreshAsync();
        }

        private static void OnParamsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var ctrl = d as IrradianceTileControl;
            if (ctrl != null && ctrl.IsLoaded)
            {
                var _ = ctrl.RefreshAsync();
            }
        }

        // wired to the little refresh button inside the tile
        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            await RefreshAsync();
        }

        private async Task RefreshAsync()
        {
            try
            {
                StatusText = "(loading...)";

                if (double.IsNaN(Latitude) || double.IsNaN(Longitude))
                {
                    TodayGhiText = "No coords";
                    TomorrowPeakText = "—";
                    StatusText = "";
                    LastUpdatedText = "";
                    return;
                }

                DateTime todayUtc = DateTime.UtcNow.Date;

                // Cached today’s GHI (10 min TTL) to speed up the dashboard
                double? todayKwhm2 = await SolarApiClient.GetTodayGhiCachedAsync(
                    Latitude, Longitude, todayUtc, TimeSpan.FromMinutes(10));

                TodayGhiText = todayKwhm2.HasValue
                    ? string.Format(CultureInfo.InvariantCulture, "Today GHI: {0:0.0} kWh/m²", todayKwhm2.Value)
                    : "Today GHI: —";

                // Tomorrow peak from Open-Meteo hourly
                DateTime tomorrow = todayUtc.AddDays(1);
                double? peakWm2 = await SolarApiClient.GetOpenMeteoTomorrowPeakShortwaveAsync(
                    Latitude, Longitude, tomorrow);

                TomorrowPeakText = peakWm2.HasValue
                    ? string.Format(CultureInfo.InvariantCulture, "Tomorrow peak: {0:0} W/m²", peakWm2.Value)
                    : "Tomorrow peak: —";

                StatusText = "";
                LastUpdatedText = "Updated " + DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            }
            catch
            {
                StatusText = "(error)";
            }
        }
    }
}
