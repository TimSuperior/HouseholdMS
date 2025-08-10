using HouseholdMS.Model;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace HouseholdMS.View.Measurement
{
    public partial class LivePlotControl : UserControl
    {
        public ObservableCollection<MeasurementPoint> Points { get; } = new ObservableCollection<MeasurementPoint>();

        private CancellationTokenSource _cts;
        private Func<string> _readFunc;

        private double _min = double.MaxValue;
        private double _max = double.MinValue;
        private double _sum = 0;
        private int _count = 0;

        public int IntervalMs { get; set; } = 500;
        public int MaxPoints { get; set; } = 1000;

        public LivePlotControl()
        {
            InitializeComponent();
            LineSeries.ItemsSource = Points;
        }

        public void SetReader(Func<string> reader)
        {
            _readFunc = reader;
        }

        public void Stop()
        {
            if (_cts != null)
            {
                _cts.Cancel();
                _cts.Dispose();
                _cts = null;
            }
        }

        private async void StartPlot_Click(object sender, RoutedEventArgs e)
        {
            if (_cts != null || _readFunc == null)
            {
                MessageBox.Show("Already running or no reader set.");
                return;
            }

            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            ResetStats();
            Points.Clear();

            await Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        var raw = _readFunc.Invoke();

                        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double val))
                        {
                            var point = new MeasurementPoint
                            {
                                Timestamp = DateTime.Now,
                                Value = val
                            };

                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                Points.Add(point);
                                if (Points.Count > MaxPoints)
                                    Points.RemoveAt(0);

                                UpdateStats(val);
                            });
                        }
                    }
                    catch
                    {
                        // Optionally log error
                    }

                    try
                    {
                        await Task.Delay(IntervalMs, token);
                    }
                    catch (TaskCanceledException)
                    {
                        break;
                    }
                }
            });
        }

        private void StopPlot_Click(object sender, RoutedEventArgs e)
        {
            Stop();
        }

        private void ResetStats()
        {
            _min = double.MaxValue;
            _max = double.MinValue;
            _sum = 0;
            _count = 0;

            Application.Current.Dispatcher.Invoke(() =>
            {
                MinText.Text = "-";
                MaxText.Text = "-";
                AvgText.Text = "-";
            });
        }

        private void UpdateStats(double value)
        {
            _min = Math.Min(_min, value);
            _max = Math.Max(_max, value);
            _sum += value;
            _count++;

            MinText.Text = _min.ToString("F2");
            MaxText.Text = _max.ToString("F2");
            AvgText.Text = (_sum / _count).ToString("F2");
        }
    }
}
