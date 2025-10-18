using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using HouseholdMS.Resources;

namespace HouseholdMS.View.Dashboard
{
    public partial class PvEnergyDetailsWindow : Window
    {
        private readonly double _lat, _lon, _kw, _loss;
        private readonly int _tilt, _az;

        public PvEnergyDetailsWindow(double lat, double lon, double peakKw, int tilt, int azimuth, double lossesPct)
        {
            InitializeComponent();
            _lat = lat; _lon = lon; _kw = peakKw; _tilt = tilt; _az = azimuth; _loss = lossesPct;

            CfgText.Text = string.Format(
                CultureInfo.InvariantCulture,
                "(lat {0:0.###}, lon {1:0.###}) • {2:0.##} kWp • tilt {3}°, az {4}° • losses {5:0.#}%",
                _lat, _lon, _kw, _tilt, _az, _loss);

            Loaded += PvEnergyDetailsWindow_Loaded;
        }

        private void PvEnergyDetailsWindow_Loaded(object sender, RoutedEventArgs e)
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
                var res = await SolarApiClient.GetPvGisPvcalcAsync(_lat, _lon, _kw, _tilt, _az, _loss);
                if (res == null)
                {
                    MessageBox.Show(Strings.PVEDW_NoData_Text, Strings.PVEDW_Info_Caption, MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var rows = new List<Row>();
                for (int i = 0; i < res.Monthly.Length; i++)
                {
                    rows.Add(new Row
                    {
                        Month = res.MonthNames[i],
                        Kwh = res.Monthly[i].ToString("0", CultureInfo.InvariantCulture)
                    });
                }

                MonthGrid.ItemsSource = null;
                MonthGrid.ItemsSource = rows;

                AnnualText.Text = res.AnnualKWh.ToString("0", CultureInfo.InvariantCulture) + " kWh";

                // Extras: specific yield & capacity factor
                if (_kw > 0)
                {
                    double specificYield = res.AnnualKWh / _kw; // kWh/kWp
                    double capacityFactor = (res.AnnualKWh / (_kw * 8760.0)) * 100.0; // %
                    SpecificYieldText.Text = specificYield.ToString("0.#", CultureInfo.InvariantCulture) + " kWh/kWp";
                    CapacityFactorText.Text = capacityFactor.ToString("0.0", CultureInfo.InvariantCulture) + " %";
                }
                else
                {
                    SpecificYieldText.Text = "—";
                    CapacityFactorText.Text = "—";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    Strings.PVEDW_Error_LoadFailed + "\n" + ex.Message,
                    Strings.PVEDW_Error_Caption,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private class Row
        {
            public string Month { get; set; }
            public string Kwh { get; set; }
        }
    }
}
