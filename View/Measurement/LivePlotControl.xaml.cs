using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Syncfusion.Licensing;            // licensing
using Syncfusion.UI.Xaml.Charts;       // SfChart, ChartZoomPanBehavior

namespace HouseholdMS.View.Measurement
{
    /// <summary>
    /// Numeric-time live plot (seconds only) using Syncfusion SfChart.
    /// Plotting happens ONLY when Start/Stop is pressed on this control.
    /// Stats chips show device stats (when provided) or live local stats as fallback.
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

        // Local live stats (fallback when device stats not provided recently)
        private double _min = double.PositiveInfinity;
        private double _max = double.NegativeInfinity;
        private double _sum = 0.0;
        private long _count = 0;
        private DateTime _lastDeviceStatsUtc = DateTime.MinValue;

        public LivePlotControl()
        {
            InitializeComponent();
            LineSeries.ItemsSource = Points;

            ResetStats();
            SetupAxisForSeconds();

            // Ensure the chart gets mouse wheel even when inside a ScrollViewer.
            // We listen "handledEventsToo: true" so we can intercept after parent preview handled it,
            // then re-raise a bubbling MouseWheel on the chart to trigger Syncfusion zoom.
            AddHandler(UIElement.PreviewMouseWheelEvent,
                new MouseWheelEventHandler(LivePlotControl_PreviewMouseWheel),
                handledEventsToo: true);

            // Focus chart when pointer enters so keyboard/mouse shortcuts apply.
            Chart.MouseEnter += (s, e) => Chart.Focus();
        }

        // Intercept wheel, forward to the chart (so ZoomPanBehavior receives it), prevent page scroll.
        private void LivePlotControl_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Chart == null) return;

            var args = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
            {
                RoutedEvent = UIElement.MouseWheelEvent,
                Source = Chart
            };
            Chart.RaiseEvent(args);
            e.Handled = true; // stop ScrollViewer from scrolling the page
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
                ResetLocalStats();
                RenderLocalStats(); // show dashes initially
            }));
        }

        private void SetupAxisForSeconds()
        {
            if (TimeAxis == null) return;
            TimeAxis.Header = "Time (s)";
            TimeAxis.LabelFormat = "0";  // 0,1,2,...
            TimeAxis.Interval = 1;
            TimeAxis.Minimum = 0d;
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

                UpdateLocalStatsWith(value);

                // Prefer device stats for ~1.2s after they were provided; otherwise show live local stats.
                if ((DateTime.UtcNow - _lastDeviceStatsUtc).TotalSeconds > 1.2)
                    RenderLocalStats();
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
                _lastDeviceStatsUtc = DateTime.UtcNow;
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

        // ---------- Public zoom API for parent view ----------
        public void ZoomIn() => RaiseSyntheticWheel(+120);  // one notch in
        public void ZoomOut() => RaiseSyntheticWheel(-120);  // one notch out

        public void ResetView()
        {
            try
            {
                // Preferred: use Syncfusion behavior's reset if available
                ZoomPan?.Reset();
            }
            catch
            {
                // Fallback: recreate behavior (lightweight) and restore axis header & interval.
                try
                {
                    if (Chart != null && ZoomPan != null)
                    {
                        int idx = Chart.Behaviors.IndexOf(ZoomPan);
                        if (idx >= 0)
                        {
                            Chart.Behaviors.RemoveAt(idx);
                            var fresh = new ChartZoomPanBehavior
                            {
                                EnableMouseWheelZooming = true,
                                EnablePanning = true,
                                ZoomMode = ZoomMode.X
                            };
                            Chart.Behaviors.Insert(idx, fresh);
                            // rebind x:Name pointer
                            // Note: x:Name-generated field won't update; but we only need behavior present.
                        }
                    }
                }
                catch { /* ignore */ }
            }

            // Also reset the X-axis labeling back to seconds baseline.
            SetupAxisForSeconds();
        }

        // ---------- Helpers ----------
        private void RaiseSyntheticWheel(int delta)
        {
            if (Chart == null) return;

            // Ensure chart owns focus so zoom behavior is active
            if (!Chart.IsKeyboardFocusWithin) Chart.Focus();

            var args = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta)
            {
                RoutedEvent = UIElement.MouseWheelEvent,
                Source = Chart
            };
            Chart.RaiseEvent(args);
        }

        // tolerant parser: "2.803433E+01,OK" or "  2.80 V"
        private static bool TryParseFirstDouble(string raw, out double v)
        {
            v = 0;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            var first = raw.Trim().Split(new[] { ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)[0];
            return double.TryParse(first, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }

        private void ResetLocalStats()
        {
            _min = double.PositiveInfinity;
            _max = double.NegativeInfinity;
            _sum = 0.0;
            _count = 0;
        }

        private void UpdateLocalStatsWith(double value)
        {
            if (value < _min) _min = value;
            if (value > _max) _max = value;
            _sum += value;
            _count++;
        }

        private void RenderLocalStats()
        {
            if (_count <= 0)
            {
                MinText.Text = "-";
                MaxText.Text = "-";
                AvgText.Text = "-";
                return;
            }

            MinText.Text = double.IsPositiveInfinity(_min) ? "-" : _min.ToString("G6", CultureInfo.InvariantCulture);
            MaxText.Text = double.IsNegativeInfinity(_max) ? "-" : _max.ToString("G6", CultureInfo.InvariantCulture);
            AvgText.Text = (_sum / Math.Max(1, _count)).ToString("G6", CultureInfo.InvariantCulture);
        }
    }
}
