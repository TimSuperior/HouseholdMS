using System;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using HouseholdMS.Helpers;   // ModbusRtuRaw, EpeverRegisters

namespace HouseholdMS.View.EqTesting
{
    public partial class MpptMiniPanelControl : UserControl
    {
        // ---------- lifecycle / state ----------
        private DispatcherTimer _pollTimer;
        private CancellationTokenSource _loopCts;
        private volatile bool _connected;
        private volatile bool _scanning;
        private volatile bool _polling;
        private int _consecutiveErrors;

        // last good endpoint (static across instances so later pages attach instantly)
        private static string _lastPort;
        private static int _lastBaud;
        private static byte _lastUnit;

        // chosen endpoint
        private string _port;
        private int _baud;
        private byte _unit;

        public MpptMiniPanelControl()
        {
            InitializeComponent();
            DataContext = this;

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            IsVisibleChanged += OnIsVisibleChanged;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (IsVisible) StartAuto();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            StopAll("Unloaded");
        }

        private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (IsVisible)
            {
                if (!_connected && !_scanning) StartAuto();
            }
            else
            {
                StopAll("Hidden");
            }
        }

        // ---------- Auto connect / rescan loop ----------
        private void StartAuto()
        {
            // cancel any prior loop
            try { _loopCts?.Cancel(); _loopCts?.Dispose(); } catch { }
            _loopCts = new CancellationTokenSource();
            var token = _loopCts.Token;

            _connected = false;
            _scanning = true;
            _consecutiveErrors = 0;
            _port = null;

            UpdateRaw("Scanning for MPPT…");

            Task.Run(async () =>
            {
                try
                {
                    // 1) Fast path: try last-known endpoint first
                    if (!string.IsNullOrEmpty(_lastPort) && !_connected && !token.IsCancellationRequested)
                    {
                        if (TryProbeRated(_lastPort, _lastBaud, _lastUnit, 600) || TryProbeBlock3100(_lastPort, _lastBaud, _lastUnit, 600))
                        {
                            SetConnection(_lastPort, _lastBaud, _lastUnit, "Reattached");
                        }
                    }

                    // 2) Full scan if still not connected
                    if (!_connected && !token.IsCancellationRequested)
                    {
                        string[] ports = new string[0];
                        try
                        {
                            ports = SerialPort.GetPortNames()
                                .OrderBy(s =>
                                {
                                    int n;
                                    if (s.StartsWith("COM", StringComparison.OrdinalIgnoreCase) &&
                                        int.TryParse(s.Substring(3), out n)) return n;
                                    return int.MaxValue;
                                })
                                .ThenBy(s => s)
                                .ToArray();
                        }
                        catch (Exception ex)
                        {
                            UpdateRaw("Port scan error: " + ex.Message);
                        }

                        // common tuples for EPEVER (extend if your fleet differs)
                        int[] bauds = new int[] { 115200, 57600, 19200, 9600 };
                        byte[] units = new byte[] { 1, 2, 5, 10, 16 };

                        for (int i = 0; i < ports.Length && !_connected && !token.IsCancellationRequested; i++)
                        {
                            string p = ports[i];
                            for (int b = 0; b < bauds.Length && !_connected && !token.IsCancellationRequested; b++)
                            {
                                int baud = bauds[b];
                                for (int u = 0; u < units.Length && !_connected && !token.IsCancellationRequested; u++)
                                {
                                    byte unit = units[u];

                                    // Safer probe: try rated register (0x3000) first, if fails try a tiny 0x3100 slice.
                                    if (TryProbeRated(p, baud, unit, 700) || TryProbeBlock3100(p, baud, unit, 700))
                                    {
                                        SetConnection(p, baud, unit, "Auto-connected");
                                        break;
                                    }
                                }
                            }
                        }
                    }

                    if (!_connected && !token.IsCancellationRequested)
                    {
                        UpdateRaw("No MPPT found. Re-scanning periodically…");
                        while (!_connected && !token.IsCancellationRequested)
                        {
                            await Task.Delay(2500, token).ConfigureAwait(false);
                            // Quick retry of last-known between scans
                            if (!string.IsNullOrEmpty(_lastPort))
                            {
                                if (TryProbeRated(_lastPort, _lastBaud, _lastUnit, 600) || TryProbeBlock3100(_lastPort, _lastBaud, _lastUnit, 600))
                                {
                                    SetConnection(_lastPort, _lastBaud, _lastUnit, "Recovered");
                                    break;
                                }
                            }
                        }
                    }
                }
                catch (TaskCanceledException) { }
                catch (Exception ex)
                {
                    UpdateRaw("Auto-connect fatal: " + ex.Message);
                }
                finally { _scanning = false; }
            });
        }

        private void SetConnection(string p, int baud, byte unit, string reason)
        {
            _port = p; _baud = baud; _unit = unit;
            _lastPort = p; _lastBaud = baud; _lastUnit = unit;
            _connected = true;

            Dispatcher.Invoke(() =>
            {
                ClearAllSeries(); // no-op now
                UpdateRaw(reason + " → " + _port + " @" + _baud + " (ID=" + _unit + ")");
                StartPolling();
            });
        }

        // Probe using a very-safe rated register (0x3000).
        private bool TryProbeRated(string port, int baud, byte unit, int timeoutMs)
        {
            try
            {
                var r = ModbusRtuRaw.ReadInputRegisters(port, baud, unit, EpeverRegisters.RATED_INPUT_VOLT, 1, timeoutMs);
                if (r == null || r.Length != 1) return false;
                double vin = ModbusRtuRaw.S100(r[0]);
                return vin >= 10.0 && vin <= 200.0; // plausibility
            }
            catch { return false; }
        }

        // Probe with the first 4 regs at 0x3100 (PV V/A/P lo/hi). Much safer than grabbing 0x20 on unknown models.
        private bool TryProbeBlock3100(string port, int baud, byte unit, int timeoutMs)
        {
            try
            {
                const ushort start = 0x3100;
                const ushort count = 4; // only PV slice; avoid exception 2 on strict maps
                var blk = ModbusRtuRaw.ReadInputRegisters(port, baud, unit, start, count, timeoutMs);
                if (blk == null || blk.Length != count) return false;

                Func<ushort, int> off = delegate (ushort abs) { return abs - start; };
                double pvV = ModbusRtuRaw.S100(blk[off(EpeverRegisters.PV_START) + EpeverRegisters.PV_VOLT]);
                double pvA = ModbusRtuRaw.S100(blk[off(EpeverRegisters.PV_START) + EpeverRegisters.PV_CURR]);

                return pvV >= 0.0 && pvV < 200.0 && pvA >= 0.0 && pvA < 50.0;
            }
            catch { return false; }
        }

        // ---------- Polling ----------
        private void StartPolling()
        {
            try { _pollTimer?.Stop(); } catch { }
            _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _pollTimer.Tick += async (s, e) => { await PollOnceSafe(); };
            _pollTimer.Start();
        }

        private async Task PollOnceSafe()
        {
            if (!_connected || _polling) return;
            _polling = true;
            try
            {
                string p = _port;
                if (string.IsNullOrEmpty(p)) { _polling = false; return; }

                await Task.Run(delegate
                {
                    PollOnce(_port, _baud, _unit, _loopCts != null ? _loopCts.Token : CancellationToken.None);
                });
                _consecutiveErrors = 0;
            }
            catch (Exception ex)
            {
                _consecutiveErrors++;
                UpdateRaw("Poll error (" + _consecutiveErrors + "): " + ex.Message);

                // After several consecutive errors (e.g., unplug, address change), rebuild connection.
                if (_consecutiveErrors >= 4)
                {
                    UpdateRaw("Connection unstable → re-scanning…");
                    StopTimersOnly();
                    _connected = false;
                    StartAuto();
                }
            }
            finally { _polling = false; }
        }

        private void PollOnce(string port, int baud, byte unit, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            // Try a wider coalesced block; if the model rejects it, fall back to piecewise reads.
            const ushort blkStart = 0x3100;
            const ushort blkCount = 0x20;

            ushort[] blk = null; string blkErr = null;
            try { blk = ModbusRtuRaw.ReadInputRegisters(port, baud, unit, blkStart, blkCount, 1200); }
            catch (Exception ex) { blkErr = ex.Message; }

            ushort[] stat = null; string statErr = null;
            try { stat = ModbusRtuRaw.ReadInputRegisters(port, baud, unit, EpeverRegisters.STAT_START, EpeverRegisters.STAT_COUNT, 800); }
            catch (Exception ex) { statErr = ex.Message; }

            double? pvV = null, pvA = null, pvW = null;
            double? batV = null, batA = null, batW = null;
            double? loadV = null, loadA = null, loadW = null;
            int? soc = null;
            string stage = null;

            if (blk != null && blk.Length >= blkCount)
            {
                Func<ushort, int> off = delegate (ushort abs) { return abs - blkStart; };

                pvV = ModbusRtuRaw.S100(blk[off(EpeverRegisters.PV_START) + EpeverRegisters.PV_VOLT]);
                pvA = ModbusRtuRaw.S100(blk[off(EpeverRegisters.PV_START) + EpeverRegisters.PV_CURR]);
                pvW = ModbusRtuRaw.PwrFromU32S100(ModbusRtuRaw.U32(
                    blk[off(EpeverRegisters.PV_START) + EpeverRegisters.PV_PWR_LO],
                    blk[off(EpeverRegisters.PV_START) + EpeverRegisters.PV_PWR_HI]));

                batV = ModbusRtuRaw.S100(blk[off(EpeverRegisters.BATC_START) + EpeverRegisters.BATC_VOLT]);
                batA = ModbusRtuRaw.S100(blk[off(EpeverRegisters.BATC_START) + EpeverRegisters.BATC_CURR]);
                batW = ModbusRtuRaw.PwrFromU32S100(ModbusRtuRaw.U32(
                    blk[off(EpeverRegisters.BATC_START) + EpeverRegisters.BATC_PWR_LO],
                    blk[off(EpeverRegisters.BATC_START) + EpeverRegisters.BATC_PWR_HI]));

                loadV = ModbusRtuRaw.S100(blk[off(EpeverRegisters.LOAD_START) + EpeverRegisters.LOAD_VOLT]);
                loadA = ModbusRtuRaw.S100(blk[off(EpeverRegisters.LOAD_START) + EpeverRegisters.LOAD_CURR]);
                loadW = ModbusRtuRaw.PwrFromU32S100(ModbusRtuRaw.U32(
                    blk[off(EpeverRegisters.LOAD_START) + EpeverRegisters.LOAD_PWR_LO],
                    blk[off(EpeverRegisters.LOAD_START) + EpeverRegisters.LOAD_PWR_HI]));

                // SOC may be within this big block depending on model
                int socIdx = off(EpeverRegisters.SOC_ADDR);
                if (socIdx >= 0 && socIdx < blk.Length) soc = blk[socIdx];
            }
            else
            {
                // piecewise fallback
                TryReadPiecewise(port, baud, unit,
                    ref pvV, ref pvA, ref pvW,
                    ref batV, ref batA, ref batW,
                    ref loadV, ref loadA, ref loadW,
                    ref soc);
            }

            if (stat != null && stat.Length >= 2)
            {
                stage = EpeverRegisters.DecodeChargingStageFrom3201(stat[1]);
            }

            // Push to UI
            Dispatcher.Invoke(delegate
            {
                TxtPvV.Text = "Voltage: " + (pvV.HasValue ? pvV.Value.ToString("F2") + " V" : "—");
                TxtPvA.Text = "Current: " + (pvA.HasValue ? pvA.Value.ToString("F2") + " A" : "—");
                TxtPvW.Text = "Power: " + (pvW.HasValue ? pvW.Value.ToString("F1") + " W" : "—");

                TxtBatV.Text = "Voltage: " + (batV.HasValue ? batV.Value.ToString("F2") + " V" : "—");
                TxtBatA.Text = "Current: " + (batA.HasValue ? batA.Value.ToString("F2") + " A" : "—");
                TxtBatW.Text = "Power: " + (batW.HasValue ? batW.Value.ToString("F1") + " W" : "—");

                TxtLoadV.Text = "Voltage: " + (loadV.HasValue ? loadV.Value.ToString("F2") + " V" : "—");
                TxtLoadA.Text = "Current: " + (loadA.HasValue ? loadA.Value.ToString("F2") + " A" : "—");
                TxtLoadW.Text = "Power: " + (loadW.HasValue ? loadW.Value.ToString("F1") + " W" : "—");

                TxtSoc.Text = "SOC: " + (soc.HasValue ? soc.Value + " %" : "—");
                TxtStage.Text = "Stage: " + (stage ?? "—");
                TxtUpdated.Text = "Last update: " + DateTime.Now.ToString("HH:mm:ss");

                string[] errs = new string[]
                {
                    (blkErr != null ? "BLK:" + blkErr : null),
                    (statErr!= null ? "STAT:"+ statErr: null)
                }.Where(s => s != null).ToArray();

                if (errs.Length > 0) UpdateRaw(string.Join("\n", errs));
                else if (string.IsNullOrWhiteSpace(TxtRaw.Text) || TxtRaw.Text.StartsWith("ERR:")) UpdateRaw("OK");
            });
        }

        // Piecewise reads (robust across firmware maps)
        private static void TryReadPiecewise(string port, int baud, byte unit,
            ref double? pvV, ref double? pvA, ref double? pvW,
            ref double? batV, ref double? batA, ref double? batW,
            ref double? loadV, ref double? loadA, ref double? loadW,
            ref int? soc)
        {
            try
            {
                var r = ModbusRtuRaw.ReadInputRegisters(port, baud, unit, EpeverRegisters.PV_START, EpeverRegisters.PV_COUNT, 1000);
                pvV = ModbusRtuRaw.S100(r[EpeverRegisters.PV_VOLT]);
                pvA = ModbusRtuRaw.S100(r[EpeverRegisters.PV_CURR]);
                pvW = ModbusRtuRaw.PwrFromU32S100(ModbusRtuRaw.U32(r[EpeverRegisters.PV_PWR_LO], r[EpeverRegisters.PV_PWR_HI]));
            }
            catch { }
            try
            {
                var r = ModbusRtuRaw.ReadInputRegisters(port, baud, unit, EpeverRegisters.BATC_START, EpeverRegisters.BATC_COUNT, 1000);
                batV = ModbusRtuRaw.S100(r[EpeverRegisters.BATC_VOLT]);
                batA = ModbusRtuRaw.S100(r[EpeverRegisters.BATC_CURR]);
                batW = ModbusRtuRaw.PwrFromU32S100(ModbusRtuRaw.U32(r[EpeverRegisters.BATC_PWR_LO], r[EpeverRegisters.BATC_PWR_HI]));
            }
            catch { }
            try
            {
                var r = ModbusRtuRaw.ReadInputRegisters(port, baud, unit, EpeverRegisters.LOAD_START, EpeverRegisters.LOAD_COUNT, 1000);
                loadV = ModbusRtuRaw.S100(r[EpeverRegisters.LOAD_VOLT]);
                loadA = ModbusRtuRaw.S100(r[EpeverRegisters.LOAD_CURR]);
                loadW = ModbusRtuRaw.PwrFromU32S100(ModbusRtuRaw.U32(r[EpeverRegisters.LOAD_PWR_LO], r[EpeverRegisters.LOAD_PWR_HI]));
            }
            catch { }
            try { var s = ModbusRtuRaw.ReadInputRegisters(port, baud, unit, EpeverRegisters.SOC_ADDR, EpeverRegisters.SOC_COUNT, 700); soc = s[0]; } catch { }
        }

        // ---------- teardown ----------
        private void StopTimersOnly()
        {
            try { _pollTimer?.Stop(); } catch { }
        }

        private void StopAll(string why)
        {
            try { _pollTimer?.Stop(); } catch { }
            try { _loopCts?.Cancel(); } catch { }
            try { _loopCts?.Dispose(); } catch { }

            _loopCts = null;
            _connected = false;
            _scanning = false;
            _polling = false;
            _consecutiveErrors = 0;

            _port = null; _baud = 0; _unit = 0;

            ClearAllSeries(); // no-op now
            UpdateRaw($"Disconnected ({why}).");
        }

        // Kept as a no-op to avoid changing call sites; plotting removed.
        private void ClearAllSeries()
        {
            // intentionally empty
        }

        private void UpdateRaw(string msg)
        {
            try
            {
                if (!Dispatcher.CheckAccess()) Dispatcher.Invoke(delegate { TxtRaw.Text = msg; });
                else TxtRaw.Text = msg;
            }
            catch { }
        }
    }
}
