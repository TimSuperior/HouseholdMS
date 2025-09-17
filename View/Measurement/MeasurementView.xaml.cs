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
        private CancellationTokenSource _cts;         // continuous read
        private CancellationTokenSource _avgPollCts;  // averaging stats poller
        private int _intervalMs = 500;

        private TextBlock _statusBlockCache;
        private TextBlock _idnBlockCache;

        public MeasurementView()
        {
            InitializeComponent();

            this.Loaded += MeasurementView_Loaded;
            this.Unloaded += (s, e) =>
            {
                StopContinuousIfRunning();
                StopAveragingPoller();
                PlotControl?.Stop();
                DisconnectAndDisposeDevice();
            };
        }

        private void MeasurementView_Loaded(object sender, RoutedEventArgs e)
        {
            _ = RefreshSerialPortListAsync(); // async scan
            InitializeSerialSettings();
            InitFunctionDefaults();
            UpdateIntervalLabel();
            SetStatus("Disconnected.");

            var rateCombo = this.FindName("RateCombo") as ComboBox;
            if (rateCombo != null && rateCombo.SelectedItem == null && rateCombo.Items.Count > 0)
                rateCombo.SelectedIndex = 1; // Medium
        }

        // ---------- UI helpers ----------
        private TextBlock StatusBlockSafe
            => _statusBlockCache ?? (_statusBlockCache = (TextBlock)FindName("StatusBlock"));

        private TextBlock IdnBlockSafe
            => _idnBlockCache ?? (_idnBlockCache = (TextBlock)FindName("IdnBlock"));

        private void SetStatus(string text)
        {
            var tb = StatusBlockSafe;
            if (tb != null) tb.Text = text;
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
        private async Task RefreshSerialPortListAsync()
        {
            if (PortComboBox == null) return;

            // Temporary quick list while scanning
            PortComboBox.ItemsSource = SerialPort.GetPortNames();
            if (PortComboBox.Items.Count > 0) PortComboBox.SelectedIndex = 0;

            try
            {
                SetStatus("Scanning ports…");
                var list = await SerialPortInspector.ProbeAllAsync(600);
                PortComboBox.ItemsSource = list; // PortDescriptor items
                if (PortComboBox.Items.Count > 0) PortComboBox.SelectedIndex = 0;
                SetStatus("Ports updated.");
            }
            catch (Exception ex)
            {
                SetStatus("Port scan error: " + ex.Message);
            }
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
                IntervalSlider.ValueChanged += (s, e) =>
                {
                    UpdateIntervalLabel();
                    TrySetDeviceRateFromUIOrInterval(); // nudge device too
                };
        }

        private void InitFunctionDefaults()
        {
            if (FunctionCombo != null && FunctionCombo.Items.Count > 0)
                FunctionCombo.SelectedIndex = 0; // Voltage DC
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

        private string ExtractPortName(object selected)
        {
            if (selected is PortDescriptor pd) return pd.Port;
            return selected?.ToString();
        }

        private IScpiDevice CreateDevice()
        {
            var selected = PortComboBox?.SelectedItem;
            var port = ExtractPortName(selected);
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
                StopAveragingPoller();
                PlotControl?.Stop();
                PlotControl?.ResetStats();
                PlotControl?.ResetPlot();
                DisconnectAndDisposeDevice();

                _device = CreateDevice();
                await Task.Run(() => _device.Connect());

                var idn = await _device.ReadDeviceIDAsync();
                if (string.IsNullOrWhiteSpace(idn))
                {
                    SetStatus("No response to *IDN?. Check cable/port/power.");
                    DisconnectAndDisposeDevice();
                    return;
                }

                TrySetDeviceRateFromUIOrInterval();

                PlotControl?.SetReader(() =>
                {
                    if (_device != null && _device.IsConnected)
                        return _device.ReadMeasurement();
                    return null;
                });

                if (DeviceNameText != null) DeviceNameText.Text = idn;
                var idnBlock = IdnBlockSafe;
                if (idnBlock != null) idnBlock.Text = "IDN: " + idn;

                SetStatus("Connected.");
            }
            catch (IOException ioex) { SetStatus("IO Error: " + ioex.Message); }
            catch (TimeoutException tex) { SetStatus("Timeout: " + tex.Message); }
            catch (Exception ex) { SetStatus("Connection error: " + ex.Message); }
            finally { SetBusy(false); }
        }

        private void Disconnect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                StopContinuousIfRunning();
                StopAveragingPoller();
                PlotControl?.Stop();
                PlotControl?.ResetStats();
                DisconnectAndDisposeDevice();
                SetStatus("Disconnected.");

                var idn = IdnBlockSafe;
                if (idn != null) idn.Text = string.Empty;
                if (DeviceNameText != null) DeviceNameText.Text = "—";
            }
            catch (Exception ex) { SetStatus("Disconnect error: " + ex.Message); }
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

                if (!string.IsNullOrWhiteSpace(idn) && DeviceNameText != null)
                    DeviceNameText.Text = idn;

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

            string functionCommand = item.Tag.ToString();          // e.g., VOLT:AC
            string functionLabel = item.Content?.ToString() ?? "";

            bool wasContinuous = ContToggle?.IsChecked == true;
            if (wasContinuous)
            {
                ContToggle.IsChecked = false;
                await Task.Delay(80);
            }

            SetBusy(true);
            SetInteractive(false);

            try
            {
                await Task.Run(() => _device.SetFunction(functionCommand));
                string readback = _device.GetFunction();
                SetStatus($"Function set: {functionLabel}" +
                          (string.IsNullOrWhiteSpace(readback) ? "" : $" • Meter: {readback}"));

                if (wasContinuous)
                    ContToggle.IsChecked = true;
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
            catch { }
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
                StartAveragingPoller();
            }
            catch (Exception ex)
            {
                SetStatus("Math error: " + ex.Message);
                if (AvgCheck != null) AvgCheck.IsChecked = false;
                StopAveragingPoller();
                PlotControl?.SetDeviceStats(null, null, null);
            }
            finally { SetBusy(false); }
        }

        private async void AvgCheck_Unchecked(object sender, RoutedEventArgs e)
        {
            if (!EnsureConnected()) { PlotControl?.SetDeviceStats(null, null, null); return; }
            try
            {
                SetBusy(true);
                await Task.Run(() => _device.SetAveraging(false));
                SetStatus("Average math disabled.");
            }
            catch (Exception ex) { SetStatus("Math error: " + ex.Message); }
            finally
            {
                StopAveragingPoller();
                PlotControl?.SetDeviceStats(null, null, null);
                SetBusy(false);
            }
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
                            Dispatcher.BeginInvoke(new Action(() =>
                            {
                                if (MeasurementText != null) MeasurementText.Text = display;
                                UpdateMeasurementFields(resp);
                            }));
                        }
                    }
                    catch { /* non-fatal */ }

                    try { await Task.Delay(Volatile.Read(ref _intervalMs), token); }
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

        private async void RefreshPorts_Click(object sender, RoutedEventArgs e)
        {
            var prevPort = ExtractPortName(PortComboBox?.SelectedItem);
            await RefreshSerialPortListAsync();

            if (!string.IsNullOrWhiteSpace(prevPort) && PortComboBox?.Items != null)
            {
                foreach (var item in PortComboBox.Items)
                {
                    var p = (item as PortDescriptor)?.Port ?? item?.ToString();
                    if (string.Equals(p, prevPort, StringComparison.OrdinalIgnoreCase))
                    {
                        PortComboBox.SelectedItem = item;
                        break;
                    }
                }
            }
        }

        private void RateCombo_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!EnsureConnected()) return;
            TrySetDeviceRateFromUIOrInterval();
        }

        // ---------- Averaging stats poller ----------
        private void StartAveragingPoller()
        {
            StopAveragingPoller();
            _avgPollCts = new CancellationTokenSource();
            var token = _avgPollCts.Token;

            _ = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested && _device != null && _device.IsConnected)
                {
                    try
                    {
                        var stats = _device.TryQueryAveragingAll();
                        if (stats != null)
                        {
                            Dispatcher.BeginInvoke(new Action(() =>
                            {
                                PlotControl?.SetDeviceStats(stats.Min, stats.Max, stats.Avg);
                            }));
                        }
                    }
                    catch { /* keep polling */ }

                    try { await Task.Delay(Math.Max(200, _intervalMs), token); }
                    catch (TaskCanceledException) { break; }
                }
            }, token);
        }

        private void StopAveragingPoller()
        {
            try
            {
                _avgPollCts?.Cancel();
                _avgPollCts?.Dispose();
                _avgPollCts = null;
            }
            catch { }
        }

        // ---------- Formatting / Parsing ----------
        private string FormatMeasurementFromResponse(string resp)
        {
            if (string.IsNullOrWhiteSpace(resp)) return "(no data)";
            var firstToken = resp.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)[0];

            if (double.TryParse(firstToken, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                return FormatEngineering(d);

            return resp;
        }

        private static bool TryParseFirstDouble(string resp, out double d)
        {
            d = 0;
            if (string.IsNullOrWhiteSpace(resp)) return false;
            var first = resp.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)[0];
            return double.TryParse(first, NumberStyles.Float, CultureInfo.InvariantCulture, out d);
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
                    var tag = (cbi.Tag.ToString() ?? "").ToUpperInvariant(); // "F","M","L"
                    rate = (tag == "L") ? 'S' : (!string.IsNullOrEmpty(tag) ? tag[0] : 'M'); // map L→S
                }
                else
                {
                    var ms = Volatile.Read(ref _intervalMs);
                    rate = (ms <= 300) ? 'F' : (ms <= 1000) ? 'M' : 'S';
                }

                _device.SetRate(rate);
            }
            catch
            {
                // Non-fatal if RATE not supported
            }
        }

        private async System.Threading.Tasks.Task<bool> PauseContinuousIfRunningAsync()
        {
            bool wasContinuous = ContToggle?.IsChecked == true;
            if (wasContinuous)
            {
                ContToggle.IsChecked = false;
                await System.Threading.Tasks.Task.Delay(80);
            }
            return wasContinuous;
        }

        private void ResumeContinuousIf(bool shouldResume)
        {
            if (shouldResume && ContToggle != null)
                ContToggle.IsChecked = true;
        }

        private void Remote_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureConnected()) return;
            _device.SetRemote();
            SetStatus("Remote mode requested (SYST:REM).");
        }

        private void Local_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureConnected()) return;
            _device.SetLocal();
            SetStatus("Local mode requested (SYST:LOC).");
        }

        private void QueryRate_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureConnected()) return;
            var r = _device.QueryRate();
            var tb = this.FindName("RateReadbackText") as TextBlock;
            if (tb != null) tb.Text = string.IsNullOrWhiteSpace(r) ? "—" : r.Trim();
            SetStatus("RATE? queried.");
        }

        private async void ApplyTempUnit_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureConnected()) return;
            char u = (TempUnitC?.IsChecked == true) ? 'C' : (TempUnitF?.IsChecked == true) ? 'F' : 'K';
            var was = await PauseContinuousIfRunningAsync();
            try { _device.SetTempUnit(u); SetStatus($"TEMP:RTD:UNIT {u}"); }
            finally { ResumeContinuousIf(was); }
        }

        private void QueryTempUnit_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureConnected()) return;
            var r = _device.QueryTempUnit()?.Trim().ToUpperInvariant();
            if (r?.Contains("C") == true) TempUnitC.IsChecked = true;
            else if (r?.Contains("F") == true) TempUnitF.IsChecked = true;
            else if (r?.Contains("K") == true) TempUnitK.IsChecked = true;
            SetStatus($"TEMP:RTD:UNIT? → {r}");
        }

        private async void ApplyTherKits90_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureConnected()) return;
            var was = await PauseContinuousIfRunningAsync();
            try { _device.ConfigureTempTherKITS90(); SetStatus("CONF:TEMP:THER KITS90 sent."); }
            finally { ResumeContinuousIf(was); }
        }

        private void QueryTempType_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureConnected()) return;
            var r = _device.QueryTempType();
            SetStatus($"TEMP:RTD:TYPE? → {r}");
        }

        private void ReadTempOnce_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureConnected()) return;
            var r = _device.ReadTempOnce();
            if (r != null)
            {
                var display = FormatMeasurementFromResponse(r);
                if (MeasurementText != null) MeasurementText.Text = display;
                UpdateMeasurementFields(r);
            }
            SetStatus("MEAS:TEMP? complete.");
        }

        private async void ApplyVoltRange_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureConnected()) return;

            var mode = ((CurrContent(VoltModeCombo)) ?? "DC").ToUpperInvariant();
            var vSel = CurrContent(VoltRangeV);
            var mvSel = CurrContent(VoltRange_mV);

            var was = await PauseContinuousIfRunningAsync();
            try
            {
                if (!string.IsNullOrWhiteSpace(vSel))
                {
                    if (mode == "DC") _device.ConfVoltDC(vSel);
                    else _device.ConfVoltAC(vSel);
                    SetStatus($"CONF:VOLT:{mode} {vSel}");
                }
                if (!string.IsNullOrWhiteSpace(mvSel))
                {
                    if (mode == "DC") _device.ConfMilliVoltDC(mvSel);
                    else _device.ConfMilliVoltAC(mvSel);
                    SetStatus($"CONF:VOLT:{mode} {mvSel}");
                }
            }
            finally { ResumeContinuousIf(was); }
        }

        private void QueryRange_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureConnected()) return;
            var r = _device.QueryRange();
            SetStatus($"RANGE? → {r}");
        }

        private async void ApplyCurrRangeA_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureConnected()) return;
            var mode = ((CurrContent(CurrModeCombo)) ?? "DC").ToUpperInvariant();
            var aSel = CurrContent(CurrRangeA);
            if (string.IsNullOrWhiteSpace(aSel)) return;

            var was = await PauseContinuousIfRunningAsync();
            try
            {
                if (mode == "DC") _device.ConfCurrDC_Amps(aSel);
                else _device.ConfCurrAC_Amps(aSel);
                SetStatus($"CONF:CURR:{mode} {aSel}");
            }
            finally { ResumeContinuousIf(was); }
        }

        private async void ApplyCurrRange_mA_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureConnected()) return;
            var mode = ((CurrContent(CurrModeCombo)) ?? "DC").ToUpperInvariant();
            var mSel = CurrContent(CurrRange_mA);
            if (string.IsNullOrWhiteSpace(mSel)) return;

            var was = await PauseContinuousIfRunningAsync();
            try
            {
                if (mode == "DC") _device.ConfCurrDC_mA(mSel);
                else _device.ConfCurrAC_mA(mSel);
                SetStatus($"CONF:CURR:{mode} {mSel}");
            }
            finally { ResumeContinuousIf(was); }
        }

        private async void ApplyRes_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureConnected()) return;
            var was = await PauseContinuousIfRunningAsync();
            try { _device.ConfRes(); SetStatus("CONF:RES"); }
            finally { ResumeContinuousIf(was); }
        }

        private async void ApplyCap_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureConnected()) return;
            var sel = CurrContent(CapRangeCombo);
            if (string.IsNullOrWhiteSpace(sel)) return;

            var was = await PauseContinuousIfRunningAsync();
            try { _device.ConfCap(sel); SetStatus($"CONF:CAP {sel}"); }
            finally { ResumeContinuousIf(was); }
        }

        private async void ApplyPer_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureConnected()) return;
            var was = await PauseContinuousIfRunningAsync();
            try { _device.ConfPer(); SetStatus("CONF:PER"); }
            finally { ResumeContinuousIf(was); }
        }

        private void StopAveraging_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureConnected()) return;
            _device.SetAveraging(false);
            if (AvgCheck != null) AvgCheck.IsChecked = false;
            SetStatus("CALC:STAT OFF");
        }

        private void QueryAverAvg_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureConnected()) return;
            var r = _device.QueryAverAvg();
            var tb = this.FindName("AvgValText") as TextBlock;
            if (tb != null) tb.Text = r ?? "—";
            SetStatus("CALC:AVER:AVER?");
        }

        private void QueryAverMin_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureConnected()) return;
            var r = _device.QueryAverMin();
            var tb = this.FindName("MinValText") as TextBlock;
            if (tb != null) tb.Text = r ?? "—";
            SetStatus("CALC:AVER:MIN?");
        }

        private void QueryAverMax_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureConnected()) return;
            var r = _device.QueryAverMax();
            var tb = this.FindName("MaxValText") as TextBlock;
            if (tb != null) tb.Text = r ?? "—";
            SetStatus("CALC:AVER:MAX?");
        }

        private void QueryFunc_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureConnected()) return;
            var f = _device.QueryFunction();
            SetStatus($"FUNC? → {f}");
        }

        private static string CurrContent(ComboBox cb)
            => (cb?.SelectedItem as ComboBoxItem)?.Content?.ToString();
    }
}
