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
using HouseholdMS.Services;

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
            byte unitId;
            if (!byte.TryParse(TxtId.Text.Trim(), out unitId) || unitId < 1 || unitId > 247)
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

            _ = PollOnceSafe();
        }

        private void BtnDisconnect_Click(object sender, RoutedEventArgs e) { Disconnect(); }
        private async void BtnRead_Click(object sender, RoutedEventArgs e) { await PollOnceSafe(); }

        // -------- Inspector --------
        private void BtnInspectorRead_Click(object sender, RoutedEventArgs e)
        {
            if (!_connected) { MessageBox.Show("Connect first.", "Inspector", MessageBoxButton.OK, MessageBoxImage.Information); return; }

            ushort start; ushort count;
            if (!TryParseAddress(TxtInspectorStart.Text.Trim(), out start))
            {
                MessageBox.Show("Invalid start address.", "Inspector", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!ushort.TryParse(TxtInspectorCount.Text.Trim(), out count) || count < 1 || count > 60)
            {
                MessageBox.Show("Count must be 1..60.", "Inspector", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            UiSnap snap = Dispatcher.Invoke(new Func<UiSnap>(GetUiSnap));
            ListInspector.ItemsSource = null;

            Task.Run(delegate
            {
                string err;
                ushort[] regs = ModbusRtuRaw.TryReadInputRegisters(snap.Port, snap.Baud, snap.Unit, start, count, 1500, out err);
                Dispatcher.Invoke(delegate
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
                int tmp;
                if (int.TryParse(s.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out tmp) && tmp >= 0 && tmp <= 0xFFFF)
                { addr = (ushort)tmp; return true; }
                addr = 0; return false;
            }
            ushort u;
            if (ushort.TryParse(s, out u)) { addr = u; return true; }
            addr = 0; return false;
        }

        // -------- Poll loop --------
        private async Task PollOnceSafe()
        {
            if (!_connected || _polling) return;
            _polling = true;
            try
            {
                UiSnap snap = Dispatcher.Invoke(new Func<UiSnap>(GetUiSnap));
                if (string.IsNullOrWhiteSpace(snap.Port)) throw new InvalidOperationException("No COM port selected.");
                await Task.Run(delegate { PollOnce(snap.Port, snap.Baud, snap.Unit, _cts != null ? _cts.Token : CancellationToken.None); });
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

            // ---- Real-time: PV ----
            double? pvV = null, pvA = null, pvW = null; string pvErr = null;
            try
            {
                ushort[] r = ModbusRtuRaw.ReadInputRegisters(port, baud, unit, EpeverRegisters.PV_START, EpeverRegisters.PV_COUNT, 1500);
                pvV = ModbusRtuRaw.S100(r[EpeverRegisters.PV_VOLT]);
                pvA = ModbusRtuRaw.S100(r[EpeverRegisters.PV_CURR]);
                pvW = ModbusRtuRaw.PwrFromU32S100(ModbusRtuRaw.U32(r[EpeverRegisters.PV_PWR_LO], r[EpeverRegisters.PV_PWR_HI]));
            }
            catch (Exception ex) { pvErr = ex.Message; }

            // ---- Battery charge ----
            double? batV = null, batA = null, batW = null; string batErr = null;
            try
            {
                ushort[] r = ModbusRtuRaw.ReadInputRegisters(port, baud, unit, EpeverRegisters.BATC_START, EpeverRegisters.BATC_COUNT, 1500);
                batV = ModbusRtuRaw.S100(r[EpeverRegisters.BATC_VOLT]);
                batA = ModbusRtuRaw.S100(r[EpeverRegisters.BATC_CURR]);
                batW = ModbusRtuRaw.PwrFromU32S100(ModbusRtuRaw.U32(r[EpeverRegisters.BATC_PWR_LO], r[EpeverRegisters.BATC_PWR_HI]));
            }
            catch (Exception ex) { batErr = ex.Message; }

            // ---- Load ----
            double? loadV = null, loadA = null, loadW = null; string loadErr = null;
            try
            {
                ushort[] r = ModbusRtuRaw.ReadInputRegisters(port, baud, unit, EpeverRegisters.LOAD_START, EpeverRegisters.LOAD_COUNT, 1500);
                loadV = ModbusRtuRaw.S100(r[EpeverRegisters.LOAD_VOLT]);
                loadA = ModbusRtuRaw.S100(r[EpeverRegisters.LOAD_CURR]);
                loadW = ModbusRtuRaw.PwrFromU32S100(ModbusRtuRaw.U32(r[EpeverRegisters.LOAD_PWR_LO], r[EpeverRegisters.LOAD_PWR_HI]));
            }
            catch (Exception ex) { loadErr = ex.Message; }

            // ---- SOC ----
            int? soc = null; string socErr = null;
            try { ushort[] s = ModbusRtuRaw.ReadInputRegisters(port, baud, unit, EpeverRegisters.SOC_ADDR, EpeverRegisters.SOC_COUNT, 1500); soc = s[0]; }
            catch (Exception ex) { socErr = ex.Message; }

            // ---- Temperatures ----
            double? tBatt = null, tAmb = null, tCtrl = null; string tErr = null;
            try
            {
                ushort[] t = ModbusRtuRaw.ReadInputRegisters(port, baud, unit, EpeverRegisters.TEMP1_START, EpeverRegisters.TEMP1_COUNT, 1500);
                tBatt = ModbusRtuRaw.S100(t[EpeverRegisters.TEMP1_BATT]);
                tAmb = ModbusRtuRaw.S100(t[EpeverRegisters.TEMP1_AMBIENT]);
                tCtrl = ModbusRtuRaw.S100(t[EpeverRegisters.TEMP1_CTRL]);
            }
            catch (Exception ex1)
            {
                tErr = ex1.Message;
                try { ushort[] r = ModbusRtuRaw.ReadInputRegisters(port, baud, unit, (ushort)(EpeverRegisters.TEMP1_START + 0), 1, 1500); tBatt = ModbusRtuRaw.S100(r[0]); } catch { }
                try { ushort[] r = ModbusRtuRaw.ReadInputRegisters(port, baud, unit, (ushort)(EpeverRegisters.TEMP1_START + 1), 1, 1500); tAmb = ModbusRtuRaw.S100(r[0]); } catch { }
                try { ushort[] r = ModbusRtuRaw.ReadInputRegisters(port, baud, unit, (ushort)(EpeverRegisters.TEMP1_START + 2), 1, 1500); tCtrl = ModbusRtuRaw.S100(r[0]); } catch { }

                if (!tBatt.HasValue && !tAmb.HasValue && !tCtrl.HasValue)
                {
                    try
                    {
                        ushort[] t2 = ModbusRtuRaw.ReadInputRegisters(port, baud, unit, EpeverRegisters.TEMP2_START, EpeverRegisters.TEMP2_COUNT, 1500);
                        tBatt = ModbusRtuRaw.S100(t2[0]);
                        tAmb = ModbusRtuRaw.S100(t2[1]);
                        tCtrl = ModbusRtuRaw.S100(t2[2]);
                        tErr = null;
                    }
                    catch { }
                }
            }

            // ---- Status ----
            string stage = null; string stErr = null; ushort stat3201 = 0;
            try
            {
                ushort[] st = ModbusRtuRaw.ReadInputRegisters(port, baud, unit, EpeverRegisters.STAT_START, EpeverRegisters.STAT_COUNT, 1500);
                stat3201 = st[1];
                stage = EpeverRegisters.DecodeChargingStageFrom3201(stat3201) + "  (0x" + stat3201.ToString("X4") + ")";
            }
            catch (Exception ex) { stErr = ex.Message; }

            // ---- Extremes ----
            double? vMaxToday = null, vMinToday = null; string vExtErr = null;
            try
            {
                ushort[] r = ModbusRtuRaw.ReadInputRegisters(port, baud, unit, EpeverRegisters.EV_BATT_VMAX_TODAY, 2, 1500);
                vMaxToday = ModbusRtuRaw.S100(r[0]); vMinToday = ModbusRtuRaw.S100(r[1]);
            }
            catch (Exception ex) { vExtErr = ex.Message; }

            // ---- Energy ----
            double? genToday = null, genMonth = null, genYear = null, genTotal = null, useToday = null, useMonth = null, useYear = null, useTotal = null, co2Ton = null;
            string energyErr = null;

            try { var r = ModbusRtuRaw.ReadInputRegisters(port, baud, unit, EpeverRegisters.EV_GEN_TODAY_LO, 2, 1500); genToday = ModbusRtuRaw.U32(r[0], r[1]) / 100.0; } catch (Exception ex) { energyErr = ex.Message; }
            try { var r = ModbusRtuRaw.ReadInputRegisters(port, baud, unit, EpeverRegisters.EV_GEN_MONTH_LO, 2, 1500); genMonth = ModbusRtuRaw.U32(r[0], r[1]) / 100.0; } catch { }
            try { var r = ModbusRtuRaw.ReadInputRegisters(port, baud, unit, EpeverRegisters.EV_GEN_YEAR_LO, 2, 1500); genYear = ModbusRtuRaw.U32(r[0], r[1]) / 100.0; } catch { }
            try { var r = ModbusRtuRaw.ReadInputRegisters(port, baud, unit, EpeverRegisters.EV_GEN_TOTAL_LO, 2, 1500); genTotal = ModbusRtuRaw.U32(r[0], r[1]) / 100.0; } catch { }

            try { var r = ModbusRtuRaw.ReadInputRegisters(port, baud, unit, EpeverRegisters.EV_CONS_TODAY_LO, 2, 1500); useToday = ModbusRtuRaw.U32(r[0], r[1]) / 100.0; } catch { }
            try { var r = ModbusRtuRaw.ReadInputRegisters(port, baud, unit, EpeverRegisters.EV_CONS_MONTH_LO, 2, 1500); useMonth = ModbusRtuRaw.U32(r[0], r[1]) / 100.0; } catch { }
            try { var r = ModbusRtuRaw.ReadInputRegisters(port, baud, unit, EpeverRegisters.EV_CONS_YEAR_LO, 2, 1500); useYear = ModbusRtuRaw.U32(r[0], r[1]) / 100.0; } catch { }
            try { var r = ModbusRtuRaw.ReadInputRegisters(port, baud, unit, EpeverRegisters.EV_CONS_TOTAL_LO, 2, 1500); useTotal = ModbusRtuRaw.U32(r[0], r[1]) / 100.0; } catch { }

            try { var r = ModbusRtuRaw.ReadInputRegisters(port, baud, unit, EpeverRegisters.EV_CO2_TON_LO, 2, 1500); co2Ton = ModbusRtuRaw.U32(r[0], r[1]) / 100.0; } catch { }

            // ---- Rated ----
            double? ratedVin = null, ratedChg = null, ratedLoad = null; string ratedErr = null;
            try { var r = ModbusRtuRaw.ReadInputRegisters(port, baud, unit, EpeverRegisters.RATED_INPUT_VOLT, 1, 1500); ratedVin = ModbusRtuRaw.S100(r[0]); } catch (Exception ex) { ratedErr = ex.Message; }
            try { var r = ModbusRtuRaw.ReadInputRegisters(port, baud, unit, EpeverRegisters.RATED_CHG_CURR, 1, 1500); ratedChg = ModbusRtuRaw.S100(r[0]); } catch { }
            try { var r = ModbusRtuRaw.ReadInputRegisters(port, baud, unit, EpeverRegisters.RATED_LOAD_CURR, 1, 1500); ratedLoad = ModbusRtuRaw.S100(r[0]); } catch { }

            // ---- UI update ----
            Dispatcher.Invoke(delegate
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

                TxtGenToday.Text = "Today: " + (genToday.HasValue ? genToday.Value.ToString("F2") + " kWh" : "—");
                TxtGenMonth.Text = "This Month: " + (genMonth.HasValue ? genMonth.Value.ToString("F2") + " kWh" : "—");
                TxtGenYear.Text = "This Year: " + (genYear.HasValue ? genYear.Value.ToString("F2") + " kWh" : "—");
                TxtGenTotal.Text = "Total: " + (genTotal.HasValue ? genTotal.Value.ToString("F2") + " kWh" : "—");

                TxtUseToday.Text = "Today: " + (useToday.HasValue ? useToday.Value.ToString("F2") + " kWh" : "—");
                TxtUseMonth.Text = "This Month: " + (useMonth.HasValue ? useMonth.Value.ToString("F2") + " kWh" : "—");
                TxtUseYear.Text = "This Year: " + (useYear.HasValue ? useYear.Value.ToString("F2") + " kWh" : "—");
                TxtUseTotal.Text = "Total: " + (useTotal.HasValue ? useTotal.Value.ToString("F2") + " kWh" : "—");

                TxtRatedVin.Text = "PV Rated Input Voltage: " + (ratedVin.HasValue ? ratedVin.Value.ToString("F1") + " V" : "—");
                TxtRatedChgA.Text = "Rated Charge Current: " + (ratedChg.HasValue ? ratedChg.Value.ToString("F1") + " A" : "—");
                TxtRatedLoadA.Text = "Rated Load Current: " + (ratedLoad.HasValue ? ratedLoad.Value.ToString("F1") + " A" : "—");

                TxtUpdated.Text = "Last update: " + DateTime.Now.ToString("HH:mm:ss");

                bool anyOk = pvV.HasValue || batV.HasValue || loadV.HasValue || soc.HasValue ||
                             tBatt.HasValue || tAmb.HasValue || tCtrl.HasValue ||
                             genToday.HasValue || useToday.HasValue;

                if (anyOk) { TxtStatus.Text = "OK"; TxtLink.Text = "Polling"; TxtLink.Foreground = (System.Windows.Media.Brush)FindResource("Good"); }
                else { TxtStatus.Text = "No data (check wiring/ID/baud)"; TxtLink.Text = "Connected, but no data"; TxtLink.Foreground = (System.Windows.Media.Brush)FindResource("Warn"); }

                var errs = new List<string>();
                if (pvErr != null) errs.Add("PV: " + pvErr);
                if (batErr != null) errs.Add("BAT: " + batErr);
                if (loadErr != null) errs.Add("LOAD: " + loadErr);
                if (socErr != null) errs.Add("SOC: " + socErr);
                if (stErr != null) errs.Add("STAT: " + stErr);
                bool anyTemp = tBatt.HasValue || tAmb.HasValue || tCtrl.HasValue;
                if (tErr != null && !anyTemp) errs.Add("TEMP: " + tErr);
                if (vExtErr != null) errs.Add("EXTREME: " + vExtErr);
                if (energyErr != null && !(genToday.HasValue || useToday.HasValue)) errs.Add("ENERGY: " + energyErr);
                if (ratedErr != null && !(ratedVin.HasValue || ratedChg.HasValue || ratedLoad.HasValue)) errs.Add("RATED: " + ratedErr);

                if (errs.Count > 0) TxtRaw.Text = string.Join("\n", errs.ToArray());
                else if (string.IsNullOrWhiteSpace(TxtRaw.Text) || TxtRaw.Text.StartsWith("ERR:")) TxtRaw.Text = "OK";
            });
        }

        // -------- Helpers --------
        private async Task RefreshPortsAsync()
        {
            // Show placeholders immediately
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

            // Cancel any in-flight scan
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
                // fallback to plain strings if something went wrong
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
            int b;
            return (item != null && int.TryParse(item.Content.ToString(), out b)) ? b : 115200;
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
            byte parsed; snap.Unit = byte.TryParse(TxtId.Text.Trim(), out parsed) ? parsed : (byte)1;
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
