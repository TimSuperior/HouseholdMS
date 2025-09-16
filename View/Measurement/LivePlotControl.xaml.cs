using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Syncfusion.Licensing; // licensing

namespace HouseholdMS.View.Measurement
{
    /// <summary>
    /// Numeric-time live plot (seconds only) using Syncfusion SfChart.
    /// Plotting happens ONLY when Start/Stop is pressed on this control.
    /// Stats are fed from device via SetDeviceStats(min,max,avg).
    /// </summary>
    public partial class LivePlotControl : UserControl
    {
        // Register Syncfusion license once, safely.
        static LivePlotControl()
        {
            try
            {
                // Safe to call multiple times; Syncfusion ignores duplicates.
                SyncfusionLicenseProvider.RegisterLicense(
                    "Mzk3NTExMEAzMzMwMmUzMDJlMzAzYjMzMzAzYlZIMmI2R1J4SGJTT0ExYWF0VTR2L3RMaDJEVUJyNkk2elh1YXpNSWFrSzA9;Mzk3NTExMUAzMzMwMmUzMDJlMzAzYjMzMzAzYmZIUkhmT1JKVzRZNDVKeUtra1BnanozdU5NTUtzeGM2MUNrY2Y0T3laN3c9;Mgo+DSMBPh8sVXN0S0d+X1ZPd11dXmJWd1p/THNYflR1fV9DaUwxOX1dQl9mSXlQd0djW31bdHVWQGRXUkQ=;NRAiBiAaIQQuGjN/VkZ+XU9HcVRDX3xKf0x/TGpQb19xflBPallYVBYiSV9jS3tTcUZiW39ccnFRR2ZbV091Xw==;Mgo+DSMBMAY9C3t3VVhhQlJDfV5AQmBIYVp/TGpJfl96cVxMZVVBJAtUQF1hTH5UdURhWX1cdXBUTmNfWkd2;Mzk3NTExNUAzMzMwMmUzMDJlMzAzYjMzMzAzYkhIbUxNNFR5alVJbys5YkVKdHJHVmYwL1p6ZnZrZ1hkaEQ1alZZQlVWVGs9;Mzk3NTExNkAzMzMwMmUzMDJlMzAzYjMzMzAzYmNwQ2s0ZWc5RzJab2l0ZFArM2R2VGIyWWorek1WenBOaHlPdjN2dnpmOGs9"
                );
            }
            catch { /* ignore – fail-safe if already registered elsewhere */ }
        }

        // Public so ItemsSource type is accessible from outside
        public sealed class PlotPoint
        {
            public double X { get; set; }   // time (seconds)
            public double Y { get; set; }   // value
        }

        public ObservableCollection<PlotPoint> Points { get; } = new ObservableCollection<PlotPoint>();

        private CancellationTokenSource _cts;
        private Func<string> _readFunc;
        private readonly Stopwatch _sw = new Stopwatch();

        /// <summary>Polling interval when this control is self-reading.</summary>
        public int IntervalMs { get; set; } = 500;

        /// <summary>Maximum number of points kept in the series.</summary>
        public int MaxPoints { get; set; } = 2000;

        public LivePlotControl()
        {
            InitializeComponent();
            LineSeries.ItemsSource = Points;

            ResetStats();
            SetupAxisForSeconds();
        }

        /// <summary>Provide a function that returns the raw device string.</summary>
        public void SetReader(Func<string> reader) => _readFunc = reader;

        /// <summary>Clear plot and restart time origin at 0s.</summary>
        public void ResetPlot()
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                Points.Clear();
                _sw.Reset();
                _sw.Start();
                SetupAxisForSeconds();
            }));
        }

        private void SetupAxisForSeconds()
        {
            if (TimeAxis == null) return;
            TimeAxis.Header = "Time (s)";
            TimeAxis.LabelFormat = "0";  // 0,1,2,...
            TimeAxis.Interval = 1;
            TimeAxis.Minimum = 0d;
            // If your version supports it:
            // TimeAxis.RangePadding = ChartRangePadding.None;
        }

        /// <summary>Stop the internal polling loop if running.</summary>
        public void Stop()
        {
            try
            {
                _cts?.Cancel();
                _cts?.Dispose();
                _cts = null;
            }
            catch { }
        }

        // ---- Start/Stop buttons (self-polling ONLY) ----
        private async void StartPlot_Click(object sender, RoutedEventArgs e)
        {
            if (_cts != null)
            {
                MessageBox.Show("Plotter is already running.");
                return;
            }
            if (_readFunc == null)
            {
                MessageBox.Show("No reader set. Call SetReader(...) before starting the plot.");
                return;
            }

            ResetPlot();

            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            await Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        var raw = _readFunc.Invoke();
                        if (TryParseFirstDouble(raw, out double val))
                            AppendSample(val);
                    }
                    catch
                    {
                        // non-fatal read errors
                    }

                    try { await Task.Delay(IntervalMs, token); }
                    catch (TaskCanceledException) { break; }
                }
            });
        }

        private void StopPlot_Click(object sender, RoutedEventArgs e) => Stop();

        /// <summary>Append one sample to the chart immediately (thread-safe).</summary>
        public void AppendSample(double value)
        {
            double t = _sw.Elapsed.TotalSeconds;

            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                Points.Add(new PlotPoint { X = t, Y = value });
                if (Points.Count > MaxPoints)
                    Points.RemoveAt(0);
            }));
        }

        // ---- Stats from device (e.g., via CALC:AVER:ALL?) ----
        public void SetDeviceStats(double? min, double? max, double? avg)
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                MinText.Text = min.HasValue ? min.Value.ToString("G6", CultureInfo.InvariantCulture) : "-";
                MaxText.Text = max.HasValue ? max.Value.ToString("G6", CultureInfo.InvariantCulture) : "-";
                AvgText.Text = avg.HasValue ? avg.Value.ToString("G6", CultureInfo.InvariantCulture) : "-";
            }));
        }

        public void ResetStats()
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                MinText.Text = "-";
                MaxText.Text = "-";
                AvgText.Text = "-";
            }));
        }

        // tolerant parser: "2.803433E+01,OK" or "  2.80 V"
        private static bool TryParseFirstDouble(string raw, out double v)
        {
            v = 0;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            var first = raw.Trim().Split(new[] { ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)[0];
            return double.TryParse(first, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }
    }
}
