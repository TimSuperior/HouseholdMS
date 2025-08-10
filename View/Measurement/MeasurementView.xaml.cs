using HouseholdMS.Services;
using System;
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

        public MeasurementView()
        {
            InitializeComponent();
            RefreshSerialPortList();
            InitializeSerialSettings();
            InitFunctionDefaults();
            UpdateIntervalLabel();
            SetStatus("Disconnected.");
        }

        // ---------- UI helpers ----------
        private void SetStatus(string text) { StatusBlock.Text = text; }
        private void SetBusy(bool on) { BusyBar.Visibility = on ? Visibility.Visible : Visibility.Collapsed; }

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
            PortComboBox.ItemsSource = SerialPort.GetPortNames();
            if (PortComboBox.Items.Count > 0) PortComboBox.SelectedIndex = 0;
        }

        private void InitializeSerialSettings()
        {
            BaudComboBox.ItemsSource = new int[] { 9600, 19200, 38400, 57600, 115200 };
            BaudComboBox.SelectedIndex = 0;
            ParityComboBox.ItemsSource = Enum.GetNames(typeof(Parity));
            ParityComboBox.SelectedIndex = 0;
            DataBitsComboBox.ItemsSource = new int[] { 7, 8 };
            DataBitsComboBox.SelectedIndex = 1;
            IntervalSlider.ValueChanged += (s, e) => UpdateIntervalLabel();
        }

        private void InitFunctionDefaults()
        {
            FunctionCombo.SelectedIndex = 0; // Default: Voltage DC
        }

        private void UpdateIntervalLabel()
        {
            _intervalMs = (int)IntervalSlider.Value;
            IntervalLabel.Text = _intervalMs + " ms";
            if (PlotControl != null)
                PlotControl.IntervalMs = _intervalMs;
        }

        private IScpiDevice CreateDevice()
        {
            var port = PortComboBox.SelectedItem?.ToString();
            var baud = Convert.ToInt32(BaudComboBox.SelectedItem);
            var parity = (Parity)Enum.Parse(typeof(Parity), ParityComboBox.SelectedItem.ToString());
            var dataBits = Convert.ToInt32(DataBitsComboBox.SelectedItem);
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

                SetStatus("Connected.");

                PlotControl.SetReader(() =>
                {
                    if (_device != null && _device.IsConnected)
                        return _device.ReadMeasurement();
                    return null;
                });
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
                IdnBlock.Text = string.Empty;
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
                var idn = await Task.Run(_device.ReadDeviceID);
                IdnBlock.Text = "IDN: " + (string.IsNullOrWhiteSpace(idn) ? "(empty)" : idn);
                SetStatus("IDN received.");
            }
            catch (TimeoutException) { SetStatus("IDN timeout."); }
            catch (Exception ex) { SetStatus("Error: " + ex.Message); }
            finally { SetBusy(false); }
        }

        private async void ApplyFunction_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureConnected()) return;

            var item = FunctionCombo.SelectedItem as ComboBoxItem;
            if (item?.Tag == null) return;

            string functionCommand = item.Tag.ToString();              // ✅ safe copy for background thread
            string functionLabel = item.Content?.ToString() ?? "";     // ✅ safe copy for UI update later

            SetBusy(true);

            try
            {
                await Task.Run(() =>
                {
                    _device.SetFunction(functionCommand);
                });

                SetStatus("Function set: " + functionLabel);  // ✅ this runs on UI thread
            }
            catch (TimeoutException)
            {
                SetStatus("Timeout while setting function.");
            }
            catch (Exception ex)
            {
                SetStatus("Error: " + ex.Message);
            }
            finally
            {
                SetBusy(false);
            }
        }



        private async void ReadButton_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureConnected()) return;
            try
            {
                SetBusy(true);
                string value = await Task.Run(_device.ReadMeasurement);
                if (value != null)
                {
                    MeasurementText.Text = FormatMeasurement(value);
                    UpdateMeasurementFields(value);
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
                await Task.Run(() =>
                {
                    _device.Write("CALCulate:FUNCtion AVERage");
                    _device.Write("CALCulate:STATe ON");
                });
                SetStatus("Average math enabled.");
            }
            catch (Exception ex)
            {
                SetStatus("Math error: " + ex.Message);
                AvgCheck.IsChecked = false;
            }
            finally { SetBusy(false); }
        }

        private async void AvgCheck_Unchecked(object sender, RoutedEventArgs e)
        {
            if (!EnsureConnected()) return;
            try
            {
                SetBusy(true);
                await Task.Run(() => _device.Write("CALCulate:STATe OFF"));
                SetStatus("Average math disabled.");
            }
            catch (Exception ex) { SetStatus("Math error: " + ex.Message); }
            finally { SetBusy(false); }
        }

        private async void ContToggle_Checked(object sender, RoutedEventArgs e)
        {
            if (!EnsureConnected())
            {
                ContToggle.IsChecked = false;
                return;
            }

            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            SetStatus("Continuous reading started.");

            await Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        string value = _device.ReadMeasurement();
                        if (value != null)
                        {
                            Dispatcher.Invoke(() =>
                            {
                                MeasurementText.Text = FormatMeasurement(value);
                                UpdateMeasurementFields(value);
                            });
                        }
                    }
                    catch { }

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
            var current = PortComboBox.SelectedItem;
            RefreshSerialPortList();
            if (current != null && PortComboBox.Items.Contains(current))
                PortComboBox.SelectedItem = current;
        }

        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);
            this.Unloaded += delegate
            {
                StopContinuousIfRunning();
                PlotControl?.Stop();
                DisconnectAndDisposeDevice();
            };
        }

        // ---------- Formatting ----------
        private string FormatMeasurement(string value)
        {
            if (double.TryParse(value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double d))
            {
                double abs = Math.Abs(d);
                if (abs == 0) return "0";
                if (abs < 1e-6) return (d * 1e9).ToString("F2") + " n";
                if (abs < 1e-3) return (d * 1e6).ToString("F2") + " µ";
                if (abs < 1) return (d * 1e3).ToString("F2") + " m";
                if (abs < 1e3) return d.ToString("F5");
                if (abs < 1e6) return (d / 1e3).ToString("F2") + " k";
                return (d / 1e6).ToString("F2") + " M";
            }
            return value;
        }

        private void UpdateMeasurementFields(string value)
        {
            // Placeholder: in future, parse multivalue response for real values
            VoltageText.Text = "-";
            CurrentText.Text = "-";
            ResistanceText.Text = "-";
        }
    }
}
