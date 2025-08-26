using HouseholdMS.Services;
using System;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace HouseholdMS.View.Measurement
{
    public partial class MeasurementView : UserControl
    {
        private IScpiDevice _device;
        private CancellationTokenSource _cts;
        private int _intervalMs = 500;

        // lazy caches in case generated fields are temporarily null
        private TextBlock _statusBlockCache;
        private TextBlock _idnBlockCache;

        public MeasurementView()
        {
            InitializeComponent();

            // Do UI-dependent initialization after the visual tree is ready
            this.Loaded += MeasurementView_Loaded;

            // Clean up when leaving
            this.Unloaded += (s, e) =>
            {
                StopContinuousIfRunning();
                PlotControl?.Stop();
                DisconnectAndDisposeDevice();
            };
        }

        private void MeasurementView_Loaded(object sender, RoutedEventArgs e)
        {
            // Now it’s safe to touch named elements
            RefreshSerialPortList();
            InitializeSerialSettings();
            InitFunctionDefaults();
            UpdateIntervalLabel();
            SetStatus("Disconnected.");

            // Optional default rate (if you added RateCombo in XAML)
            var rateCombo = this.FindName("RateCombo") as ComboBox;
            if (rateCombo != null)
            {
                if (rateCombo.SelectedItem == null && rateCombo.Items.Count > 0)
                    rateCombo.SelectedIndex = 1; // Medium
            }
        }

        // ---------- UI helpers ----------
        private TextBlock StatusBlockSafe
            => _statusBlockCache ?? (_statusBlockCache = (TextBlock)FindName("StatusBlock"));

        private TextBlock IdnBlockSafe
            => _idnBlockCache ?? (_idnBlockCache = (TextBlock)FindName("IdnBlock"));

        private void SetStatus(string text)
        {
            var tb = StatusBlockSafe;
            if (tb != null) tb.Text = text; // avoid NRE even if called early
        }

        private void SetBusy(bool on)
        {
            if (BusyBar != null)
                BusyBar.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        }

        private bool EnsureConnected()
        {
            if (_device == null || !_device.IsConnected)
            {
                SetStatus("Device is not connected.");
                return false;
            }
            return true;
        }

        // ---------- Ports / serial ----------
        private void RefreshSerialPortList()
        {
            if (PortComboBox == null) return;
            PortComboBox.ItemsSource = SerialPort.GetPortNames();
            if (PortComboBox.Items.Count > 0) PortComboBox.SelectedIndex = 0;
        }

        private void InitializeSerialSettings()
        {
            if (BaudComboBox != null)
            {
                BaudComboBox.ItemsSource = new int[] { 9600, 19200, 38400, 57600, 115200 };
                BaudComboBox.SelectedIndex = 0;
            }

            if (ParityComboBox != null)
            {
                ParityComboBox.ItemsSource = Enum.GetNames(typeof(Parity));
                ParityComboBox.SelectedIndex = 0;
            }

            if (DataBitsComboBox != null)
            {
                DataBitsComboBox.ItemsSource = new int[] { 7, 8 };
                DataBitsComboBox.SelectedIndex = 1;
            }

            if (IntervalSlider != null)
                IntervalSlider.ValueChanged += (s, e) => UpdateIntervalLabel();
        }

        private void InitFunctionDefaults()
        {
            if (FunctionCombo != null && FunctionCombo.Items.Count > 0)
                FunctionCombo.SelectedIndex = 0; // Default: Voltage DC
        }

        private void UpdateIntervalLabel()
        {
            if (IntervalSlider != null)
                _intervalMs = (int)IntervalSlider.Value;

            if (IntervalLabel != null)
                IntervalLabel.Text = _intervalMs + " ms";

            if (PlotControl != null)
                PlotControl.IntervalMs = _intervalMs;
        }

        private IScpiDevice CreateDevice()
        {
            var port = PortComboBox?.SelectedItem?.ToString();
            var baud = Convert.ToInt32(BaudComboBox?.SelectedItem ?? 9600);
            var parity = (Parity)Enum.Parse(typeof(Parity), ParityComboBox?.SelectedItem?.ToString() ?? "None");
            var dataBits = Convert.ToInt32(DataBitsComboBox?.SelectedItem ?? 8);
            return new ScpiDevice(port, baud, parity, dataBits);
        }

        // ---------- Events ----------
        private async void Connect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SetBusy(true);
                StopContinuousIfRunning();
                PlotControl?.Stop();
                DisconnectAndDisposeDevice();

                _device = CreateDevice();
                await Task.Run(() => _device.Connect());

                // Apply a reasonable default device-side rate
                TrySetDeviceRateFromUIOrInterval();

                // Wire the plot to use the device reader
                PlotControl?.SetReader(() =>
                {
                    if (_device != null && _device.IsConnected)
                        return _device.ReadMeasurement();
                    return null;
                });

                SetStatus("Connected.");
            }
            catch (IOException ioex)
            {
                SetStatus("IO Error: " + ioex.Message);
            }
            catch (TimeoutException tex)
            {
                SetStatus("Timeout: " + tex.Message);
            }
            catch (Exception ex)
            {
                SetStatus("Connection error: " + ex.Message);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void Disconnect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                StopContinuousIfRunning();
                PlotControl?.Stop();
                DisconnectAndDisposeDevice();
                SetStatus("Disconnected.");

                var idn = IdnBlockSafe;
                if (idn != null) idn.Text = string.Empty;
            }
            catch (Exception ex)
            {
                SetStatus("Disconnect error: " + ex.Message);
            }
        }

        private void DisconnectAndDisposeDevice()
        {
            try
            {
                _device?.Disconnect();
                _device?.Dispose();
                _device = null;
            }
            catch { }
        }

        private async void ReadIDN_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureConnected()) return;
            try
            {
                SetBusy(true);
                var idn = await _device.ReadDeviceIDAsync();

                var idnBlock = IdnBlockSafe;
                if (idnBlock != null)
                    idnBlock.Text = "IDN: " + (string.IsNullOrWhiteSpace(idn) ? "(empty)" : idn);

                SetStatus("IDN received.");
            }
            catch (TimeoutException) { SetStatus("IDN timeout."); }
            catch (Exception ex) { SetStatus("Error: " + ex.Message); }
            finally { SetBusy(false); }
        }

        private async void ApplyFunction_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureConnected()) return;

            var item = FunctionCombo?.SelectedItem as ComboBoxItem;
            if (item?.Tag == null) return;

            string functionCommand = item.Tag.ToString();          // e.g., VOLT:AC / CURR:DC / TEMP
            string functionLabel = item.Content?.ToString() ?? "";

            // 1) Pause Continuous to avoid serial contention
            bool wasContinuous = ContToggle?.IsChecked == true;
            if (wasContinuous)
            {
                ContToggle.IsChecked = false;   // triggers StopContinuousIfRunning()
                await Task.Delay(80);           // tiny grace so the worker loop exits
            }

            SetBusy(true);
            SetInteractive(false);

            try
            {
                // 2) Set function (fast confirmation lives in ScpiDevice now)
                await Task.Run(() => _device.SetFunction(functionCommand));

                // 3) Optional: quick readback for UI
                string readback = _device.GetFunction();
                SetStatus($"Function set: {functionLabel}" +
                          (string.IsNullOrWhiteSpace(readback) ? "" : $" • Meter: {readback}"));

                // 4) Restore Continuous if it was on
                if (wasContinuous)
                    ContToggle.IsChecked = true; // starts again
            }
            catch (TimeoutException) { SetStatus("Timeout while setting function."); }
            catch (Exception ex) { SetStatus("Error: " + ex.Message); }
            finally
            {
                SetInteractive(true);
                SetBusy(false);
            }
        }

        private void SetInteractive(bool enabled)
        {
            try
            {
                FunctionCombo.IsEnabled = enabled;
                AvgCheck.IsEnabled = enabled;
                ContToggle.IsEnabled = enabled;
                BaudComboBox.IsEnabled = enabled;
                ParityComboBox.IsEnabled = enabled;
                DataBitsComboBox.IsEnabled = enabled;
                RateCombo.IsEnabled = enabled;
                IntervalSlider.IsEnabled = enabled;
            }
            catch { /* ignore if any control is null in templates */ }
        }

        private async void ReadButton_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureConnected()) return;
            try
            {
                SetBusy(true);
                string resp = await _device.ReadMeasurementAsync();
                if (resp != null)
                {
                    var display = FormatMeasurementFromResponse(resp);
                    if (MeasurementText != null) MeasurementText.Text = display;
                    UpdateMeasurementFields(resp);
                    SetStatus("Measurement complete.");
                }
                else
                {
                    SetStatus("No data from device.");
                }
            }
            catch (TimeoutException) { SetStatus("Read timeout."); }
            catch (Exception ex) { SetStatus("Error: " + ex.Message); }
            finally { SetBusy(false); }
        }

        private async void AvgCheck_Checked(object sender, RoutedEventArgs e)
        {
            if (!EnsureConnected()) return;
            try
            {
                SetBusy(true);
                await Task.Run(() => _device.SetAveraging(true));
                SetStatus("Average math enabled.");
            }
            catch (Exception ex)
            {
                SetStatus("Math error: " + ex.Message);
                if (AvgCheck != null) AvgCheck.IsChecked = false;
            }
            finally { SetBusy(false); }
        }

        private async void AvgCheck_Unchecked(object sender, RoutedEventArgs e)
        {
            if (!EnsureConnected()) return;
            try
            {
                SetBusy(true);
                await Task.Run(() => _device.SetAveraging(false));
                SetStatus("Average math disabled.");
            }
            catch (Exception ex) { SetStatus("Math error: " + ex.Message); }
            finally { SetBusy(false); }
        }

        private async void ContToggle_Checked(object sender, RoutedEventArgs e)
        {
            if (!EnsureConnected())
            {
                if (ContToggle != null) ContToggle.IsChecked = false;
                return;
            }

            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            SetStatus("Continuous reading started.");

            TrySetDeviceRateFromUIOrInterval();

            await Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        string resp = _device.ReadMeasurement();
                        if (resp != null)
                        {
                            string display = FormatMeasurementFromResponse(resp);
                            Dispatcher.Invoke(() =>
                            {
                                if (MeasurementText != null) MeasurementText.Text = display;
                                UpdateMeasurementFields(resp);
                            });
                        }
                    }
                    catch
                    {
                        // Non-fatal during continuous mode
                    }

                    try
                    {
                        await Task.Delay(Volatile.Read(ref _intervalMs), token);
                    }
                    catch (TaskCanceledException) { break; }
                }
            });
        }

        private void ContToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            StopContinuousIfRunning();
            SetStatus("Continuous reading stopped.");
        }

        private void StopContinuousIfRunning()
        {
            try
            {
                _cts?.Cancel();
                _cts?.Dispose();
                _cts = null;
            }
            catch { }
        }

        private void RefreshPorts_Click(object sender, RoutedEventArgs e)
        {
            var current = PortComboBox?.SelectedItem;
            RefreshSerialPortList();
            if (current != null && PortComboBox != null && PortComboBox.Items.Contains(current))
                PortComboBox.SelectedItem = current;
        }

        // Optional: if you added RateCombo in XAML (SelectionChanged="RateCombo_Changed")
        private void RateCombo_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!EnsureConnected()) return;
            TrySetDeviceRateFromUIOrInterval();
        }

        // ---------- Formatting / Parsing ----------
        private string FormatMeasurementFromResponse(string resp)
        {
            if (string.IsNullOrWhiteSpace(resp)) return "(no data)";

            var firstToken = resp.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)[0];

            double d;
            if (double.TryParse(firstToken, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                return FormatEngineering(d);

            return resp; // fallback
        }

        private string FormatEngineering(double d)
        {
            double abs = Math.Abs(d);
            if (abs == 0) return "0";
            if (abs < 1e-9) return (d * 1e12).ToString("F2", CultureInfo.InvariantCulture) + " p";
            if (abs < 1e-6) return (d * 1e9).ToString("F2", CultureInfo.InvariantCulture) + " n";
            if (abs < 1e-3) return (d * 1e6).ToString("F2", CultureInfo.InvariantCulture) + " µ";
            if (abs < 1) return (d * 1e3).ToString("F2", CultureInfo.InvariantCulture) + " m";
            if (abs < 1e3) return d.ToString("G6", CultureInfo.InvariantCulture);
            if (abs < 1e6) return (d / 1e3).ToString("F2", CultureInfo.InvariantCulture) + " k";
            if (abs < 1e9) return (d / 1e6).ToString("F2", CultureInfo.InvariantCulture) + " M";
            return (d / 1e9).ToString("F2", CultureInfo.InvariantCulture) + " G";
        }

        private void UpdateMeasurementFields(string resp)
        {
            // TODO: parse sub-values by function; for now, keep placeholders
            if (VoltageText != null) VoltageText.Text = "-";
            if (CurrentText != null) CurrentText.Text = "-";
            if (ResistanceText != null) ResistanceText.Text = "-";
        }

        // ---------- Device-rate helpers ----------
        private void TrySetDeviceRateFromUIOrInterval()
        {
            try
            {
                if (_device == null || !_device.IsConnected) return;

                char rate;

                var rateCombo = this.FindName("RateCombo") as ComboBox;
                if (rateCombo != null && rateCombo.SelectedItem is ComboBoxItem cbi && cbi.Tag != null)
                {
                    var tag = cbi.Tag.ToString(); // "F","M","L"
                    rate = (!string.IsNullOrEmpty(tag)) ? char.ToUpperInvariant(tag[0]) : 'M';
                }
                else
                {
                    // Fallback: map from interval
                    var ms = Volatile.Read(ref _intervalMs);
                    rate = (ms <= 300) ? 'F' : (ms <= 1000) ? 'M' : 'L';
                }

                _device.SetRate(rate);
            }
            catch
            {
                // Non-fatal if RATE not supported
            }
        }
    }
}
