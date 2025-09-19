// File: HouseholdMS/View/UserControls/EpeverMonitorControl.xaml.cs
using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using HouseholdMS.Helpers;
using HouseholdMS.Services; // for PortDescriptor / SerialPortInspector if present

namespace HouseholdMS.View.UserControls
{
    public partial class EpeverMonitorControl : UserControl
    {
        private CancellationTokenSource _cts;
        private CancellationTokenSource _portScanCts;
        private DispatcherTimer _timer;
        private bool _connected;
        private bool _polling;

        private struct UiSnap { public string Port; public int Baud; public byte Unit; }

        public EpeverMonitorControl()
        {
            InitializeComponent();
            _ = RefreshPortsAsync(); // cached
            TxtStatus.Text = "Select COM port, set ID, click Connect.";
            this.Unloaded += EpeverMonitorControl_Unloaded;
        }

        private void EpeverMonitorControl_Unloaded(object sender, RoutedEventArgs e)
        {
            Disconnect();
            try { _portScanCts?.Cancel(); _portScanCts?.Dispose(); _portScanCts = null; } catch { }
        }

        // -------- Toolbar --------
        private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            SerialPortInspector.InvalidateCache();
            await RefreshPortsAsync();
        }

        private void CmbInterval_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_timer != null) _timer.Interval = GetSelectedInterval();
        }

        private void BtnConnect_Click(object sender, RoutedEventArgs e)
        {
            var portName = ExtractPortName(CmbPorts?.SelectedItem);
            if (string.IsNullOrWhiteSpace(portName))
            {
                MessageBox.Show("Select a COM port first.", "EPEVER Monitor", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (!byte.TryParse(TxtId.Text.Trim(), out byte unitId) || unitId < 1 || unitId > 247)
            {
                MessageBox.Show("Device ID must be 1..247.", "EPEVER Monitor", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _cts = new CancellationTokenSource();
            _connected = true;
            SetUiState(true);

            _timer = new DispatcherTimer { Interval = GetSelectedInterval() };
            _timer.Tick += (s, ev) => { if (ChkAuto.IsChecked == true) _ = PollOnceSafe(); };
            _timer.Start();

            var selectedText = CmbPorts.SelectedItem?.ToString() ?? portName;
            TxtLink.Text = "Connected (idle)";
            TxtLink.Foreground = (System.Windows.Media.Brush)FindResource("Warn");
            TxtStatus.Text = $"Connected to {selectedText} @ {GetBaudUI()} (ID={unitId}).";

            // Query device identification in background (non-blocking)
            _ = Task.Run(() => QueryDeviceIdentification(portName, GetBaudUI(), unitId, _cts.Token));

            // First poll
            _ = PollOnceSafe();
        }

        private void BtnDisconnect_Click(object sender, RoutedEventArgs e) => Disconnect();
        private async void BtnRead_Click(object sender, RoutedEventArgs e) => await PollOnceSafe();

        // -------- Inspector --------
        private void BtnInspectorRead_Click(object sender, RoutedEventArgs e)
        {
            if (!_connected) { MessageBox.Show("Connect first.", "Inspector", MessageBoxButton.OK, MessageBoxImage.Information); return; }

            if (!TryParseAddress(TxtInspectorStart.Text.Trim(), out ushort start))
            {
                MessageBox.Show("Invalid start address.", "Inspector", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!ushort.TryParse(TxtInspectorCount.Text.Trim(), out ushort count) || count < 1 || count > 60)
            {
                MessageBox.Show("Count must be 1..60.", "Inspector", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            UiSnap snap = Dispatcher.Invoke(new Func<UiSnap>(GetUiSnap));
            ListInspector.ItemsSource = null;

            Task.Run(() =>
            {
                string err;
                ushort[] regs = ModbusRtuRaw.TryReadInputRegisters(snap.Port, snap.Baud, snap.Unit, start, count, 1500, out err);
                Dispatcher.Invoke(() =>
                {
                    if (regs == null) { TxtRaw.Text = "Inspector ERR: " + err; return; }
                    var rows = new List<object>();
                    for (int i = 0; i < regs.Length; i++)
                    {
                        double scaled = Math.Round(regs[i] / 100.0, 2);
                        rows.Add(new { Index = i, Hex = "0x" + regs[i].ToString("X4"), Dec = regs[i], Scaled = scaled });
                    }
                    ListInspector.ItemsSource = rows;
                    TxtRaw.Text = "Inspector OK: " + count + " regs @ 0x" + start.ToString("X4");
                });
            });
        }

        private static bool TryParseAddress(string s, out ushort addr)
        {
            s = s.Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(s.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out int tmp) && tmp >= 0 && tmp <= 0xFFFF)
                { addr = (ushort)tmp; return true; }
                addr = 0; return false;
            }
            if (ushort.TryParse(s, out ushort u)) { addr = u; return true; }
            addr = 0; return false;
        }

        // -------- Poll loop (coalesced reads, single in-flight) --------
        private async Task PollOnceSafe()
        {
            if (!_connected || _polling) return;
            _polling = true;
            try
            {
                UiSnap snap = Dispatcher.Invoke(new Func<UiSnap>(GetUiSnap));
                if (string.IsNullOrWhiteSpace(snap.Port)) throw new InvalidOperationException("No COM port selected.");
                await Task.Run(() => PollOnce(snap.Port, snap.Baud, snap.Unit, _cts != null ? _cts.Token : CancellationToken.None));
            }
            catch (Exception ex)
            {
                TxtStatus.Text = "Error: " + ex.Message;
                TxtLink.Text = "Comm error";
                TxtLink.Foreground = (System.Windows.Media.Brush)FindResource("Bad");
                TxtRaw.Text = "ERR: " + ex.Message;
            }
            finally { _polling = false; }
        }

        private void PollOnce(string port, int baud, byte unit, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            // ---- Coalesced block A: 0x3100.. up to SOC (0x311A) ----
            ushort blkAStart = 0x3100;
            ushort blkACount = 0x20; // 32 regs to cover PV, BATC, LOAD, TEMP1, and near SOC
            ushort[] blkA = null; string blkAErr = null;
            try { blkA = ModbusRtuRaw.ReadInputRegisters(port, baud, unit, blkAStart, blkACount, 1200); }
            catch (Exception ex) { blkAErr = ex.Message; }

            // ---- Status block: 0x3200..0x3201 ----
            ushort[] stat = null; string statErr = null;
            try { stat = ModbusRtuRaw.ReadInputRegisters(port, baud, unit, Helpers.EpeverRegisters.STAT_START, Helpers.EpeverRegisters.STAT_COUNT, 800); }
            catch (Exception ex) { statErr = ex.Message; }

            // ---- Extremes: 0x3302..0x3303 (2 regs) ----
            ushort[] ext = null; string extErr = null;
            try { ext = ModbusRtuRaw.ReadInputRegisters(port, baud, unit, Helpers.EpeverRegisters.EV_BATT_VMAX_TODAY, 2, 800); }
            catch (Exception ex) { extErr = ex.Message; }

            // ---- Rated values (small single reads; safe) ----
            ushort[] ratedVinRaw = null, ratedChgRaw = null, ratedLoadRaw = null;
            string ratedErr = null;
            try { ratedVinRaw = ModbusRtuRaw.ReadInputRegisters(port, baud, unit, Helpers.EpeverRegisters.RATED_INPUT_VOLT, 1, 600); } catch (Exception ex) { ratedErr = ex.Message; }
            try { ratedChgRaw = ModbusRtuRaw.ReadInputRegisters(port, baud, unit, Helpers.EpeverRegisters.RATED_CHG_CURR, 1, 600); } catch { }
            try { ratedLoadRaw = ModbusRtuRaw.ReadInputRegisters(port, baud, unit, Helpers.EpeverRegisters.RATED_LOAD_CURR, 1, 600); } catch { }

            // ---- Parse block A safely ----
            double? pvV = null, pvA = null, pvW = null;
            double? batV = null, batA = null, batW = null;
            double? loadV = null, loadA = null, loadW = null;
            double? tBatt = null, tAmb = null, tCtrl = null;
            int? soc = null;

            if (blkA != null && blkA.Length >= blkACount)
            {
                int off(ushort abs) => abs - blkAStart;

                // PV
                pvV = ModbusRtuRaw.S100(blkA[off(EpeverRegisters.PV_START) + EpeverRegisters.PV_VOLT]);
                pvA = ModbusRtuRaw.S100(blkA[off(EpeverRegisters.PV_START) + EpeverRegisters.PV_CURR]);
                pvW = ModbusRtuRaw.PwrFromU32S100(
                        ModbusRtuRaw.U32(
                            blkA[off(EpeverRegisters.PV_START) + EpeverRegisters.PV_PWR_LO],
                            blkA[off(EpeverRegisters.PV_START) + EpeverRegisters.PV_PWR_HI]));

                // Battery charge
                batV = ModbusRtuRaw.S100(blkA[off(EpeverRegisters.BATC_START) + EpeverRegisters.BATC_VOLT]);
                batA = ModbusRtuRaw.S100(blkA[off(EpeverRegisters.BATC_START) + EpeverRegisters.BATC_CURR]);
                batW = ModbusRtuRaw.PwrFromU32S100(
                        ModbusRtuRaw.U32(
                            blkA[off(EpeverRegisters.BATC_START) + EpeverRegisters.BATC_PWR_LO],
                            blkA[off(EpeverRegisters.BATC_START) + EpeverRegisters.BATC_PWR_HI]));

                // Load
                loadV = ModbusRtuRaw.S100(blkA[off(EpeverRegisters.LOAD_START) + EpeverRegisters.LOAD_VOLT]);
                loadA = ModbusRtuRaw.S100(blkA[off(EpeverRegisters.LOAD_START) + EpeverRegisters.LOAD_CURR]);
                loadW = ModbusRtuRaw.PwrFromU32S100(
                        ModbusRtuRaw.U32(
                            blkA[off(EpeverRegisters.LOAD_START) + EpeverRegisters.LOAD_PWR_LO],
                            blkA[off(EpeverRegisters.LOAD_START) + EpeverRegisters.LOAD_PWR_HI]));

                // Temperatures (handle missing gracefully)
                try { tBatt = ModbusRtuRaw.S100(blkA[off(EpeverRegisters.TEMP1_START) + EpeverRegisters.TEMP1_BATT]); } catch { }
                try { tAmb = ModbusRtuRaw.S100(blkA[off(EpeverRegisters.TEMP1_START) + EpeverRegisters.TEMP1_AMBIENT]); } catch { }
                try { tCtrl = ModbusRtuRaw.S100(blkA[off(EpeverRegisters.TEMP1_START) + EpeverRegisters.TEMP1_CTRL]); } catch { }

                // SOC (may sit at 0x311A)
                int socIdx = off(EpeverRegisters.SOC_ADDR);
                if (socIdx >= 0 && socIdx < blkA.Length) soc = blkA[socIdx];
            }
            else
            {
                // Fallback fine-grained reads if big block failed (keeps UI alive)
                TryReadPiecewise(port, baud, unit, ref pvV, ref pvA, ref pvW, ref batV, ref batA, ref batW,
                                 ref loadV, ref loadA, ref loadW, ref tBatt, ref tAmb, ref tCtrl, ref soc);
            }

            // Status / Stage
            string stage = null;
            if (stat != null && stat.Length >= 2)
            {
                ushort reg3201 = stat[1];
                stage = EpeverRegisters.DecodeChargingStageFrom3201(reg3201) + $"  (0x{reg3201:X4})";
            }

            // Extremes
            double? vMaxToday = null, vMinToday = null;
            if (ext != null && ext.Length >= 2)
            {
                vMaxToday = ModbusRtuRaw.S100(ext[0]);
                vMinToday = ModbusRtuRaw.S100(ext[1]);
            }

            // Rated
            double? ratedVin = ratedVinRaw != null ? ModbusRtuRaw.S100(ratedVinRaw[0]) : (double?)null;
            double? ratedChg = ratedChgRaw != null ? ModbusRtuRaw.S100(ratedChgRaw[0]) : (double?)null;
            double? ratedLoad = ratedLoadRaw != null ? ModbusRtuRaw.S100(ratedLoadRaw[0]) : (double?)null;

            // ---- UI update ----
            Dispatcher.Invoke(() =>
            {
                TxtPvV.Text = "Voltage: " + (pvV.HasValue ? pvV.Value.ToString("F2") + " V" : "—");
                TxtPvA.Text = "Current: " + (pvA.HasValue ? pvA.Value.ToString("F2") + " A" : "—");
                TxtPvW.Text = "Power: " + (pvW.HasValue ? pvW.Value.ToString("F1") + " W" : "—");

                TxtBatV.Text = "Voltage: " + (batV.HasValue ? batV.Value.ToString("F2") + " V" : "—");
                TxtBatA.Text = "Charge Current: " + (batA.HasValue ? batA.Value.ToString("F2") + " A" : "—");
                TxtBatW.Text = "Charge Power: " + (batW.HasValue ? batW.Value.ToString("F1") + " W" : "—");

                TxtLoadV.Text = "Voltage: " + (loadV.HasValue ? loadV.Value.ToString("F2") + " V" : "—");
                TxtLoadA.Text = "Current: " + (loadA.HasValue ? loadA.Value.ToString("F2") + " A" : "—");
                TxtLoadW.Text = "Power: " + (loadW.HasValue ? loadW.Value.ToString("F1") + " W" : "—");

                TxtSoc.Text = "SOC: " + (soc.HasValue ? soc.Value + " %" : "—");
                TxtStage.Text = "Charge Stage: " + (stage ?? "—");

                TxtTempBatt.Text = "Battery: " + (tBatt.HasValue ? tBatt.Value.ToString("F2") + " °C" : "—");
                TxtTempAmb.Text = "Ambient: " + (tAmb.HasValue ? tAmb.Value.ToString("F2") + " °C" : "—");
                TxtTempCtrl.Text = "Controller: " + (tCtrl.HasValue ? tCtrl.Value.ToString("F2") + " °C" : "—");

                TxtBattVmaxToday.Text = "Battery Vmax: " + (vMaxToday.HasValue ? vMaxToday.Value.ToString("F2") + " V" : "—");
                TxtBattVminToday.Text = "Battery Vmin: " + (vMinToday.HasValue ? vMinToday.Value.ToString("F2") + " V" : "—");

                TxtRatedVin.Text = "PV Rated Input Voltage: " + (ratedVin.HasValue ? ratedVin.Value.ToString("F1") + " V" : "—");
                TxtRatedChgA.Text = "Rated Charge Current: " + (ratedChg.HasValue ? ratedChg.Value.ToString("F1") + " A" : "—");
                TxtRatedLoadA.Text = "Rated Load Current: " + (ratedLoad.HasValue ? ratedLoad.Value.ToString("F1") + " A" : "—");

                TxtUpdated.Text = "Last update: " + DateTime.Now.ToString("HH:mm:ss");

                bool anyOk = pvV.HasValue || batV.HasValue || loadV.HasValue || soc.HasValue ||
                             tBatt.HasValue || tAmb.HasValue || tCtrl.HasValue || vMaxToday.HasValue;

                if (anyOk) { TxtStatus.Text = "OK"; TxtLink.Text = "Polling"; TxtLink.Foreground = (System.Windows.Media.Brush)FindResource("Good"); }
                else { TxtStatus.Text = "No data (check wiring/ID/baud)"; TxtLink.Text = "Connected, but no data"; TxtLink.Foreground = (System.Windows.Media.Brush)FindResource("Warn"); }

                var errs = new List<string>();
                if (blkAErr != null) errs.Add("BlockA: " + blkAErr);
                if (statErr != null) errs.Add("STAT: " + statErr);
                if (extErr != null) errs.Add("EXTREME: " + extErr);
                if (ratedErr != null && !(ratedVin.HasValue || ratedChg.HasValue || ratedLoad.HasValue)) errs.Add("RATED: " + ratedErr);

                if (errs.Count > 0) TxtRaw.Text = string.Join("\n", errs);
                else if (string.IsNullOrWhiteSpace(TxtRaw.Text) || TxtRaw.Text.StartsWith("ERR:")) TxtRaw.Text = "OK";
            });
        }

        private void TryReadPiecewise(string port, int baud, byte unit,
            ref double? pvV, ref double? pvA, ref double? pvW,
            ref double? batV, ref double? batA, ref double? batW,
            ref double? loadV, ref double? loadA, ref double? loadW,
            ref double? tBatt, ref double? tAmb, ref double? tCtrl, ref int? soc)
        {
            try
            {
                var r = ModbusRtuRaw.ReadInputRegisters(port, baud, unit, EpeverRegisters.PV_START, EpeverRegisters.PV_COUNT, 1200);
                pvV = ModbusRtuRaw.S100(r[EpeverRegisters.PV_VOLT]);
                pvA = ModbusRtuRaw.S100(r[EpeverRegisters.PV_CURR]);
                pvW = ModbusRtuRaw.PwrFromU32S100(ModbusRtuRaw.U32(r[EpeverRegisters.PV_PWR_LO], r[EpeverRegisters.PV_PWR_HI]));
            }
            catch { }
            try
            {
                var r = ModbusRtuRaw.ReadInputRegisters(port, baud, unit, EpeverRegisters.BATC_START, EpeverRegisters.BATC_COUNT, 1200);
                batV = ModbusRtuRaw.S100(r[EpeverRegisters.BATC_VOLT]);
                batA = ModbusRtuRaw.S100(r[EpeverRegisters.BATC_CURR]);
                batW = ModbusRtuRaw.PwrFromU32S100(ModbusRtuRaw.U32(r[EpeverRegisters.BATC_PWR_LO], r[EpeverRegisters.BATC_PWR_HI]));
            }
            catch { }
            try
            {
                var r = ModbusRtuRaw.ReadInputRegisters(port, baud, unit, EpeverRegisters.LOAD_START, EpeverRegisters.LOAD_COUNT, 1200);
                loadV = ModbusRtuRaw.S100(r[EpeverRegisters.LOAD_VOLT]);
                loadA = ModbusRtuRaw.S100(r[EpeverRegisters.LOAD_CURR]);
                loadW = ModbusRtuRaw.PwrFromU32S100(ModbusRtuRaw.U32(r[EpeverRegisters.LOAD_PWR_LO], r[EpeverRegisters.LOAD_PWR_HI]));
            }
            catch { }
            try
            {
                var t = ModbusRtuRaw.ReadInputRegisters(port, baud, unit, EpeverRegisters.TEMP1_START, EpeverRegisters.TEMP1_COUNT, 1200);
                tBatt = ModbusRtuRaw.S100(t[EpeverRegisters.TEMP1_BATT]);
                tAmb = ModbusRtuRaw.S100(t[EpeverRegisters.TEMP1_AMBIENT]);
                tCtrl = ModbusRtuRaw.S100(t[EpeverRegisters.TEMP1_CTRL]);
            }
            catch
            {
                try
                {
                    var t = ModbusRtuRaw.ReadInputRegisters(port, baud, unit, EpeverRegisters.TEMP2_START, EpeverRegisters.TEMP2_COUNT, 1200);
                    tBatt = ModbusRtuRaw.S100(t[0]); tAmb = ModbusRtuRaw.S100(t[1]); tCtrl = ModbusRtuRaw.S100(t[2]);
                }
                catch { }
            }
            try { var s = ModbusRtuRaw.ReadInputRegisters(port, baud, unit, EpeverRegisters.SOC_ADDR, EpeverRegisters.SOC_COUNT, 800); soc = s[0]; } catch { }
        }

        // -------- Device ID (non-blocking) --------
        private void QueryDeviceIdentification(string port, int baud, byte unit, CancellationToken ct)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                string err;
                var dict = ModbusRtuRaw.TryReadDeviceIdentification(port, baud, unit, ModbusRtuRaw.DeviceIdCategory.Basic, 1500, out err);
                if (dict == null)
                {
                    Dispatcher.Invoke(() =>
                    {
                        TxtDevVendor.Text = "Vendor: N/A";
                        TxtDevProduct.Text = "Product: N/A";
                        TxtDevFw.Text = "Firmware: N/A";
                        if (!string.IsNullOrWhiteSpace(err)) TxtRaw.Text = "DevID ERR: " + err;
                    });
                    return;
                }

                dict.TryGetValue(0x00, out string vendor);
                dict.TryGetValue(0x01, out string product);
                dict.TryGetValue(0x02, out string fw);

                Dispatcher.Invoke(() =>
                {
                    TxtDevVendor.Text = "Vendor: " + (string.IsNullOrWhiteSpace(vendor) ? "—" : vendor);
                    TxtDevProduct.Text = "Product: " + (string.IsNullOrWhiteSpace(product) ? "—" : product);
                    TxtDevFw.Text = "Firmware: " + (string.IsNullOrWhiteSpace(fw) ? "—" : fw);
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    TxtDevVendor.Text = "Vendor: N/A";
                    TxtDevProduct.Text = "Product: N/A";
                    TxtDevFw.Text = "Firmware: N/A";
                    TxtRaw.Text = "DevID ERR: " + ex.Message;
                });
            }
        }

        // -------- Helpers --------
        private async Task RefreshPortsAsync()
        {
            // quick placeholder
            var quick = SerialPort.GetPortNames()
                .OrderBy(s => {
                    if (s.StartsWith("COM", StringComparison.OrdinalIgnoreCase) && int.TryParse(s.Substring(3), out int n)) return n;
                    return int.MaxValue;
                })
                .ThenBy(s => s)
                .Select(s => new PortDescriptor(s, "Scanning…", "Scan"))
                .ToList();

            CmbPorts.ItemsSource = quick;
            if (quick.Count > 0) CmbPorts.SelectedIndex = 0;

            // Cancel previous scan
            try { _portScanCts?.Cancel(); _portScanCts?.Dispose(); } catch { }
            _portScanCts = new CancellationTokenSource();

            try
            {
                var scanned = await SerialPortInspector.GetOrProbeAsync(
                    attemptTimeoutMs: 200,
                    cacheTtlMs: 15000,
                    ct: _portScanCts.Token);

                CmbPorts.ItemsSource = scanned; // PortDescriptor
                if (CmbPorts.Items.Count > 0) CmbPorts.SelectedIndex = 0;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                var ports = SerialPort.GetPortNames().ToList();
                CmbPorts.ItemsSource = ports;
                if (ports.Count > 0) CmbPorts.SelectedIndex = 0;
                TxtStatus.Text = "Port scan error: " + ex.Message;
            }
        }

        private string ExtractPortName(object selected)
        {
            if (selected is PortDescriptor pd) return pd.Port;
            return selected?.ToString();
        }

        private int GetBaudUI()
        {
            var item = (CmbBaud.SelectedItem as ComboBoxItem);
            return (item != null && int.TryParse(item.Content.ToString(), out int b)) ? b : 115200;
        }

        private TimeSpan GetSelectedInterval()
        {
            var item = (CmbInterval.SelectedItem as ComboBoxItem);
            var label = item != null ? item.Content.ToString() : "1s";
            if (label == "2s") return TimeSpan.FromSeconds(2);
            if (label == "5s") return TimeSpan.FromSeconds(5);
            return TimeSpan.FromSeconds(1);
        }

        private UiSnap GetUiSnap()
        {
            var snap = new UiSnap();
            snap.Port = ExtractPortName(CmbPorts.SelectedItem);
            snap.Baud = GetBaudUI();
            snap.Unit = byte.TryParse(TxtId.Text.Trim(), out byte parsed) ? parsed : (byte)1;
            return snap;
        }

        private void Disconnect()
        {
            if (_timer != null) _timer.Stop();
            if (_cts != null) _cts.Cancel();
            if (_cts != null) _cts.Dispose();
            _cts = null;
            _connected = false;
            SetUiState(false);
            TxtLink.Text = "Disconnected";
            TxtLink.Foreground = (System.Windows.Media.Brush)FindResource("Bad");
            TxtStatus.Text = "Disconnected.";

            // Clear device info
            TxtDevVendor.Text = "Vendor: —";
            TxtDevProduct.Text = "Product: —";
            TxtDevFw.Text = "Firmware: —";
        }

        private void SetUiState(bool connected)
        {
            BtnConnect.IsEnabled = !connected;
            BtnDisconnect.IsEnabled = connected;
            BtnRead.IsEnabled = connected;
            ChkAuto.IsEnabled = connected;
            CmbPorts.IsEnabled = !connected;
            CmbBaud.IsEnabled = !connected;
            TxtId.IsEnabled = !connected;
            CmbInterval.IsEnabled = connected;
        }
    }
}
