using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using HouseholdMS.Resources; // for Strings.*

namespace HouseholdMS.View.Dashboard
{
    public partial class IrradianceDetailsWindow : Window
    {
        private readonly double _lat, _lon;

        public IrradianceDetailsWindow(double lat, double lon)
        {
            InitializeComponent();
            _lat = lat; _lon = lon;

            LocText.Text = string.Format(CultureInfo.InvariantCulture,
                "(lat {0:0.###}, lon {1:0.###})", _lat, _lon);

            Loaded += IrradianceDetailsWindow_Loaded;
        }

        private void IrradianceDetailsWindow_Loaded(object sender, RoutedEventArgs e)
        {
            var _ = RefreshAsync();
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            await RefreshAsync();
        }

        private async Task RefreshAsync()
        {
            try
            {
                var today = DateTime.UtcNow.Date;
                List<Row> pastRows;
                List<Row> forecastRows;

                // -------- FAST PATH: one Open-Meteo call for past-7 + forecast-3 (+ hourly peaks) --------
                try
                {
                    var (past7, next3) = await SolarApiClient.GetOpenMeteoPast7Next3Async(_lat, _lon, today);

                    pastRows = past7
                        .OrderBy(r => r.Date)
                        .Select(r => new Row
                        {
                            Date = r.Date.ToString("yyyy-MM-dd"),
                            GhiKwhm2 = r.Kwhm2.HasValue ? r.Kwhm2.Value.ToString("0.00", CultureInfo.InvariantCulture) : Strings.Common_NoDataDash,
                            PeakWm2 = Strings.Common_NoDataDash // not shown in Past grid
                        })
                        .ToList();

                    forecastRows = next3
                        .OrderBy(r => r.Date)
                        .Select(r => new Row
                        {
                            Date = r.Date.ToString("yyyy-MM-dd"),
                            GhiKwhm2 = r.Kwhm2.HasValue ? r.Kwhm2.Value.ToString("0.00", CultureInfo.InvariantCulture) : Strings.Common_NoDataDash,
                            PeakWm2 = r.PeakWm2.HasValue ? r.PeakWm2.Value.ToString("0", CultureInfo.InvariantCulture) : Strings.Common_NoDataDash
                        })
                        .ToList();

                    SetSummary(past7.Select(x => x.Kwhm2), next3.Select(x => x.Kwhm2), Strings.IrrDet_Mode_OpenMeteoFast);
                }
                catch
                {
                    // -------- PRECISE FALLBACK: batched NASA daily + hourly + Open-Meteo fallback --------
                    var (past, next) = await SolarApiClient.GetIrradianceSummaryAsync(_lat, _lon, today);

                    pastRows = past
                        .OrderBy(r => r.Date)
                        .Select(r => new Row
                        {
                            Date = r.Date.ToString("yyyy-MM-dd"),
                            GhiKwhm2 = r.Kwhm2.HasValue ? r.Kwhm2.Value.ToString("0.00", CultureInfo.InvariantCulture) : Strings.Common_NoDataDash,
                            PeakWm2 = Strings.Common_NoDataDash
                        })
                        .ToList();

                    forecastRows = next
                        .OrderBy(r => r.Date)
                        .Select(r => new Row
                        {
                            Date = r.Date.ToString("yyyy-MM-dd"),
                            GhiKwhm2 = r.Kwhm2.HasValue ? r.Kwhm2.Value.ToString("0.00", CultureInfo.InvariantCulture) : Strings.Common_NoDataDash,
                            PeakWm2 = r.PeakWm2.HasValue ? r.PeakWm2.Value.ToString("0", CultureInfo.InvariantCulture) : Strings.Common_NoDataDash
                        })
                        .ToList();

                    SetSummary(past.Select(x => x.Kwhm2), next.Select(x => x.Kwhm2), Strings.IrrDet_Mode_NasaFallback);
                }

                // Bind
                PastGrid.ItemsSource = null;
                PastGrid.ItemsSource = pastRows;

                ForecastGrid.ItemsSource = null;
                ForecastGrid.ItemsSource = forecastRows;

                // Title quick sanity check (keeps localization)
                this.Title = Strings.IrrDet_Title_Base + " • " + Strings.IrrDet_RowsLabel + ":" + (pastRows.Count + forecastRows.Count);
            }
            catch (Exception ex)
            {
                MessageBox.Show(Strings.Err_IrradianceLoadFailed + "\n" + ex.Message,
                    Strings.Common_ErrorCaption, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SetSummary(IEnumerable<double?> pastVals, IEnumerable<double?> nextVals, string mode)
        {
            double? avgPast = Avg(pastVals);
            double? avgNext = Avg(nextVals);

            string pastTxt = avgPast.HasValue ? avgPast.Value.ToString("0.00", CultureInfo.InvariantCulture) : Strings.Common_NoDataDash;
            string nextTxt = avgNext.HasValue ? avgNext.Value.ToString("0.00", CultureInfo.InvariantCulture) : Strings.Common_NoDataDash;

            SummaryText.Text = string.Format(CultureInfo.CurrentUICulture,
                Strings.IrrDet_Summary_Format, mode, pastTxt, nextTxt);
        }

        private static double? Avg(IEnumerable<double?> seq)
        {
            var vals = seq.Where(v => v.HasValue).Select(v => v.Value).ToArray();
            if (vals.Length == 0) return null;
            return vals.Average();
        }

        private class Row
        {
            public string Date { get; set; }
            public string GhiKwhm2 { get; set; }
            public string PeakWm2 { get; set; } // "—" for past rows
        }
    }
}
