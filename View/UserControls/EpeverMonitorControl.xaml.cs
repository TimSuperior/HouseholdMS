using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using HouseholdMS.Helpers;
using HouseholdMS.Services; // for PortDescriptor / SerialPortInspector
using Syncfusion.UI.Xaml.Charts;   // axes + behaviors
using System.Windows.Input;
using HouseholdMS.Resources; // <-- for Strings.*

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

        // ---- chart data (simple time/value) ----
        public sealed class TimePoint { public DateTime Time { get; set; } public double Value { get; set; } }
        private const int MaxPoints = 1000;

        public ObservableCollection<TimePoint> VoltSolar { get; } = new ObservableCollection<TimePoint>();
        public ObservableCollection<TimePoint> VoltBattery { get; } = new ObservableCollection<TimePoint>();
        public ObservableCollection<TimePoint> VoltLoad { get; } = new ObservableCollection<TimePoint>();

        public ObservableCollection<TimePoint> CurrSolar { get; } = new ObservableCollection<TimePoint>();
        public ObservableCollection<TimePoint> CurrBattery { get; } = new ObservableCollection<TimePoint>();
        public ObservableCollection<TimePoint> CurrLoad { get; } = new ObservableCollection<TimePoint>();

        public ObservableCollection<TimePoint> PowerSolar { get; } = new ObservableCollection<TimePoint>();
        public ObservableCollection<TimePoint> PowerBattery { get; } = new ObservableCollection<TimePoint>();
        public ObservableCollection<TimePoint> PowerLoad { get; } = new ObservableCollection<TimePoint>();

        public EpeverMonitorControl()
        {
            InitializeComponent();
            DataContext = this; // bind chart series
            _ = RefreshPortsAsync();
            TxtStatus.Text = Strings.EPEV_Status_InitHint;
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
                MessageBox.Show(Strings.EPEV_Msg_SelectPort, Strings.EPEV_Msg_Title, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (!byte.TryParse(TxtId.Text.Trim(), out byte unitId) || unitId < 1 || unitId > 247)
            {
                MessageBox.Show(Strings.EPEV_Msg_InvalidUnitId, Strings.EPEV_Msg_Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _cts = new CancellationTokenSource();
            _connected = true;
            SetUiState(true);

            ClearAllSeries();

            _timer = new DispatcherTimer { Interval = GetSelectedInterval() };
            _timer.Tick += (s, ev) => { if (ChkAuto.IsChecked == true) _ = PollOnceSafe(); };
            _timer.Start();

            var selectedText = CmbPorts.SelectedItem?.ToString() ?? portName;
            TxtLink.Text = Strings.EPEV_Link_ConnectedIdle;
            TxtLink.Foreground = (System.Windows.Media.Brush)FindResource("Warn");
            TxtStatus.Text = string.Format(Strings.EPEV_Status_ConnectedFmt, selectedText, GetBaudUI(), unitId);

            // Device identification (background)
            _ = Task.Run(() => QueryDeviceIdentification(portName, GetBaudUI(), unitId, _cts.Token));

            // First poll
            _ = PollOnceSafe();
        }

        private void BtnDisconnect_Click(object sender, RoutedEventArgs e) => Disconnect();
        private async void BtnRead_Click(object sender, RoutedEventArgs e) => await PollOnceSafe();

        // -------- Inspector --------
        private void BtnInspectorRead_Click(object sender, RoutedEventArgs e)
        {
            if (!_connected) { MessageBox.Show(Strings.EPEV_Msg_ConnectFirst, Strings.EPEV_Msg_Title, MessageBoxButton.OK, MessageBoxImage.Information); return; }

            if (!TryParseAddress(TxtInspectorStart.Text.Trim(), out ushort start))
            {
                MessageBox.Show(Strings.EPEV_Msg_InvalidStart, Strings.EPEV_Msg_Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!ushort.TryParse(TxtInspectorCount.Text.Trim(), out ushort count) || count < 1 || count > 60)
            {
                MessageBox.Show(Strings.EPEV_Msg_CountRange, Strings.EPEV_Msg_Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            UiSnap snap = UiRead(GetUiSnap);
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
                UiSnap snap = UiRead(GetUiSnap);
                if (string.IsNullOrWhiteSpace(snap.Port)) throw new InvalidOperationException(Strings.EPEV_Msg_SelectPort);
                await Task.Run(() => PollOnce(snap.Port, snap.Baud, snap.Unit, _cts != null ? _cts.Token : CancellationToken.None));
            }
            catch (Exception ex)
            {
                TxtStatus.Text = Strings.Error_UnexpectedPrefix + " " + ex.Message; // "An unexpected error occurred: ..."
                TxtLink.Text = Strings.EPEV_Link_CommError;
                TxtLink.Foreground = (System.Windows.Media.Brush)FindResource("Bad");
                TxtRaw.Text = "ERR: " + ex.Message;
            }
            finally { _polling = false; }
        }

        private void PollOnce(string port, int baud, byte unit, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            // ---- Coalesced block A: 0x3100.. (32 regs) ----
            ushort blkAStart = 0x3100;
            ushort blkACount = 0x20;
            ushort[] blkA = null; string blkAErr = null;
            try { blkA = ModbusRtuRaw.ReadInputRegisters(port, baud, unit, blkAStart, blkACount, 1200); }
            catch (Exception ex) { blkAErr = ex.Message; }

            // ---- Status block: 0x3200..0x3201 ----
            ushort[] stat = null; string statErr = null;
            try { stat = ModbusRtuRaw.ReadInputRegisters(port, baud, unit, EpeverRegisters.STAT_START, EpeverRegisters.STAT_COUNT, 800); }
            catch (Exception ex) { statErr = ex.Message; }

            // ---- Extremes: 0x3302..0x3303 ----
            ushort[] ext = null; string extErr = null;
            try { ext = ModbusRtuRaw.ReadInputRegisters(port, baud, unit, EpeverRegisters.EV_BATT_VMAX_TODAY, 2, 800); }
            catch (Exception ex) { extErr = ex.Message; }

            // ---- Rated values ----
            ushort[] ratedVinRaw = null, ratedChgRaw = null, ratedLoadRaw = null;
            string ratedErr = null;
            try { ratedVinRaw = ModbusRtuRaw.ReadInputRegisters(port, baud, unit, EpeverRegisters.RATED_INPUT_VOLT, 1, 600); } catch (Exception ex) { ratedErr = ex.Message; }
            try { ratedChgRaw = ModbusRtuRaw.ReadInputRegisters(port, baud, unit, EpeverRegisters.RATED_CHG_CURR, 1, 600); } catch { }
            try { ratedLoadRaw = ModbusRtuRaw.ReadInputRegisters(port, baud, unit, EpeverRegisters.RATED_LOAD_CURR, 1, 600); } catch { }

            // ---- Parse block A ----
            double? pvV = null, pvA = null, pvW = null;
            double? batV = null, batA = null, batW = null;
            double? loadV = null, loadA = null, loadW = null;
            double? tBatt = null, tAmb = null, tCtrl = null;
            int? soc = null;

            if (blkA != null && blkA.Length >= blkACount)
            {
                int off(ushort abs) => abs - blkAStart;

                pvV = ModbusRtuRaw.S100(blkA[off(EpeverRegisters.PV_START) + EpeverRegisters.PV_VOLT]);
                pvA = ModbusRtuRaw.S100(blkA[off(EpeverRegisters.PV_START) + EpeverRegisters.PV_CURR]);
                pvW = ModbusRtuRaw.PwrFromU32S100(ModbusRtuRaw.U32(blkA[off(EpeverRegisters.PV_START) + EpeverRegisters.PV_PWR_LO],
                                                                   blkA[off(EpeverRegisters.PV_START) + EpeverRegisters.PV_PWR_HI]));

                batV = ModbusRtuRaw.S100(blkA[off(EpeverRegisters.BATC_START) + EpeverRegisters.BATC_VOLT]);
                batA = ModbusRtuRaw.S100(blkA[off(EpeverRegisters.BATC_START) + EpeverRegisters.BATC_CURR]);
                batW = ModbusRtuRaw.PwrFromU32S100(ModbusRtuRaw.U32(blkA[off(EpeverRegisters.BATC_START) + EpeverRegisters.BATC_PWR_LO],
                                                                    blkA[off(EpeverRegisters.BATC_START) + EpeverRegisters.BATC_PWR_HI]));

                loadV = ModbusRtuRaw.S100(blkA[off(EpeverRegisters.LOAD_START) + EpeverRegisters.LOAD_VOLT]);
                loadA = ModbusRtuRaw.S100(blkA[off(EpeverRegisters.LOAD_START) + EpeverRegisters.LOAD_CURR]);
                loadW = ModbusRtuRaw.PwrFromU32S100(ModbusRtuRaw.U32(blkA[off(EpeverRegisters.LOAD_START) + EpeverRegisters.LOAD_PWR_LO],
                                                                     blkA[off(EpeverRegisters.LOAD_START) + EpeverRegisters.LOAD_PWR_HI]));

                try { tBatt = ModbusRtuRaw.S100(blkA[off(EpeverRegisters.TEMP1_START) + EpeverRegisters.TEMP1_BATT]); } catch { }
                try { tAmb = ModbusRtuRaw.S100(blkA[off(EpeverRegisters.TEMP1_START) + EpeverRegisters.TEMP1_AMBIENT]); } catch { }
                try { tCtrl = ModbusRtuRaw.S100(blkA[off(EpeverRegisters.TEMP1_START) + EpeverRegisters.TEMP1_CTRL]); } catch { }

                int socIdx = off(EpeverRegisters.SOC_ADDR);
                if (socIdx >= 0 && socIdx < blkA.Length) soc = blkA[socIdx];
            }
            else
            {
                TryReadPiecewise(port, baud, unit, ref pvV, ref pvA, ref pvW, ref batV, ref batA, ref batW,
                                 ref loadV, ref loadA, ref loadW, ref tBatt, ref tAmb, ref tCtrl, ref soc);
            }

            string stage = null;
            if (stat != null && stat.Length >= 2)
            {
                ushort reg3201 = stat[1];
                stage = EpeverRegisters.DecodeChargingStageFrom3201(reg3201) + $"  (0x{reg3201:X4})";
            }

            double? vMaxToday = null, vMinToday = null;
            if (ext != null && ext.Length >= 2)
            {
                vMaxToday = ModbusRtuRaw.S100(ext[0]);
                vMinToday = ModbusRtuRaw.S100(ext[1]);
            }

            double? ratedVin = ratedVinRaw != null ? ModbusRtuRaw.S100(ratedVinRaw[0]) : (double?)null;
            double? ratedChg = ratedChgRaw != null ? ModbusRtuRaw.S100(ratedChgRaw[0]) : (double?)null;
            double? ratedLoad = ratedLoadRaw != null ? ModbusRtuRaw.S100(ratedLoadRaw[0]) : (double?)null;

            Dispatcher.Invoke(() =>
            {
                // units
                string uV = Strings.EPEV_Unit_V;
                string uA = Strings.EPEV_Unit_A;
                string uW = Strings.EPEV_Unit_W;
                string uC = Strings.EPEV_Unit_Cdeg;
                string dash = Strings.Common_NoDataDash;

                // PV
                TxtPvV.Text = $"{Strings.EPEV_Label_Voltage} " + (pvV.HasValue ? pvV.Value.ToString("F2") + uV : dash);
                TxtPvA.Text = $"{Strings.EPEV_Label_Current} " + (pvA.HasValue ? pvA.Value.ToString("F2") + uA : dash);
                TxtPvW.Text = $"{Strings.EPEV_Label_Power} " + (pvW.HasValue ? pvW.Value.ToString("F1") + uW : dash);

                // Battery
                TxtBatV.Text = $"{Strings.EPEV_Label_Voltage} " + (batV.HasValue ? batV.Value.ToString("F2") + uV : dash);
                TxtBatA.Text = $"{Strings.EPEV_Label_ChargeCurrent} " + (batA.HasValue ? batA.Value.ToString("F2") + uA : dash);
                TxtBatW.Text = $"{Strings.EPEV_Label_ChargePower} " + (batW.HasValue ? batW.Value.ToString("F1") + uW : dash);

                // Load
                TxtLoadV.Text = $"{Strings.EPEV_Label_Voltage} " + (loadV.HasValue ? loadV.Value.ToString("F2") + uV : dash);
                TxtLoadA.Text = $"{Strings.EPEV_Label_Current} " + (loadA.HasValue ? loadA.Value.ToString("F2") + uA : dash);
                TxtLoadW.Text = $"{Strings.EPEV_Label_Power} " + (loadW.HasValue ? loadW.Value.ToString("F1") + uW : dash);

                // State / temps / extremes / rated
                TxtSoc.Text = $"{Strings.EPEV_Label_SOC} " + (soc.HasValue ? soc.Value + Strings.EPEV_SocPercent : dash);
                TxtStage.Text = $"{Strings.EPEV_Label_ChargeStage} " + (stage ?? dash);
                TxtTempBatt.Text = $"{Strings.EPEV_Temp_Battery} " + (tBatt.HasValue ? tBatt.Value.ToString("F2") + uC : dash);
                TxtTempAmb.Text = $"{Strings.EPEV_Temp_Ambient} " + (tAmb.HasValue ? tAmb.Value.ToString("F2") + uC : dash);
                TxtTempCtrl.Text = $"{Strings.EPEV_Temp_Controller} " + (tCtrl.HasValue ? tCtrl.Value.ToString("F2") + uC : dash);

                TxtBattVmaxToday.Text = $"{Strings.EPEV_Today_BattVmax} " + (vMaxToday.HasValue ? vMaxToday.Value.ToString("F2") + uV : dash);
                TxtBattVminToday.Text = $"{Strings.EPEV_Today_BattVmin} " + (vMinToday.HasValue ? vMinToday.Value.ToString("F2") + uV : dash);

                TxtRatedVin.Text = $"{Strings.EPEV_Rated_PvRatedInputVoltage} " + (ratedVin.HasValue ? ratedVin.Value.ToString("F1") + uV : dash);
                TxtRatedChgA.Text = $"{Strings.EPEV_Rated_ChargeCurrent} " + (ratedChg.HasValue ? ratedChg.Value.ToString("F1") + uA : dash);
                TxtRatedLoadA.Text = $"{Strings.EPEV_Rated_LoadCurrent} " + (ratedLoad.HasValue ? ratedLoad.Value.ToString("F1") + uA : dash);

                TxtUpdated.Text = $"{Strings.EPEV_Label_LastUpdate} " + DateTime.Now.ToString("HH:mm:ss");

                bool anyOk = pvV.HasValue || batV.HasValue || loadV.HasValue || soc.HasValue ||
                             tBatt.HasValue || tAmb.HasValue || tCtrl.HasValue || vMaxToday.HasValue;

                if (anyOk) { TxtStatus.Text = Strings.EPEV_Status_OK; TxtLink.Text = Strings.EPEV_Link_Polling; TxtLink.Foreground = (System.Windows.Media.Brush)FindResource("Good"); }
                else { TxtStatus.Text = "No data (check wiring/ID/baud)"; TxtLink.Text = Strings.EPEV_Link_ConnectedNoData; TxtLink.Foreground = (System.Windows.Media.Brush)FindResource("Warn"); }

                var errs = new List<string>();
                if (blkAErr != null) errs.Add("BlockA: " + blkAErr);
                if (statErr != null) errs.Add("STAT: " + statErr);
                if (extErr != null) errs.Add("EXTREME: " + extErr);
                if (ratedErr != null && !(ratedVin.HasValue || ratedChg.HasValue || ratedLoad.HasValue)) errs.Add("RATED: " + ratedErr);

                if (errs.Count > 0) TxtRaw.Text = string.Join("\n", errs);
                else if (string.IsNullOrWhiteSpace(TxtRaw.Text) || TxtRaw.Text.StartsWith("ERR:")) TxtRaw.Text = "OK";

                // feed charts
                var now = DateTime.Now;
                if (pvV.HasValue) AppendPoint(VoltSolar, now, pvV.Value);
                if (batV.HasValue) AppendPoint(VoltBattery, now, batV.Value);
                if (loadV.HasValue) AppendPoint(VoltLoad, now, loadV.Value);

                if (pvA.HasValue) AppendPoint(CurrSolar, now, pvA.Value);
                if (batA.HasValue) AppendPoint(CurrBattery, now, batA.Value);
                if (loadA.HasValue) AppendPoint(CurrLoad, now, loadA.Value);

                if (pvW.HasValue) AppendPoint(PowerSolar, now, pvW.Value);
                if (batW.HasValue) AppendPoint(PowerBattery, now, batW.Value);
                if (loadW.HasValue) AppendPoint(PowerLoad, now, loadW.Value);
            });
        }

        private static void AppendPoint(ObservableCollection<TimePoint> col, DateTime t, double v)
        {
            col.Add(new TimePoint { Time = t, Value = v });
            if (col.Count > MaxPoints) col.RemoveAt(0);
        }

        // -------- Piecewise reads (unchanged) --------
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

        // -------- Device ID (unchanged logic; localized labels) --------
        private void QueryDeviceIdentification(string port, int baud, byte unit, CancellationToken ct)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                string error;
                var dict = ModbusRtuRaw.TryReadDeviceIdentification(port, baud, unit, ModbusRtuRaw.DeviceIdCategory.Basic, 1500, out error);
                if (dict == null)
                {
                    Dispatcher.Invoke(() =>
                    {
                        TxtDevVendor.Text = $"{Strings.EPEV_Device_VendorLabel} {Strings.Common_NoDataDash}";
                        TxtDevProduct.Text = $"{Strings.EPEV_Device_ProductLabel} {Strings.Common_NoDataDash}";
                        TxtDevFw.Text = $"{Strings.EPEV_Device_FirmwareLabel} {Strings.Common_NoDataDash}";
                        if (!string.IsNullOrWhiteSpace(error)) TxtRaw.Text = "DevID ERR: " + error;
                    });
                    return;
                }

                dict.TryGetValue(0x00, out string vendor);
                dict.TryGetValue(0x01, out string product);
                dict.TryGetValue(0x02, out string fw);

                Dispatcher.Invoke(() =>
                {
                    TxtDevVendor.Text = $"{Strings.EPEV_Device_VendorLabel} " + (string.IsNullOrWhiteSpace(vendor) ? Strings.Common_NoDataDash : vendor);
                    TxtDevProduct.Text = $"{Strings.EPEV_Device_ProductLabel} " + (string.IsNullOrWhiteSpace(product) ? Strings.Common_NoDataDash : product);
                    TxtDevFw.Text = $"{Strings.EPEV_Device_FirmwareLabel} " + (string.IsNullOrWhiteSpace(fw) ? Strings.Common_NoDataDash : fw);
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    TxtDevVendor.Text = $"{Strings.EPEV_Device_VendorLabel} {Strings.Common_NoDataDash}";
                    TxtDevProduct.Text = $"{Strings.EPEV_Device_ProductLabel} {Strings.Common_NoDataDash}";
                    TxtDevFw.Text = $"{Strings.EPEV_Device_FirmwareLabel} {Strings.Common_NoDataDash}";
                    TxtRaw.Text = "DevID ERR: " + ex.Message;
                });
            }
        }

        // ===== Chart UX =====
        private void Chart_MouseWheel(object sender, MouseWheelEventArgs e) { e.Handled = true; }

        private void BtnResetZoom_Click(object sender, RoutedEventArgs e)
        {
            switch (TabsRealtime.SelectedIndex)
            {
                case 0: ResetAxes(VoltXAxis, VoltYAxis); break;
                case 1: ResetAxes(CurrXAxis, CurrYAxis); break;
                case 2: ResetAxes(PowerXAxis, PowerYAxis); break;
            }
        }
        private void TabsRealtime_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            BtnResetZoom_Click(null, null);
        }
        private static void ResetAxes(ChartAxisBase2D xAxis, ChartAxisBase2D yAxis)
        {
            if (xAxis != null) { xAxis.ZoomFactor = 1; xAxis.ZoomPosition = 0; }
            if (yAxis != null) { yAxis.ZoomFactor = 1; yAxis.ZoomPosition = 0; }
        }

        // -------- Helpers --------
        private T UiRead<T>(Func<T> read)
        {
            if (Dispatcher.CheckAccess()) return read();
            return Dispatcher.Invoke(read);
        }

        private async Task RefreshPortsAsync()
        {
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

            try { _portScanCts?.Cancel(); _portScanCts?.Dispose(); } catch { }
            _portScanCts = new CancellationTokenSource();

            try
            {
                var scanned = await SerialPortInspector.GetOrProbeAsync(
                    attemptTimeoutMs: 200,
                    cacheTtlMs: 15000,
                    ct: _portScanCts.Token);

                CmbPorts.ItemsSource = scanned;
                if (CmbPorts.Items.Count > 0) CmbPorts.SelectedIndex = 0;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                var ports = SerialPort.GetPortNames().ToList();
                CmbPorts.ItemsSource = ports;
                if (ports.Count > 0) CmbPorts.SelectedIndex = 0;
                TxtStatus.Text = string.Format(Strings.MEAS_Status_PortScanErrorFmt, ex.Message);
            }
        }

        private string ExtractPortName(object selected)
        {
            if (selected is PortDescriptor pd) return pd.Port;
            return selected?.ToString();
        }

        private int GetBaudUI()
        {
            return UiRead(() =>
            {
                var item = CmbBaud.SelectedItem as ComboBoxItem;
                if (item == null) return 115200;
                return int.TryParse(item.Content?.ToString(), out var b) ? b : 115200;
            });
        }

        private TimeSpan GetSelectedInterval()
        {
            var item = CmbInterval.SelectedItem as ComboBoxItem;
            var label = item != null ? item.Content.ToString() : Strings.EPEV_Interval_1s;
            if (label == Strings.EPEV_Interval_2s) return TimeSpan.FromSeconds(2);
            if (label == Strings.EPEV_Interval_5s) return TimeSpan.FromSeconds(5);
            return TimeSpan.FromSeconds(1);
        }

        private UiSnap GetUiSnap()
        {
            return UiRead(() =>
            {
                var snap = new UiSnap();
                object sel = CmbPorts.SelectedItem;
                snap.Port = (sel is PortDescriptor pd) ? pd.Port : sel?.ToString();
                snap.Baud = GetBaudUI();
                snap.Unit = byte.TryParse(TxtId.Text.Trim(), out var parsed) ? parsed : (byte)1;
                return snap;
            });
        }

        private void Disconnect()
        {
            if (_timer != null) _timer.Stop();
            if (_cts != null) _cts.Cancel();
            _cts?.Dispose();
            _cts = null;
            _connected = false;
            SetUiState(false);
            TxtLink.Text = Strings.EPEV_Link_Disconnected;
            TxtLink.Foreground = (System.Windows.Media.Brush)FindResource("Bad");
            TxtStatus.Text = Strings.EPEV_Disconnected;

            // Clear device info
            TxtDevVendor.Text = $"{Strings.EPEV_Device_VendorLabel} {Strings.Common_NoDataDash}";
            TxtDevProduct.Text = $"{Strings.EPEV_Device_ProductLabel} {Strings.Common_NoDataDash}";
            TxtDevFw.Text = $"{Strings.EPEV_Device_FirmwareLabel} {Strings.Common_NoDataDash}";

            ClearAllSeries();

            // Reset zoom for all tabs so next session starts clean
            ResetAxes(VoltXAxis, VoltYAxis);
            ResetAxes(CurrXAxis, CurrYAxis);
            ResetAxes(PowerXAxis, PowerYAxis);
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

        private void ClearAllSeries()
        {
            VoltSolar.Clear(); VoltBattery.Clear(); VoltLoad.Clear();
            CurrSolar.Clear(); CurrBattery.Clear(); CurrLoad.Clear();
            PowerSolar.Clear(); PowerBattery.Clear(); PowerLoad.Clear();
        }
    }
}
