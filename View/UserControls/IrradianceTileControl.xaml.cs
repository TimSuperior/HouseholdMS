using System;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using HouseholdMS.Resources; // <-- access to Strings.*

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
            get { return (double)GetValue(LatitudeProperty); }
            set { SetValue(LatitudeProperty, value); }
        }

        public double Longitude
        {
            get { return (double)GetValue(LongitudeProperty); }
            set { SetValue(LongitudeProperty, value); }
        }

        // -------- State booleans for language-agnostic triggers --------
        public static readonly DependencyProperty IsLoadingProperty =
            DependencyProperty.Register(nameof(IsLoading), typeof(bool), typeof(IrradianceTileControl),
                new PropertyMetadata(false));

        public static readonly DependencyProperty IsErrorProperty =
            DependencyProperty.Register(nameof(IsError), typeof(bool), typeof(IrradianceTileControl),
                new PropertyMetadata(false));

        public bool IsLoading
        {
            get { return (bool)GetValue(IsLoadingProperty); }
            set { SetValue(IsLoadingProperty, value); }
        }

        public bool IsError
        {
            get { return (bool)GetValue(IsErrorProperty); }
            set { SetValue(IsErrorProperty, value); }
        }

        // -------- UI text (DependencyProperties) --------
        public static readonly DependencyProperty StatusTextProperty =
            DependencyProperty.Register(nameof(StatusText), typeof(string), typeof(IrradianceTileControl),
                new PropertyMetadata(Strings.IRR_Status_Loading));

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
            get { return (string)GetValue(StatusTextProperty); }
            set { SetValue(StatusTextProperty, value); }
        }
        public string TodayGhiText
        {
            get { return (string)GetValue(TodayGhiTextProperty); }
            set { SetValue(TodayGhiTextProperty, value); }
        }
        public string TomorrowPeakText
        {
            get { return (string)GetValue(TomorrowPeakTextProperty); }
            set { SetValue(TomorrowPeakTextProperty, value); }
        }
        public string LastUpdatedText
        {
            get { return (string)GetValue(LastUpdatedTextProperty); }
            set { SetValue(LastUpdatedTextProperty, value); }
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
                IsLoading = true;
                IsError = false;
                StatusText = Strings.IRR_Status_Loading;

                if (double.IsNaN(Latitude) || double.IsNaN(Longitude))
                {
                    TodayGhiText = Strings.IRR_NoCoords;
                    TomorrowPeakText = "—";
                    StatusText = string.Empty;
                    LastUpdatedText = string.Empty;
                    IsLoading = false;
                    return;
                }

                DateTime todayUtc = DateTime.UtcNow.Date;

                // Cached today’s GHI (10 min TTL) to speed up the dashboard
                double? todayKwhm2 = await SolarApiClient.GetTodayGhiCachedAsync(
                    Latitude, Longitude, todayUtc, TimeSpan.FromMinutes(10));

                TodayGhiText = todayKwhm2.HasValue
                    ? string.Format(CultureInfo.CurrentCulture, Strings.IRR_TodayGhiFmt, todayKwhm2.Value)
                    : Strings.IRR_TodayGhiDash;

                // Tomorrow peak from Open-Meteo hourly
                DateTime tomorrow = todayUtc.AddDays(1);
                double? peakWm2 = await SolarApiClient.GetOpenMeteoTomorrowPeakShortwaveAsync(
                    Latitude, Longitude, tomorrow);

                TomorrowPeakText = peakWm2.HasValue
                    ? string.Format(CultureInfo.CurrentCulture, Strings.IRR_TomorrowPeakFmt, peakWm2.Value)
                    : Strings.IRR_TomorrowPeakDash;

                StatusText = string.Empty;
                // keep explicit yyyy-MM-dd HH:mm like before, but localized prefix
                string ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
                LastUpdatedText = string.Format(CultureInfo.CurrentCulture, Strings.IRR_UpdatedFmt, ts);

                IsLoading = false;
            }
            catch
            {
                IsLoading = false;
                IsError = true;
                StatusText = Strings.IRR_Status_Error;
            }
        }
    }
}
