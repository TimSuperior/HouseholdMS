using HouseholdMS.Services;
using HouseholdMS.Resources; // <-- for Strings.*
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading; // for Dispatcher.InvokeAsync

namespace HouseholdMS.View.Measurement
{
    public partial class MeasurementView : UserControl
    {
        private IScpiDevice _device;
        private CancellationTokenSource _cts;         // continuous read
        private CancellationTokenSource _avgPollCts;  // averaging stats poller
        private CancellationTokenSource _portScanCts; // cancel port scan when navigating away
        private int _intervalMs = 500;

        private TextBlock _statusBlockCache;
        private TextBlock _idnBlockCache;

        // NEW: state for averaging toggle
        private bool _mathEnabled = false;

        // SPS calculation
        private readonly Queue<DateTime> _spsWindow = new Queue<DateTime>(capacity: 100);

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
                try { _portScanCts?.Cancel(); _portScanCts?.Dispose(); _portScanCts = null; } catch { }
            };
        }

        private async void MeasurementView_Loaded(object sender, RoutedEventArgs e)
        {
            // Prevent ScrollViewer from moving when zooming with wheel over plot
            if (PlotControl != null)
            {
                PlotControl.AddHandler(UIElement.MouseWheelEvent,
                    new MouseWheelEventHandler(PlotArea_MouseWheelHandled),
                    /*handledEventsToo*/ true);
            }

            await RefreshSerialPortListAsyncCached();
            InitializeSerialSettings();
            InitFunctionDefaults();
            UpdateIntervalLabel();
            SetStatus(Strings.MEAS_Status_Disconnected);

            var rateCombo = this.FindName("RateCombo") as ComboBox;
            if (rateCombo != null && rateCombo.SelectedItem == null && rateCombo.Items.Count > 0)
                rateCombo.SelectedIndex = 1; // Medium

            // Ensure toggle label is correct on load
            UpdateMathToggleUI();
        }

        private void PlotArea_MouseWheelHandled(object sender, MouseWheelEventArgs e)
        {
            e.Handled = true;
        }

        // ---------- UI helpers ----------
        private TextBlock StatusBlockSafe => _statusBlockCache ?? (_statusBlockCache = (TextBlock)FindName("StatusBlock"));
        private TextBlock IdnBlockSafe => _idnBlockCache ?? (_idnBlockCache = (TextBlock)FindName("IdnBlock"));
        private void SetStatus(string text) { var tb = StatusBlockSafe; if (tb != null) tb.Text = text; }
        private void SetBusy(bool on) { if (BusyBar != null) BusyBar.Visibility = on ? Visibility.Visible : Visibility.Collapsed; }

        private bool EnsureConnected()
        {
            if (_device == null || !_device.IsConnected)
            {
                SetStatus(Strings.MEAS_Status_NotConnected);
                return false;
            }
            return true;
        }

        // ---------- Ports / serial ----------
        private async Task RefreshSerialPortListAsyncCached()
        {
            if (PortComboBox == null) return;

            PortComboBox.ItemsSource = SerialPort.GetPortNames();
            if (PortComboBox.Items.Count > 0) PortComboBox.SelectedIndex = 0;

            try { _portScanCts?.Cancel(); _portScanCts?.Dispose(); } catch { }
            _portScanCts = new CancellationTokenSource();

            try
            {
                SetStatus(Strings.MEAS_Status_ScanningPorts);
                var list = await SerialPortInspector.GetOrProbeAsync(
                    attemptTimeoutMs: 200,
                    cacheTtlMs: 15000,
                    ct: _portScanCts.Token);

                PortComboBox.ItemsSource = list;
                if (PortComboBox.Items.Count > 0) PortComboBox.SelectedIndex = 0;
                SetStatus(Strings.MEAS_Status_PortsUpdated);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                SetStatus(string.Format(CultureInfo.CurrentCulture, Strings.MEAS_Status_PortScanErrorFmt, ex.Message));
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
                    TrySetDeviceRateFromUIOrInterval();
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
                    SetStatus(Strings.MEAS_Status_NoIdn);
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

                if (DeviceNameText != null) DeviceNameText.Text = idn.Trim();
                var idnBlock = IdnBlockSafe; if (idnBlock != null) idnBlock.Text = "IDN: " + idn.Trim();

                // Reset math state on fresh connection
                _mathEnabled = false;
                UpdateMathToggleUI();

                SetStatus(Strings.MEAS_Status_Connected);
            }
            catch (TimeoutException tex) { SetStatus(string.Format(CultureInfo.CurrentCulture, Strings.MEAS_Status_TimeoutFmt, tex.Message)); }
            catch (Exception ex) { SetStatus(string.Format(CultureInfo.CurrentCulture, Strings.MEAS_Status_ConnectionErrorFmt, ex.Message)); }
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
                SetStatus(Strings.MEAS_Status_Disconnected);

                var idn = IdnBlockSafe; if (idn != null) idn.Text = string.Empty;
                if (DeviceNameText != null) DeviceNameText.Text = "—";

                // Reset math UI
                _mathEnabled = false;
                ClearStatsTexts();
                UpdateMathToggleUI();
            }
            catch (Exception ex) { SetStatus(string.Format(CultureInfo.CurrentCulture, Strings.MEAS_Status_DisconnectErrorFmt, ex.Message)); }
        }

        private void DisconnectAndDisposeDevice()
        {
            try { _device?.Disconnect(); _device?.Dispose(); _device = null; } catch { }
        }

        private async void ReadIDN_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureConnected()) return;
            try
            {
                SetBusy(true);
                var idn = await _device.ReadDeviceIDAsync();

                var idnBlock = IdnBlockSafe;
                if (idnBlock != null) idnBlock.Text = "IDN: " + (string.IsNullOrWhiteSpace(idn) ? "(empty)" : idn.Trim());
                if (!string.IsNullOrWhiteSpace(idn) && DeviceNameText != null) DeviceNameText.Text = idn.Trim();

                SetStatus(Strings.MEAS_Status_IdnReceived);
            }
            catch (TimeoutException) { SetStatus(Strings.MEAS_Status_IdnTimeout); }
            catch (Exception ex) { SetStatus(string.Format(CultureInfo.CurrentCulture, Strings.MEAS_Status_ErrorPrefixFmt, ex.Message)); }
            finally { SetBusy(false); }
        }

        private async void ApplyFunction_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureConnected()) return;

            var item = FunctionCombo?.SelectedItem as ComboBoxItem;
            if (item?.Tag == null) return;

            string functionCommand = item.Tag.ToString();
            string functionLabel = item.Content?.ToString() ?? "";

            bool wasContinuous = ContToggle?.IsChecked == true;
            if (wasContinuous) { ContToggle.IsChecked = false; await Task.Delay(80); }

            SetBusy(true);
            SetInteractive(false);

            try
            {
                await Task.Run(() => _device.SetFunction(functionCommand));
                string readback = _device.GetFunction();
                string msg = string.IsNullOrWhiteSpace(readback)
                    ? string.Format(CultureInfo.CurrentCulture, Strings.MEAS_Status_FunctionSetFmt, functionLabel)
                    : string.Format(CultureInfo.CurrentCulture, Strings.MEAS_Status_FunctionSetWithMeterFmt, functionLabel, readback);
                SetStatus(msg);
                UnitLabel.Text = InferUnitFromFunction(functionCommand);
                if (wasContinuous) ContToggle.IsChecked = true;
            }
            catch (TimeoutException) { SetStatus(Strings.MEAS_Status_FunctionTimeout); }
            catch (Exception ex) { SetStatus(string.Format(CultureInfo.CurrentCulture, Strings.MEAS_Status_ErrorPrefixFmt, ex.Message)); }
            finally { SetInteractive(true); SetBusy(false); }
        }

        private string InferUnitFromFunction(string f)
        {
            f = (f ?? "").ToUpperInvariant();
            if (f.StartsWith("VOLT")) return "V";
            if (f.StartsWith("CURR")) return "A";
            if (f == "RES" || f == "FRES") return "Ω";
            if (f == "CAP") return "F";
            if (f == "FREQ") return "Hz";
            if (f == "PER") return "s";
            if (f == "TEMP") return "°";
            if (f == "CONT") return "Ω";
            if (f == "DIOD") return "V";
            return "";
        }

        private void SetInteractive(bool enabled)
        {
            try
            {
                FunctionCombo.IsEnabled = enabled;
                MathToggleBtn.IsEnabled = enabled;
                ContToggle.IsEnabled = enabled;
                BaudComboBox.IsEnabled = enabled;
                ParityComboBox.IsEnabled = enabled;
                DataBitsComboBox.IsEnabled = enabled;
                RateCombo.IsEnabled = enabled;
                IntervalSlider.IsEnabled = enabled;
                AutoRangeCheck.IsEnabled = enabled;
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
                    HandleIncomingMeasurement(resp, DateTime.UtcNow);
                    SetStatus(Strings.MEAS_Status_ReadDone);
                }
                else SetStatus(Strings.MEAS_Status_NoData);
            }
            catch (TimeoutException) { SetStatus(Strings.MEAS_Status_ReadTimeout); }
            catch (Exception ex) { SetStatus(string.Format(CultureInfo.CurrentCulture, Strings.MEAS_Status_ErrorPrefixFmt, ex.Message)); }
            finally { SetBusy(false); }
        }

        // NEW: Single toggle handler for math/averaging
        private async void MathToggleBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureConnected()) return;

            try
            {
                SetBusy(true);

                if (!_mathEnabled)
                {
                    await Task.Run(() => _device.SetAveraging(true));
                    _mathEnabled = true;
                    StartAveragingPoller();
                    SetStatus(Strings.MEAS_Status_MathEnabled);
                }
                else
                {
                    await Task.Run(() => _device.SetAveraging(false));
                    _mathEnabled = false;
                    StopAveragingPoller();
                    PlotControl?.SetDeviceStats(null, null, null);
                    ClearStatsTexts();
                    SetStatus(Strings.MEAS_Status_MathDisabled);
                }

                UpdateMathToggleUI();
            }
            catch (Exception ex)
            {
                SetStatus(string.Format(CultureInfo.CurrentCulture, Strings.MEAS_Status_MathErrorFmt, ex.Message));
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void UpdateMathToggleUI()
        {
            if (MathToggleBtn == null) return;
            MathToggleBtn.Content = _mathEnabled ? Strings.MEAS_Math_Disable : Strings.MEAS_Math_Enable;
        }

        private void ClearStatsTexts()
        {
            if (AvgValText != null) AvgValText.Text = "";
            if (MinValText != null) MinValText.Text = "";
            if (MaxValText != null) MaxValText.Text = "";
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
            SetStatus(Strings.MEAS_Status_ContStart);

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
                            var ts = DateTime.UtcNow;

                            // Await UI dispatch (no CS4014)
                            await Dispatcher.InvokeAsync(() =>
                            {
                                HandleIncomingMeasurement(resp, ts);
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        _device?.WatchdogBump(ex);
                    }

                    try { await Task.Delay(Volatile.Read(ref _intervalMs), token); }
                    catch (TaskCanceledException) { break; }
                }
            });
        }

        private void ContToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            StopContinuousIfRunning();
            SetStatus(Strings.MEAS_Status_ContStop);
        }

        private void StopContinuousIfRunning()
        {
            try { _cts?.Cancel(); _cts?.Dispose(); _cts = null; } catch { }
        }

        private async void RefreshPorts_Click(object sender, RoutedEventArgs e)
        {
            var prev = ExtractPortName(PortComboBox?.SelectedItem);
            SerialPortInspector.InvalidateCache();
            await RefreshSerialPortListAsyncCached();

            if (!string.IsNullOrWhiteSpace(prev) && PortComboBox?.Items != null)
            {
                foreach (var item in PortComboBox.Items)
                {
                    var p = (item as PortDescriptor)?.Port ?? item?.ToString();
                    if (string.Equals(p, prev, StringComparison.OrdinalIgnoreCase))
                    { PortComboBox.SelectedItem = item; break; }
                }
            }
        }

        private void RateCombo_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!EnsureConnected()) return;
            TrySetDeviceRateFromUIOrInterval();
        }

        // ---------- Averaging poller ----------
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
                            // Await UI dispatch
                            await Dispatcher.InvokeAsync(() =>
                            {
                                PlotControl?.SetDeviceStats(stats.Min, stats.Max, stats.Avg);
                                AvgValText.Text = DoubleToStr(stats.Avg);
                                MinValText.Text = DoubleToStr(stats.Min);
                                MaxValText.Text = DoubleToStr(stats.Max);
                            });
                        }
                    }
                    catch { }

                    try { await Task.Delay(Math.Max(200, _intervalMs), token); }
                    catch (TaskCanceledException) { break; }
                }
            }, token);
        }

        private void StopAveragingPoller()
        {
            try { _avgPollCts?.Cancel(); _avgPollCts?.Dispose(); _avgPollCts = null; } catch { }
        }

        // ---------- Formatting ----------
        private string FormatMeasurementFromResponse(string resp, out double? value)
        {
            value = null;
            if (string.IsNullOrWhiteSpace(resp)) return "(no data)";
            var firstToken = resp.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)[0];
            if (double.TryParse(firstToken, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
            {
                value = d;
                return FormatEngineering(d);
            }
            return resp;
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

        private string DoubleToStr(double v) => v.ToString("G6", CultureInfo.InvariantCulture);

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
                    rate = (tag == "L") ? 'S' : (!string.IsNullOrEmpty(tag) ? tag[0] : 'M');
                }
                else
                {
                    var ms = Volatile.Read(ref _intervalMs);
                    rate = (ms <= 300) ? 'F' : (ms <= 1000) ? 'M' : 'S';
                }

                _device.SetRate(rate);
            }
            catch { }
        }

        private async Task<bool> PauseContinuousIfRunningAsync()
        {
            bool wasContinuous = ContToggle?.IsChecked == true;
            if (wasContinuous) { ContToggle.IsChecked = false; await Task.Delay(80); }
            return wasContinuous;
        }

        private void ResumeContinuousIf(bool shouldResume)
        {
            if (shouldResume && ContToggle != null) ContToggle.IsChecked = true;
        }

        private void Remote_Click(object sender, RoutedEventArgs e) { if (!EnsureConnected()) return; _device.SetRemote(); SetStatus(Strings.MEAS_Status_RemoteRequested); }
        private void Local_Click(object sender, RoutedEventArgs e) { if (!EnsureConnected()) return; _device.SetLocal(); SetStatus(Strings.MEAS_Status_LocalRequested); }

        private void QueryRate_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureConnected()) return;
            var r = _device.QueryRate();
            var tb = this.FindName("RateReadbackText") as TextBlock;
            if (tb != null) tb.Text = string.IsNullOrWhiteSpace(r) ? "—" : r.Trim();
            SetStatus(Strings.MEAS_Status_RateQueried);
        }

        private async void ApplyTempUnit_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureConnected()) return;
            char u = (TempUnitC?.IsChecked == true) ? 'C' : (TempUnitF?.IsChecked == true) ? 'F' : 'K';
            var was = await PauseContinuousIfRunningAsync();
            try { _device.SetTempUnit(u); SetStatus(string.Format(CultureInfo.CurrentCulture, Strings.MEAS_Status_TempUnitAppliedFmt, u)); }
            finally { ResumeContinuousIf(was); }
        }

        private void QueryTempUnit_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureConnected()) return;
            var r = _device.QueryTempUnit()?.Trim().ToUpperInvariant();
            if (r?.Contains("C") == true) TempUnitC.IsChecked = true;
            else if (r?.Contains("F") == true) TempUnitF.IsChecked = true;
            else if (r?.Contains("K") == true) TempUnitK.IsChecked = true;
            SetStatus(string.Format(CultureInfo.CurrentCulture, Strings.MEAS_Status_TempUnitQueriedFmt, r));
        }

        private async void ApplyTherKits90_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureConnected()) return;
            var was = await PauseContinuousIfRunningAsync();
            try { _device.ConfigureTempTherKITS90(); SetStatus(Strings.MEAS_Status_ThermoConfigured); }
            finally { ResumeContinuousIf(was); }
        }

        private void QueryTempType_Click(object sender, RoutedEventArgs e) { if (!EnsureConnected()) return; var r = _device.QueryTempType(); SetStatus(string.Format(CultureInfo.CurrentCulture, Strings.MEAS_Status_TempTypeQueriedFmt, r)); }

        private void ReadTempOnce_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureConnected()) return;
            var r = _device.ReadTempOnce();
            if (r != null)
            {
                HandleIncomingMeasurement(r, DateTime.UtcNow);
            }
            SetStatus(Strings.MEAS_Status_TempReadDone);
        }

        // ---------- Voltage range APPLY ----------
        private async void ApplyVoltRangeV_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureConnected()) return;

            var mode = ((CurrContent(VoltModeCombo)) ?? "DC").ToUpperInvariant();
            var vSel = CurrContent(VoltRangeV);
            if (string.IsNullOrWhiteSpace(vSel)) return;

            var was = await PauseContinuousIfRunningAsync();
            try
            {
                if (mode == "DC") _device.ConfVoltDC(vSel);
                else _device.ConfVoltAC(vSel);
                SetStatus(string.Format(CultureInfo.CurrentCulture, Strings.MEAS_Status_VoltConfFmt, mode, vSel));
                UpdateRangeDisplay($"{vSel} V");
                VoltRange_mV.SelectedIndex = -1;
            }
            finally { ResumeContinuousIf(was); }
        }

        private async void ApplyVoltRange_mV_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureConnected()) return;

            var mode = ((CurrContent(VoltModeCombo)) ?? "DC").ToUpperInvariant();
            var mvSel = CurrContent(VoltRange_mV);
            if (string.IsNullOrWhiteSpace(mvSel)) return;

            var was = await PauseContinuousIfRunningAsync();
            try
            {
                if (mode == "DC") _device.ConfMilliVoltDC(mvSel);
                else _device.ConfMilliVoltAC(mvSel);
                SetStatus(string.Format(CultureInfo.CurrentCulture, Strings.MEAS_Status_VoltConfFmt, mode, mvSel));
                UpdateRangeDisplay($"{mvSel} V");
                VoltRangeV.SelectedIndex = -1;
            }
            finally { ResumeContinuousIf(was); }
        }

        private void QueryRange_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureConnected()) return;
            var r = _device.QueryRange();
            SetStatus(string.Format(CultureInfo.CurrentCulture, Strings.MEAS_Status_RangeQueryFmt, r));
            if (!string.IsNullOrWhiteSpace(r))
                UpdateRangeDisplay(r.Trim());
        }

        private void QueryFunc_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureConnected()) return;
            var f = _device.QueryFunction();
            SetStatus(string.Format(CultureInfo.CurrentCulture, Strings.MEAS_Status_FuncQueryFmt, f));
        }

        private void UpdateRangeDisplay(string rangeText)
        {
            if (RangeDisplayText != null)
                RangeDisplayText.Text = rangeText;
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
                if (mode == "DC") _device.ConfCurrDC_Amps(aSel); else _device.ConfCurrAC_Amps(aSel);
                SetStatus(string.Format(CultureInfo.CurrentCulture, Strings.MEAS_Status_CurrConfFmt, mode, aSel));
                UpdateRangeDisplay($"{aSel} A");
                CurrRange_mA.SelectedIndex = -1;
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
                if (mode == "DC") _device.ConfCurrDC_mA(mSel); else _device.ConfCurrAC_mA(mSel);
                SetStatus(string.Format(CultureInfo.CurrentCulture, Strings.MEAS_Status_CurrConfFmt, mode, mSel));
                UpdateRangeDisplay($"{mSel} A");
                CurrRangeA.SelectedIndex = -1;
            }
            finally { ResumeContinuousIf(was); }
        }

        private async void ApplyRes_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureConnected()) return;
            var was = await PauseContinuousIfRunningAsync();
            try { _device.ConfRes(); SetStatus(Strings.MEAS_Status_ResConf); }
            finally { ResumeContinuousIf(was); }
        }

        private async void ApplyCap_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureConnected()) return;
            var sel = CurrContent(CapRangeCombo);
            if (string.IsNullOrWhiteSpace(sel)) return;

            var was = await PauseContinuousIfRunningAsync();
            try { _device.ConfCap(sel); SetStatus(string.Format(CultureInfo.CurrentCulture, Strings.MEAS_Status_CapConfFmt, sel)); }
            finally { ResumeContinuousIf(was); }
        }

        private async void ApplyPer_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureConnected()) return;
            var was = await PauseContinuousIfRunningAsync();
            try { _device.ConfPer(); SetStatus(Strings.MEAS_Status_PerConf); }
            finally { ResumeContinuousIf(was); }
        }

        private void RefreshStats_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureConnected()) return;
            try
            {
                var stats = _device.TryQueryAveragingAll();
                if (stats != null)
                {
                    AvgValText.Text = DoubleToStr(stats.Avg);
                    MinValText.Text = DoubleToStr(stats.Min);
                    MaxValText.Text = DoubleToStr(stats.Max);
                    PlotControl?.SetDeviceStats(stats.Min, stats.Max, stats.Avg);
                    SetStatus(Strings.MEAS_Status_AvgRefreshed);
                }
                else
                {
                    AvgValText.Text = _device.QueryAverAvg() ?? "—";
                    MinValText.Text = _device.QueryAverMin() ?? "—";
                    MaxValText.Text = _device.QueryAverMax() ?? "—";
                    SetStatus(Strings.MEAS_Status_AvgRefreshedAlt);
                }
            }
            catch { /* ignore UI errors */ }
        }

        private static string CurrContent(ComboBox cb) => (cb?.SelectedItem as ComboBoxItem)?.Content?.ToString();

        // -------- Auto Range --------
        private void AutoRangeCheck_Checked(object sender, RoutedEventArgs e)
        {
            if (!EnsureConnected()) return;
            _device.SetAutoRange(true);
        }
        private void AutoRangeCheck_Unchecked(object sender, RoutedEventArgs e)
        {
            if (!EnsureConnected()) return;
            _device.SetAutoRange(false);
        }

        // -------- Math suite (relative, dB) --------
        private void RelEnable_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureConnected()) return;
            _device.MathRelEnable();
            SetStatus("CALC:FUNC NULL");
        }

        private void RelZero_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureConnected()) return;
            _device.MathRelZero();
            SetStatus("CALC:NULL:OFFS");
        }

        private void RelDisable_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureConnected()) return;
            _device.MathOff();
            SetStatus("CALC:FUNC OFF");
        }

        private void MathDb_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureConnected()) return;
            var refOhms = ParseRefOhms();
            if (refOhms != null) _device.MathDb(refOhms.Value);
            else _device.MathDb(50);
            SetStatus("CALC:FUNC DB");
        }

        private void MathDbm_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureConnected()) return;
            var refOhms = ParseRefOhms();
            if (refOhms != null) _device.MathDbm(refOhms.Value);
            else _device.MathDbm(50);
            SetStatus("CALC:FUNC DBM");
        }

        private void MathOff_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureConnected()) return;
            _device.MathOff();
            SetStatus("CALC:FUNC OFF");
        }

        private int? ParseRefOhms()
        {
            var s = CurrContent(DbRefCombo);
            if (int.TryParse(s, out int v)) return v;
            return null;
        }

        // -------- Continuity & Beeper --------
        private async void ApplyContinuityThreshold_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureConnected()) return;
            if (!double.TryParse(ContThresholdBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double thr))
            {
                SetStatus(Strings.MEAS_Status_InvalidContThreshold);
                return;
            }
            var was = await PauseContinuousIfRunningAsync();
            try { _device.SetContinuityThreshold(thr); SetStatus(string.Format(CultureInfo.CurrentCulture, Strings.MEAS_Status_ContThresholdFmt, thr)); }
            finally { ResumeContinuousIf(was); }
        }

        private void BeepToggle_Checked(object sender, RoutedEventArgs e)
        {
            if (!EnsureConnected()) return;
            _device.SetBeeper(true);
            SetStatus(Strings.MEAS_Status_BeeperOn);
        }

        private void BeepToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            if (!EnsureConnected()) return;
            _device.SetBeeper(false);
            SetStatus(Strings.MEAS_Status_BeeperOff);
        }

        // -------- Limits + PASS/FAIL --------
        private void ApplyLimits_Click(object sender, RoutedEventArgs e)
        {
            SetStatus(Strings.MEAS_Status_LimitsApplied);
        }

        private (double? lo, double? hi) GetLimits()
        {
            double? lo = null, hi = null;
            if (double.TryParse(LowerLimitBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double l)) lo = l;
            if (double.TryParse(UpperLimitBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double h)) hi = h;
            return (lo, hi);
        }

        private void UpdatePassFail(double? value)
        {
            if (!value.HasValue) { PassFailText.Text = "—"; PassFailText.Foreground = Brushes.Black; return; }
            var (lo, hi) = GetLimits();
            bool pass = true;
            if (lo.HasValue && value.Value < lo.Value) pass = false;
            if (hi.HasValue && value.Value > hi.Value) pass = false;

            PassFailText.Text = pass ? Strings.Common_Pass : Strings.Common_Fail;
            PassFailText.Foreground = pass ? (Brush)FindResource("PassBrush") : (Brush)FindResource("FailBrush");
        }

        // -------- Plot Controls --------
        private void PlotZoomIn_Click(object sender, RoutedEventArgs e)
        {
            if (PlotControl == null) return;
            PlotControl.Focus();
            InvokeIfExists(PlotControl, "ZoomIn");
        }

        private void PlotZoomOut_Click(object sender, RoutedEventArgs e)
        {
            if (PlotControl == null) return;
            PlotControl.Focus();
            InvokeIfExists(PlotControl, "ZoomOut");
        }

        private void PlotResetView_Click(object sender, RoutedEventArgs e)
        {
            if (PlotControl == null) return;
            PlotControl.Focus();
            InvokeIfExists(PlotControl, "ResetView");
        }

        private static void InvokeIfExists(object target, string method, params object[] args)
        {
            if (target == null) return;
            var mi = target.GetType().GetMethod(method, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (mi != null)
            {
                try { mi.Invoke(target, args); } catch { }
            }
        }

        // -------- Central point for incoming measurement --------
        private void HandleIncomingMeasurement(string resp, DateTime tsUtc)
        {
            var display = FormatMeasurementFromResponse(resp, out double? value);
            if (MeasurementText != null) MeasurementText.Text = display;

            var unit = UnitLabel?.Text ?? "";
            if (unit == "°") unit = (TempUnitC?.IsChecked == true) ? "°C" : (TempUnitF?.IsChecked == true) ? "°F" : "K";
            UnitSuffixText.Text = unit;

            UpdateMeasurementFields(resp);
            UpdatePassFail(value);
            UpdateSps(tsUtc);
        }

        private void UpdateSps(DateTime tsUtc)
        {
            _spsWindow.Enqueue(tsUtc);
            while (_spsWindow.Count > 30) _spsWindow.Dequeue();
            if (_spsWindow.Count >= 2)
            {
                var first = _spsWindow.First();
                var last = _spsWindow.Last();
                var seconds = (last - first).TotalSeconds;
                if (seconds > 0)
                {
                    var sps = (_spsWindow.Count - 1) / seconds;
                    SpsText.Text = sps.ToString("F1", CultureInfo.InvariantCulture);
                }
            }
        }
    }
}
