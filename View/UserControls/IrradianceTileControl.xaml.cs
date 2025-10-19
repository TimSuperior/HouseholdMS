using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using HouseholdMS.Resources; // Strings.*

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

        // -------- State booleans for language-agnostic triggers --------
        public static readonly DependencyProperty IsLoadingProperty =
            DependencyProperty.Register(nameof(IsLoading), typeof(bool), typeof(IrradianceTileControl),
                new PropertyMetadata(false));

        public static readonly DependencyProperty IsErrorProperty =
            DependencyProperty.Register(nameof(IsError), typeof(bool), typeof(IrradianceTileControl),
                new PropertyMetadata(false));

        public bool IsLoading
        {
            get => (bool)GetValue(IsLoadingProperty);
            set => SetValue(IsLoadingProperty, value);
        }

        public bool IsError
        {
            get => (bool)GetValue(IsErrorProperty);
            set => SetValue(IsErrorProperty, value);
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

                var todayUtc = DateTime.UtcNow.Date;
                var tomorrowUtc = todayUtc.AddDays(1);

                double? todayKwhm2 = null;
                double? tomorrowPeakWm2 = null;

                // -------- FAST PATH: one Open-Meteo call (like the Details window) --------
                try
                {
                    var (past7, next3) = await SolarApiClient.GetOpenMeteoPast7Next3Async(Latitude, Longitude, todayUtc);

                    var todayRow = past7?.FirstOrDefault(r => r.Date.Date == todayUtc);
                    if (todayRow != null) todayKwhm2 = todayRow.Kwhm2;

                    var tomorrowRow = next3?.FirstOrDefault(r => r.Date.Date == tomorrowUtc);
                    if (tomorrowRow != null) tomorrowPeakWm2 = tomorrowRow.PeakWm2;
                }
                catch
                {
                    // swallow to use fallback below
                }

                // -------- FALLBACK: summary API (NASA/Open-Meteo hybrid), same as window --------
                if (!todayKwhm2.HasValue || !tomorrowPeakWm2.HasValue)
                {
                    try
                    {
                        var (past, next) = await SolarApiClient.GetIrradianceSummaryAsync(Latitude, Longitude, todayUtc);

                        if (!todayKwhm2.HasValue)
                        {
                            var t = past?.FirstOrDefault(r => r.Date.Date == todayUtc);
                            if (t != null) todayKwhm2 = t.Kwhm2;
                        }

                        if (!tomorrowPeakWm2.HasValue)
                        {
                            var tm = next?.FirstOrDefault(r => r.Date.Date == tomorrowUtc);
                            if (tm != null) tomorrowPeakWm2 = tm.PeakWm2;
                        }
                    }
                    catch
                    {
                        // swallow; we have per-metric fallbacks next
                    }
                }

                // -------- Per-metric last-resort fallbacks (original helpers) --------
                if (!todayKwhm2.HasValue)
                {
                    todayKwhm2 = await SolarApiClient.GetTodayGhiCachedAsync(
                        Latitude, Longitude, todayUtc, TimeSpan.FromMinutes(10));
                }
                if (!tomorrowPeakWm2.HasValue)
                {
                    tomorrowPeakWm2 = await SolarApiClient.GetOpenMeteoTomorrowPeakShortwaveAsync(
                        Latitude, Longitude, tomorrowUtc);
                }

                // -------- Bind text safely (no exceptions bubble out) --------
                TodayGhiText = todayKwhm2.HasValue
                    ? string.Format(CultureInfo.CurrentCulture, Strings.IRR_TodayGhiFmt, todayKwhm2.Value)
                    : Strings.IRR_TodayGhiDash;

                TomorrowPeakText = tomorrowPeakWm2.HasValue
                    ? string.Format(CultureInfo.CurrentCulture, Strings.IRR_TomorrowPeakFmt, tomorrowPeakWm2.Value)
                    : Strings.IRR_TomorrowPeakDash;

                StatusText = string.Empty; // hide chip on success
                string ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
                LastUpdatedText = string.Format(CultureInfo.CurrentCulture, Strings.IRR_UpdatedFmt, ts);

                IsLoading = false;
            }
            catch
            {
                IsLoading = false;
                IsError = true;
                StatusText = Strings.IRR_Status_Error;
                // (Optional) log the exception to your app logger if you have one
            }
        }
    }
}
