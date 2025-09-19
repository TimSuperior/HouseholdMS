using HouseholdMS.Services;
using Syncfusion.UI.Xaml.Charts;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace HouseholdMS.View.Measurement
{
    public partial class Tbs1000cView : UserControl
    {
        private sealed class DataPoint
        {
            public double XValue { get; set; }
            public double YValue { get; set; }
            public DataPoint() { }
            public DataPoint(double x, double y) { XValue = x; YValue = y; }
        }

        // I/O
        private IScpiTransport _transport;
        private Tbs1000cCommandScheduler _sched;
        private readonly Tbs1000cStateCache _cache = new Tbs1000cStateCache();
        private string _visaResource;

        // Connection health flag (only start live when true)
        private volatile bool _connOk;

        // Chart data (time-domain)
        private readonly Dictionary<string, IList<DataPoint>> _timeSeries =
            new Dictionary<string, IList<DataPoint>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, FastLineSeries> _timeSeriesHandle =
            new Dictionary<string, FastLineSeries>(StringComparer.OrdinalIgnoreCase);

        // Measurement mini-charts
        private readonly Dictionary<string, ObservableCollection<DataPoint>> _measSeries =
            new Dictionary<string, ObservableCollection<DataPoint>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, FastLineSeries> _measSeriesHandle =
            new Dictionary<string, FastLineSeries>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, TextBlock> _measValueLabels =
            new Dictionary<string, TextBlock>(StringComparer.OrdinalIgnoreCase);
        private DateTime _measStartUtc = DateTime.UtcNow;
        private const int _measMaxPoints = 120;

        // Live polling
        private DispatcherTimer _liveTimer;
        private bool _liveBusy;
        private bool _livePaused;

        // Coarse gate so waveform transfers and measurement bursts never overlap
        private readonly SemaphoreSlim _opGate = new SemaphoreSlim(1, 1);

        // last data ranges for auto-fit
        private double _lastMinX, _lastMaxX, _lastMinY, _lastMaxY;

        // light rate limit for log spam during live plot
        private DateTime _lastMeasAtUtc = DateTime.MinValue;

        // Mini graphs set
        private static readonly string[] MiniTypes = new[]
        {
            "FREQuency","HIGH","LOW","MAXimum","MEAN","MEDian","MINImum",
            "NDUty","NWIdth","OVershoot","PDUty","PERIod","PHAse","PK2Pk",
            "PWIdth","RISe","RMS","UNdershoot","ZEROCross"
        };

        public Tbs1000cView()
        {
            InitializeComponent();

            Loaded += (_, __) =>
            {
                SrcCh1.IsChecked = true;
                SrcCh2.IsChecked = false;
                // Ensure both Checked and Unchecked go through the same path even if XAML missed wiring
                if (CurEnable != null)
                {
                    CurEnable.Checked -= CurEnable_CheckedChanged;
                    CurEnable.Unchecked -= CurEnable_CheckedChanged;
                    CurEnable.Checked += CurEnable_CheckedChanged;
                    CurEnable.Unchecked += CurEnable_CheckedChanged;
                }
                TryEnsureMiniCharts();
            };
            Unloaded += (_, __) => SafeShutdown();
        }

        public void Initialize(IScpiTransport transport, string displayName = null)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _transport.TimeoutMs = 3000;
            _sched?.Dispose();
            _sched = new Tbs1000cCommandScheduler(_transport);
            _connOk = false; // will probe below
            Append(">>> Initialized transport" + (string.IsNullOrWhiteSpace(displayName) ? "" : (" [" + displayName + "]")) + ".");
            TryEnsureMiniCharts();
            // Probe and only then start live
            _ = VerifyAndMaybeStartLiveAsync();
        }

        #region Connection

        private async void Connect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // If we already have a scheduler/transport, just verify before starting live
                if (_sched != null && _transport != null)
                {
                    Append("Already connected. Verifying device responsiveness...");
                    await VerifyAndMaybeStartLiveAsync();
                    return;
                }

                if (_transport != null && _sched == null)
                {
                    _sched = new Tbs1000cCommandScheduler(_transport);
                    Append(">>> Connected using injected transport (pending verification)...");
                    await VerifyAndMaybeStartLiveAsync();
                    return;
                }

                // Open first VISA USB (prefer Tek)
                _transport = VisaComTransport.OpenFirstTekUsb(out _visaResource, timeoutMs: 3000);
                _sched = new Tbs1000cCommandScheduler(_transport);
                Append(">>> Connected via VISA: " + _visaResource + " (pending verification)...");
                await VerifyAndMaybeStartLiveAsync();
            }
            catch (Exception ex)
            {
                Append("CONNECT ERROR: " + ex.Message);
                SafeShutdown();
            }
        }

        private void Disconnect_Click(object sender, RoutedEventArgs e)
        {
            SafeShutdown();
            Append(">>> Disconnected.");
        }

        private void SafeShutdown()
        {
            StopLiveInternal();
            _connOk = false;

            try { _sched?.Dispose(); } catch { }
            _sched = null;

            try { (_transport as IDisposable)?.Dispose(); } catch { }
            _transport = null;
            _cache.Clear();
        }

        #endregion

        #region Helpers

        private void RequireConn()
        {
            if (_sched == null) throw new InvalidOperationException("Not connected. Click Connect first.");
        }

        private async Task<bool> ProbeInstrumentAsync(int timeoutMs = 1500)
        {
            try
            {
                RequireConn();
                var s = _sched;
                if (s == null) return false;

                string idn = await s.EnqueueQueryAsync("*IDN?", timeoutMs: timeoutMs, retries: 1);
                if (!string.IsNullOrWhiteSpace(idn))
                {
                    Append("*IDN? -> " + idn.Trim());
                    return true;
                }
            }
            catch (Exception ex)
            {
                Append("CONNECT PROBE ERR: " + ex.Message);
            }
            return false;
        }

        private async Task VerifyAndMaybeStartLiveAsync()
        {
            _connOk = await ProbeInstrumentAsync(2000);
            if (_connOk)
            {
                TryEnsureMiniCharts();
                EnsureLiveRunning();
            }
            else
            {
                Append("No response from instrument. Live plotting will not start.");
                StopLiveInternal();
            }
        }

        private static string ComboText(ComboBox cb)
        {
            var it = cb?.SelectedItem as ComboBoxItem;
            return ((it != null ? it.Content : (object)(cb?.Text ?? "")) ?? "").ToString().Trim();
        }

        private static double GetDouble(TextBox tb, double def = 0)
        {
            double v;
            return double.TryParse(tb == null ? "" : tb.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : def;
        }

        private static int GetInt(TextBox tb, int def = 0)
        {
            int v;
            return int.TryParse(tb == null ? "" : tb.Text, out v) ? v : def;
        }

        private void Send(string cmd, bool waitOpc = false)
        {
            var s = _sched;
            if (s == null) return;
            s.EnqueueWrite(cmd, waitOpc: waitOpc, onError: ex => Append("ERR[" + cmd + "]: " + ex.Message));
        }

        private void Append(string line)
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (ConsoleOut == null) return;
                ConsoleOut.AppendText(line + Environment.NewLine);
                ConsoleOut.ScrollToEnd();
            }, DispatcherPriority.Background);
        }

        private static string OnOff(bool b) => b ? "ON" : "OFF";

        private static double[] ParseCsvToDouble(string csv)
        {
            var parts = (csv ?? "").Split(new[] { ',', ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            var arr = new double[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                double v;
                arr[i] = double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : double.NaN;
            }
            return arr;
        }

        private static bool TryParseDouble(string s, out double v)
            => double.TryParse((s ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v);

        private static string Key(string ch, string type) => ch.ToUpperInvariant() + "|" + type.ToUpperInvariant();

        #endregion

        #region System Buttons

        private void BtnIdn_Click(object sender, RoutedEventArgs e) { try { RequireConn(); var s = _sched; if (s == null) return; s.EnqueueQuery("*IDN?", r => Append("*IDN? -> " + (r == null ? "" : r.Trim()))); } catch (Exception ex) { Append(ex.Message); } }
        private void BtnRst_Click(object sender, RoutedEventArgs e) { try { RequireConn(); Send("*RST", waitOpc: true); Append("Instrument reset."); } catch (Exception ex) { Append(ex.Message); } }
        private void BtnCls_Click(object sender, RoutedEventArgs e) { try { RequireConn(); Send("*CLS"); Append("Status/event queues cleared."); } catch (Exception ex) { Append(ex.Message); } }
        private void BtnAutoset_Click(object sender, RoutedEventArgs e) { try { RequireConn(); Send("AUTOSet EXECute", waitOpc: true); Append("Autoset executed."); } catch (Exception ex) { Append(ex.Message); } }

        #endregion

        #region Acquisition / Channel / Timebase / Trigger

        private void BtnRun_Click(object sender, RoutedEventArgs e) { try { RequireConn(); Send("ACQuire:STATE RUN"); } catch (Exception ex) { Append(ex.Message); } }
        private void BtnStop_Click(object sender, RoutedEventArgs e) { try { RequireConn(); Send("ACQuire:STATE STOP"); } catch (Exception ex) { Append(ex.Message); } }
        private void BtnSingle_Click(object sender, RoutedEventArgs e) { try { RequireConn(); Send("ACQuire:STOPAfter SEQuence"); Send("ACQuire:STATE RUN"); } catch (Exception ex) { Append(ex.Message); } }

        private void BtnAcqApply2_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                RequireConn();
                var mode = ComboText(AcqMode2).ToUpperInvariant();
                if (_cache.ShouldSend("ACQ:MODE", mode)) Send("ACQuire:MODe " + mode);
                if (mode.StartsWith("AVER"))
                {
                    var avg = GetInt(AcqNumAvg2, 16);
                    if (_cache.ShouldSend("ACQ:NUMAV", avg.ToString()))
                        Send("ACQuire:NUMAVg " + avg);
                }
            }
            catch (Exception ex) { Append(ex.Message); }
        }

        private void BtnTbApply2_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                RequireConn();
                Send("HORizontal:MAIn:SCAle " + GetDouble(TbScale2, 1e-3).ToString("G", CultureInfo.InvariantCulture));
                Send("HORizontal:MAIn:POSition " + GetDouble(TbPos2, 0).ToString("G", CultureInfo.InvariantCulture));
            }
            catch (Exception ex) { Append(ex.Message); }
        }
        private void BtnTbRecord_Click(object sender, RoutedEventArgs e)
        { try { RequireConn(); Send("HORizontal:RECOrdlength " + GetInt(TbRecord, 2000)); } catch (Exception ex) { Append(ex.Message); } }
        private void BtnTbRoll_Click(object sender, RoutedEventArgs e)
        { try { RequireConn(); Send("HORizontal:ROLL " + OnOff(TbRoll.IsChecked == true)); } catch (Exception ex) { Append(ex.Message); } }
        private async void BtnTbSrq_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                RequireConn();
                var s = _sched; if (s == null) return;
                var xincrStr = await s.EnqueueQueryAsync("WFMOutpre:XINcr?");
                if (double.TryParse((xincrStr ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var xincr) && xincr > 0)
                    Append("Sample rate ≈ " + (1.0 / xincr).ToString("G6", CultureInfo.InvariantCulture) + " Sa/s");
                else
                    Append("Could not read XINCR (is the source channel displayed?).");
            }
            catch (Exception ex) { Append(ex.Message); }
        }

        private void BtnTrigApply_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                RequireConn();
                Send("TRIGger:A:TYPe EDGE");
                Send("TRIGger:A:EDGE:SOUrce " + ComboText(TrigSource).ToUpperInvariant());
                Send("TRIGger:A:EDGE:SLOpe " + ComboText(TrigSlope).ToUpperInvariant());
                Send("TRIGger:A:EDGE:COUPling " + ComboText(TrigCoupling).ToUpperInvariant());
            }
            catch (Exception ex) { Append(ex.Message); }
        }
        private void BtnTrigLevel_Click(object sender, RoutedEventArgs e) { try { RequireConn(); Send("TRIGger:A:LEVel " + GetDouble(TrigLevel, 0.2).ToString("G", CultureInfo.InvariantCulture)); } catch (Exception ex) { Append(ex.Message); } }
        private void BtnTrigHoldoff_Click(object sender, RoutedEventArgs e) { try { RequireConn(); Send("TRIGger:A:HOLDoff " + GetDouble(TrigHoldoff, 0).ToString("G", CultureInfo.InvariantCulture)); } catch (Exception ex) { Append(ex.Message); } }
        private void BtnTrigState_Click(object sender, RoutedEventArgs e) { try { RequireConn(); var s = _sched; if (s == null) return; s.EnqueueQuery("TRIGger:STATE?", r => Append("TRIGger:STATE? -> " + (r == null ? "" : r.Trim()))); } catch (Exception ex) { Append(ex.Message); } }

        private void BtnChApplyFromChart_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                RequireConn();
                foreach (var src in SelectedSources())
                {
                    var sc = GetDouble(ChScaleG, 1);
                    var off = GetDouble(ChOffsetG, 0);
                    var coup = ComboText(ChCouplingG).ToUpperInvariant();
                    var bw = ChBWLimitG.IsChecked == true;
                    var inv = ChInvertG.IsChecked == true;

                    Send($"SELect:{src} ON");
                    Send($"{src}:SCAle {sc.ToString("G", CultureInfo.InvariantCulture)}");
                    Send($"{src}:OFFSet {off.ToString("G", CultureInfo.InvariantCulture)}");
                    Send($"{src}:COUPling {coup}");
                    Send(bw ? $"{src}:BANdwidth TWEnty" : $"{src}:BANdwidth FULl");
                    Send($"{src}:INVert {OnOff(inv)}");
                }
            }
            catch (Exception ex) { Append(ex.Message); }
        }

        #endregion

        #region Cursors

        private void CurEnable_CheckedChanged(object sender, RoutedEventArgs e)
        {
            try
            {
                RequireConn();
                var en = CurEnable.IsChecked == true;
                Send("CURSor:STATE " + OnOff(en));
                if (!en) UpdateGateOverlay(double.NaN, double.NaN);
            }
            catch (Exception ex) { Append(ex.Message); }
        }

        private void BtnCurApply_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                RequireConn();
                var en = CurEnable.IsChecked == true;

                // Always reflect the checkbox state
                Send("CURSor:STATE " + OnOff(en));

                // Only push the function if enabled; if disabled, also clear the local overlay
                if (en)
                {
                    Send("CURSor:FUNCtion " + ComboText(CurFunc).ToUpperInvariant());
                }
                else
                {
                    UpdateGateOverlay(double.NaN, double.NaN);
                }
            }
            catch (Exception ex) { Append(ex.Message); }
        }

        private void BtnCurSetH_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                RequireConn();
                double p1 = GetDouble(CurH1, 0); double p2 = GetDouble(CurH2, 1e-3);
                Send("CURSor:HBARs:POSition1 " + p1.ToString("G", CultureInfo.InvariantCulture));
                Send("CURSor:HBARs:POSition2 " + p2.ToString("G", CultureInfo.InvariantCulture));
                UpdateGateOverlay(p1, p2);
            }
            catch (Exception ex) { Append(ex.Message); }
        }
        private void BtnCurDeltaH_Click(object sender, RoutedEventArgs e) { try { RequireConn(); var s = _sched; if (s == null) return; s.EnqueueQuery("CURSor:HBARs:DELTa?", r => Append("CURSor:HBARs:DELTa? -> " + (r == null ? "" : r.Trim()))); } catch (Exception ex) { Append(ex.Message); } }

        private void BtnCurSetV_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                RequireConn();
                double p1 = GetDouble(CurV1, 0); double p2 = GetDouble(CurV2, 1);
                Send("CURSor:VBARs:POSition1 " + p1.ToString("G", CultureInfo.InvariantCulture));
                Send("CURSor:VBARs:POSition2 " + p2.ToString("G", CultureInfo.InvariantCulture));
            }
            catch (Exception ex) { Append(ex.Message); }
        }
        private void BtnCurDeltaV_Click(object sender, RoutedEventArgs e) { try { RequireConn(); var s = _sched; if (s == null) return; s.EnqueueQuery("CURSor:VBARs:DELTa?", r => Append("CURSor:VBARs:DELTa? -> " + (r == null ? "" : r.Trim()))); } catch (Exception ex) { Append(ex.Message); } }

        #endregion

        #region Display

        private void BtnDispApply_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                RequireConn();
                Send("DISPlay:PERSistence:STATE " + OnOff(DispPersistOn.IsChecked == true));
                var sec = Math.Max(0, GetInt(DispPersistSec, 3));
                Send("DISPlay:PERSistence:VALUe " + sec);
            }
            catch (Exception ex) { Append(ex.Message); }
        }

        #endregion

        #region Chart toolbar

        private void BtnFitAll_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var z1 = TimeChart?.Behaviors?.OfType<ChartZoomPanBehavior>().FirstOrDefault();
                z1?.Reset();
            }
            catch (Exception ex) { Append(ex.Message); }
        }

        private void BtnClearCharts_Click(object sender, RoutedEventArgs e)
        {
            TimeChart.Series.Clear();
            _timeSeries.Clear();
            _timeSeriesHandle.Clear();
        }

        private void BtnApplyAxis_Click(object sender, RoutedEventArgs e)
        {
            var xAxis = AxisTimeX;
            var yAxis = AxisTimeY;

            if (XAuto.IsChecked == true) { xAxis.Minimum = double.NaN; xAxis.Maximum = double.NaN; }
            else
            {
                xAxis.Minimum = GetDouble(XMin, double.NaN);
                xAxis.Maximum = GetDouble(XMax, double.NaN);
            }

            if (YAuto.IsChecked == true) { yAxis.Minimum = double.NaN; yAxis.Maximum = double.NaN; }
            else
            {
                yAxis.Minimum = GetDouble(YMin, double.NaN);
                yAxis.Maximum = GetDouble(YMax, double.NaN);
            }
        }

        private void BtnAutoAxisFromData_Click(object sender, RoutedEventArgs e)
        {
            var xAxis = AxisTimeX;
            var yAxis = AxisTimeY;

            double xmin = _lastMinX, xmax = _lastMaxX, ymin = _lastMinY, ymax = _lastMaxY;
            if (double.IsInfinity(ymin) || double.IsInfinity(ymax)) return;

            double padY = Math.Max(1e-12, (ymax - ymin) * 0.05);
            double padX = Math.Max(1e-12, (xmax - xmin) * 0.03);

            if (XAuto.IsChecked == true) { xAxis.Minimum = xmin - padX; xAxis.Maximum = xmax + padX; }
            if (YAuto.IsChecked == true) { yAxis.Minimum = ymin - padY; yAxis.Maximum = ymax + padY; }
        }

        private void BtnPauseLive_Click(object sender, RoutedEventArgs e)
        {
            _livePaused = !_livePaused;
            BtnPauseLive.Content = _livePaused ? "Resume Live" : "Pause Live";
        }

        #endregion

        #region Continuous live plotting

        private IEnumerable<string> SelectedSources()
        {
            if (SrcCh1.IsChecked == true) yield return "CH1";
            if (SrcCh2.IsChecked == true) yield return "CH2";
        }

        private void EnsureLiveRunning()
        {
            try
            {
                RequireConn();
                if (!_connOk)
                {
                    Append("LIVE START ERR: Instrument not verified yet.");
                    return;
                }

                if (_liveTimer != null) return; // already running

                int hz = Math.Max(1, GetInt(LiveHz, 5));
                int ms = (int)Math.Max(60, 1000.0 / hz);

                _liveTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(ms) };
                _liveTimer.Tick += async (_, __) =>
                {
                    if (_liveBusy || _sched == null || _livePaused || !_connOk) return;

                    try
                    {
                        _liveBusy = true;

                        // adjust interval if user changed the Hz on the fly (lightweight)
                        var wantHz = Math.Max(1, GetInt(LiveHz, 5));
                        var wantMs = (int)Math.Max(60, 1000.0 / wantHz);
                        if (Math.Abs(_liveTimer.Interval.TotalMilliseconds - wantMs) > 1)
                            _liveTimer.Interval = TimeSpan.FromMilliseconds(wantMs);

                        var any = false;
                        foreach (var src in SelectedSources())
                        {
                            await AcquireAndPlotAsync(src, replaceExisting: true, forLive: true);
                            any = true;
                        }

                        if (!any)
                            BtnClearCharts_Click(null, null);
                    }
                    catch (Exception ex)
                    {
                        Append("LIVE ERR: " + ex.Message);
                    }
                    finally
                    {
                        _liveBusy = false;
                    }
                };
                _liveTimer.Start();
                Append("Live plotting started.");
            }
            catch (Exception ex)
            {
                Append("LIVE START ERR: " + ex.Message);
            }
        }

        private void StopLiveInternal()
        {
            try
            {
                if (_liveTimer != null)
                {
                    _liveTimer.Stop();
                    _liveTimer = null;
                    Append("Live plotting stopped.");
                }
            }
            catch { }
            _liveBusy = false;
            _livePaused = false;
            if (BtnPauseLive != null) BtnPauseLive.Content = "Pause Live";
        }

        #endregion

        #region Measurements – immediate + discover + slots

        private void BtnMeasApply_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                RequireConn();
                Send("MEASUrement:IMMed:TYPe " + ComboText(MeasType).ToUpperInvariant());
                var s1 = ComboText(MeasSrc1).ToUpperInvariant();
                var s2 = ComboText(MeasSrc2).ToUpperInvariant();
                if (!string.IsNullOrWhiteSpace(s1)) Send("MEASUrement:IMMed:SOUrce1 " + s1);
                if (!string.IsNullOrWhiteSpace(s2)) Send("MEASUrement:IMMed:SOUrce2 " + s2);
            }
            catch (Exception ex) { Append(ex.Message); }
        }

        private void BtnMeasRead_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                RequireConn();
                var s = _sched; if (s == null) return;
                s.EnqueueQuery("MEASUrement:IMMed:VALue?", r => Append("IMMed:VALue? -> " + (r == null ? "" : r.Trim())));
            }
            catch (Exception ex) { Append(ex.Message); }
        }

        private static readonly string[] ProbeMeasurementCandidates = new string[]
        {
            "FREQuency","PERIod","MEAN","MEDian","RMS","CRMs","PK2Pk","AMPlitude",
            "HIGH","LOW","MAXimum","MINImum","OVershoot","UNdershoot","RISe","FALL",
            "PDUty","NDUty","PWIdth","NWIdth","PHAse","DELay","ZEROCross"
        };

        private async void BtnMeasDiscover_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                RequireConn();
                var s = _sched; if (s == null) return;
                string src = ComboText(MeasSrc1).ToUpperInvariant();
                await s.EnqueueWriteAsync("MEASUrement:IMMed:SOUrce1 " + src);

                var supported = new List<string>();
                foreach (var t in ProbeMeasurementCandidates)
                {
                    try
                    {
                        await s.EnqueueWriteAsync("MEASUrement:IMMed:TYPe " + t, retries: 3);
                        string v = await s.EnqueueQueryAsync("MEASUrement:IMMed:VALue?", timeoutMs: 8000, retries: 3);
                        if (!string.IsNullOrWhiteSpace(v)) supported.Add(t);
                    }
                    catch { /* skip */ }
                }

                supported.Sort(StringComparer.OrdinalIgnoreCase);
                MeasTypeList.ItemsSource = supported;
                Append("Discovered " + supported.Count + " measurement type(s) for " + src + ".");
            }
            catch (Exception ex) { Append("DISCOVER ERR: " + ex.Message); }
        }

        private async void BtnMeasReadSelected_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                RequireConn();
                var s = _sched; if (s == null) return;
                string src = ComboText(MeasSrc1).ToUpperInvariant();
                await s.EnqueueWriteAsync("MEASUrement:IMMed:SOUrce1 " + src);

                var selected = MeasTypeList.SelectedItems.Cast<string>().Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
                if (selected.Count == 0) { Append("Nothing selected."); return; }

                foreach (var t in selected)
                {
                    try
                    {
                        await s.EnqueueWriteAsync("MEASUrement:IMMed:TYPe " + t, retries: 3);
                        string v = await s.EnqueueQueryAsync("MEASUrement:IMMed:VALue?", timeoutMs: 8000, retries: 3);
                        Append("[" + src + "] " + t + " = " + (v == null ? "" : v.Trim()));
                    }
                    catch (Exception ex) { Append("[" + src + "] " + t + " -> ERR: " + ex.Message); }
                }
            }
            catch (Exception ex) { Append("READ SEL ERR: " + ex.Message); }
        }

        private void BtnMeasSlotCfg_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                RequireConn();
                int slot = GetInt(MeasSlot, 1);
                var typ = (MeasSlotType?.Text ?? "FREQuency").Trim().ToUpperInvariant();
                var s1 = (MeasSlotS1?.Text ?? "CH1").Trim().ToUpperInvariant();
                var s2 = (MeasSlotS2?.Text ?? "").Trim().ToUpperInvariant();
                Send("MEASUrement:MEAS" + slot + ":TYPe " + typ);
                if (!string.IsNullOrWhiteSpace(s1)) Send("MEASUrement:MEAS" + slot + ":SOUrce1 " + s1);
                if (!string.IsNullOrWhiteSpace(s2)) Send("MEASUrement:MEAS" + slot + ":SOUrce2 " + s2);
            }
            catch (Exception ex) { Append(ex.Message); }
        }

        private void BtnMeasSlotOn_Click(object sender, RoutedEventArgs e)
        {
            try { RequireConn(); int slot = GetInt(MeasSlot, 1); Send("MEASUrement:MEAS" + slot + ":STATE ON"); }
            catch (Exception ex) { Append(ex.Message); }
        }

        private void BtnMeasSlotOff_Click(object sender, RoutedEventArgs e)
        {
            try { RequireConn(); int slot = GetInt(MeasSlot, 1); Send("MEASUrement:MEAS" + slot + ":STATE OFF"); }
            catch (Exception ex) { Append(ex.Message); }
        }

        private void BtnMeasSlotRead_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                RequireConn();
                var s = _sched; if (s == null) return;
                int slot = GetInt(MeasSlot, 1);
                s.EnqueueQuery("MEASUrement:MEAS" + slot + ":VALue?", r => Append("MEAS" + slot + ":VALue? -> " + (r == null ? "" : r.Trim())));
            }
            catch (Exception ex) { Append(ex.Message); }
        }

        private void BtnMeasGate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                RequireConn();
                Send("MEASUrement:GATing " + ((MeasGateCursor.IsChecked == true) ? "CURSor" : "OFF"));
            }
            catch (Exception ex) { Append(ex.Message); }
        }

        private void BtnMeasClearAllSlots_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                RequireConn();
                for (int i = 1; i <= 6; i++) Send("MEASUrement:MEAS" + i + ":STATE OFF");
                Append("All on-screen measurement slots cleared.");
            }
            catch (Exception ex) { Append(ex.Message); }
        }

        #endregion

        #region Acquire + Plot core

        private Task<(double[] x, double[] y)?> ReadWaveformAsync(string source)
            => ReadWaveformAsync(source, _sched);

        private async Task<(double[] x, double[] y)?> ReadWaveformAsync(string source, Tbs1000cCommandScheduler sched)
        {
            if (sched == null) return null;

            await sched.EnqueueWriteAsync($"SELect:{source} ON", retries: 3);
            await sched.EnqueueWriteAsync("DATa:SOUrce " + source, retries: 3);
            await sched.EnqueueWriteAsync("DATa:ENCDG ASCii", retries: 3);
            await sched.EnqueueWriteAsync("DATa:WIDth 1", retries: 3);
            await sched.EnqueueWriteAsync("DATa:STARt 1", retries: 3);

            var pre = await GetPreambleSafelyAsync(sched);
            if (pre == null) return null;

            int rec = pre.NR_PT > 1 ? pre.NR_PT : await GetRecordLengthSafelyAsync(sched);
            rec = Math.Max(1000, Math.Min(rec, 5000));
            await sched.EnqueueWriteAsync("DATa:STOP " + rec.ToString(CultureInfo.InvariantCulture), retries: 3);

            var cur = await sched.EnqueueQueryAsync("CURVe?", retries: 3);
            var raw = ParseCsvToDouble(cur);
            if (raw.Length == 0) return null;

            var volts = new double[raw.Length];
            for (int i = 0; i < raw.Length; i++)
                volts[i] = (raw[i] - pre.YOFF) * pre.YMULT + pre.YZERO;

            var x = new double[raw.Length];
            if (pre.XINCR > 0)
            {
                for (int i = 0; i < raw.Length; i++) x[i] = pre.XZERO + i * pre.XINCR;
            }
            else
            {
                for (int i = 0; i < raw.Length; i++) x[i] = i;
            }

            return (x, volts);
        }

        private Task<Tbs1000cWaveformPreamble> GetPreambleSafelyAsync()
            => GetPreambleSafelyAsync(_sched);

        private async Task<Tbs1000cWaveformPreamble> GetPreambleSafelyAsync(Tbs1000cCommandScheduler sched)
        {
            if (sched == null) return null;

            var p = new Tbs1000cWaveformPreamble();

            async Task<string> Q(string suffix)
            {
                try { return await sched.EnqueueQueryAsync("WFMOutpre:" + suffix, retries: 3); }
                catch { return await sched.EnqueueQueryAsync("WFMPRE:" + suffix, retries: 3); }
            }

            double D(string s) { double v; return double.TryParse((s ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : 0.0; }
            int I(string s) { int v; return int.TryParse((s ?? "").Trim(), out v) ? v : 0; }

            try { p.XINCR = D(await Q("XINCR?")); } catch { }
            try { p.XZERO = D(await Q("XZERO?")); } catch { }
            try { p.YMULT = D(await Q("YMULT?")); } catch { }
            try { p.YOFF = D(await Q("YOFF?")); } catch { }
            try { p.YZERO = D(await Q("YZERO?")); } catch { }
            try { p.NR_PT = I(await Q("NR_Pt?")); } catch { }

            if (p.YMULT == 0) p.YMULT = 1;
            return p;
        }

        private Task<int> GetRecordLengthSafelyAsync()
            => GetRecordLengthSafelyAsync(_sched);

        private async Task<int> GetRecordLengthSafelyAsync(Tbs1000cCommandScheduler sched)
        {
            if (sched == null) return 2000;
            try
            {
                var nStr = await sched.EnqueueQueryAsync("WFMOutpre:NR_Pt?", retries: 3);
                if (int.TryParse((nStr ?? "").Trim(), out var n) && n > 1) return n;
            }
            catch { }

            try
            {
                var nStr2 = await sched.EnqueueQueryAsync("HORizontal:RECOrdlength?", retries: 3);
                if (int.TryParse((nStr2 ?? "").Trim(), out var n2) && n2 > 1) return n2;
            }
            catch { }

            return 2000;
        }

        private async Task AcquireAndPlotAsync(string source, bool replaceExisting = true, bool forLive = false)
        {
            var s = _sched;
            if (s == null) return;

            await _opGate.WaitAsync();
            try
            {
                var wf = await ReadWaveformAsync(source, s);
                if (wf == null) return;

                var x = wf.Value.x;
                var y = wf.Value.y;

                _lastMinX = x.Length > 0 ? x[0] : 0; _lastMaxX = x.Length > 0 ? x[x.Length - 1] : 1;
                double minY = double.PositiveInfinity, maxY = double.NegativeInfinity;
                for (int i = 0; i < y.Length; i++) { if (y[i] < minY) minY = y[i]; if (y[i] > maxY) maxY = y[i]; }
                _lastMinY = minY; _lastMaxY = maxY;

                var list = BuildDecimatedSeries(x, y, 2500);
                UpdateSeriesData(source, list, replaceExisting);

                if (XAuto.IsChecked == true || YAuto.IsChecked == true)
                    BtnAutoAxisFromData_Click(null, null);

                if (!forLive)
                    TimeChart?.Behaviors?.OfType<ChartZoomPanBehavior>().FirstOrDefault()?.Reset();

                // Rate-limited measurement pull for mini charts + logs subset
                var now = DateTime.UtcNow;
                if ((now - _lastMeasAtUtc).TotalSeconds >= 2)
                {
                    _lastMeasAtUtc = now;
                    await UpdateMiniMeasurementsAsync(source, alsoLogSubset: true, s); // await to avoid overlap
                }
            }
            finally
            {
                _opGate.Release();
            }
        }

        private void UpdateSeriesData(string source, IList<DataPoint> newPoints, bool replaceExisting)
        {
            var chart = TimeChart;

            if (!_timeSeriesHandle.TryGetValue(source, out var series))
            {
                series = new FastLineSeries
                {
                    XBindingPath = "XValue",
                    YBindingPath = "YValue",
                    Label = source,
                    XAxis = AxisTimeX,
                    YAxis = AxisTimeY,
                    IsHitTestVisible = false
                };
                chart.Series.Add(series);
                _timeSeriesHandle[source] = series;
            }

            _timeSeries[source] = newPoints;
            series.ItemsSource = _timeSeries[source];
        }

        private static List<DataPoint> BuildDecimatedSeries(double[] x, double[] y, int maxPoints)
        {
            int n = Math.Min(x.Length, y.Length);
            var result = new List<DataPoint>(Math.Min(n, maxPoints));

            if (n <= 0) return result;
            if (n <= maxPoints)
            {
                for (int i = 0; i < n; i++) result.Add(new DataPoint(x[i], y[i]));
                return result;
            }

            int buckets = Math.Max(1, maxPoints / 2);
            int bucketSize = n / buckets;

            for (int b = 0; b < buckets; b++)
            {
                int start = b * bucketSize;
                int end = (b == buckets - 1) ? n : start + bucketSize;
                if (start >= n) break;

                double min = double.PositiveInfinity, max = double.NegativeInfinity;
                int iMin = start, iMax = start;
                for (int i = start; i < end; i++)
                {
                    var yi = y[i];
                    if (yi < min) { min = yi; iMin = i; }
                    if (yi > max) { max = yi; iMax = i; }
                }
                result.Add(new DataPoint(x[iMin], y[iMin]));
                if (iMax != iMin) result.Add(new DataPoint(x[iMax], y[iMax]));
            }

            return result;
        }

        private void UpdateGateOverlay(double t1, double t2)
        {
            var axis = AxisTimeX;
            if (axis == null) return;

            try
            {
                axis.StripLines?.Clear();
                if (double.IsNaN(t1) || double.IsNaN(t2)) return;

                double a = Math.Min(t1, t2);
                double w = Math.Abs(t2 - t1);

                var strip = new ChartStripLine
                {
                    Start = a,
                    Width = w,
                    IsSegmented = false,
                    Background = System.Windows.Media.Brushes.LightGoldenrodYellow,
                    Opacity = 0.3
                };
                axis.StripLines.Add(strip);
            }
            catch { }
        }

        private Task UpdateMiniMeasurementsAsync(string src, bool alsoLogSubset)
            => UpdateMiniMeasurementsAsync(src, alsoLogSubset, _sched);

        private async Task UpdateMiniMeasurementsAsync(string src, bool alsoLogSubset, Tbs1000cCommandScheduler sched)
        {
            if (sched == null) return;

            try
            {
                await sched.EnqueueWriteAsync("MEASUrement:IMMed:SOUrce1 " + src, retries: 3);

                string[] typesLog = { "FREQuency", "PERIod", "RMS", "MEAN", "PK2Pk", "MINImum", "MAXimum", "RISe", "FALL", "PDUty" };

                foreach (var t in MiniTypes)
                {
                    try
                    {
                        await sched.EnqueueWriteAsync("MEASUrement:IMMed:TYPe " + t, retries: 3);
                        var raw = await sched.EnqueueQueryAsync("MEASUrement:IMMed:VALue?", timeoutMs: 8000, retries: 3);
                        if (!TryParseDouble(raw, out var val)) continue;
                        if (double.IsNaN(val) || double.IsInfinity(val) || Math.Abs(val) > 1e36) continue;

                        AddMiniPoint(src, t, val);

                        if (alsoLogSubset && typesLog.Contains(t))
                            Append("[" + src + "] " + t + " = " + (raw == null ? "" : raw.Trim()));
                    }
                    catch (Exception ex)
                    {
                        if (alsoLogSubset && typesLog.Contains(t))
                            Append("[" + src + "] " + t + " -> ERR: " + ex.Message);
                    }
                }
            }
            catch (Exception ex) { Append("MEAS MINI ERR: " + ex.Message); }
        }

        #endregion

        #region Mini chart UI + data

        private void TryEnsureMiniCharts()
        {
            if (MiniWrapCh1 == null || MiniWrapCh2 == null) return;
            if (MiniWrapCh1.Children.Count > 0 && MiniWrapCh2.Children.Count > 0) return; // already built

            BuildMiniSection("CH1", MiniWrapCh1);
            BuildMiniSection("CH2", MiniWrapCh2);
        }

        private void BuildMiniSection(string channel, Panel host)
        {
            host.Children.Clear();

            foreach (var t in MiniTypes)
            {
                var tile = BuildMiniTile(channel, t);
                host.Children.Add(tile);
            }
        }

        private FrameworkElement BuildMiniTile(string channel, string type)
        {
            var key = Key(channel, type);

            var border = new Border
            {
                Margin = new Thickness(6),
                Padding = new Thickness(8),
                Background = System.Windows.Media.Brushes.White,
                BorderBrush = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(220, 225, 232)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Width = 220,
                Height = 120
            };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var header = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var name = new TextBlock
            {
                Text = type,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            var val = new TextBlock
            {
                Text = "-",
                Margin = new Thickness(8, 0, 0, 0),
                Foreground = System.Windows.Media.Brushes.DimGray,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            DockPanel.SetDock(val, Dock.Right);
            header.Children.Add(val);
            header.Children.Add(name);
            Grid.SetRow(header, 0);
            grid.Children.Add(header);

            var chart = new SfChart
            {
                Background = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0),
                Legend = null
            };

            // Hide axes (compatible across SfChart versions)
            var xAxis = new NumericalAxis
            {
                ShowGridLines = false,
                Visibility = Visibility.Collapsed
            };
            var yAxis = new NumericalAxis
            {
                ShowGridLines = false,
                Visibility = Visibility.Collapsed
            };
            chart.PrimaryAxis = xAxis;
            chart.SecondaryAxis = yAxis;

            var series = new FastLineSeries
            {
                XBindingPath = "XValue",
                YBindingPath = "YValue",
                IsHitTestVisible = false
            };

            var src = new ObservableCollection<DataPoint>();
            series.ItemsSource = src;
            chart.Series.Add(series);

            Grid.SetRow(chart, 1);
            grid.Children.Add(chart);
            border.Child = grid;

            // register references
            _measSeries[key] = src;
            _measSeriesHandle[key] = series;
            _measValueLabels[key] = val;

            return border;
        }

        private void AddMiniPoint(string channel, string type, double value)
        {
            var key = Key(channel, type);
            if (!_measSeries.TryGetValue(key, out var coll)) return; // UI not built yet

            var t = (DateTime.UtcNow - _measStartUtc).TotalSeconds;
            coll.Add(new DataPoint(t, value));
            if (coll.Count > _measMaxPoints)
            {
                while (coll.Count > _measMaxPoints) coll.RemoveAt(0);
            }

            if (_measValueLabels.TryGetValue(key, out var lbl))
                lbl.Text = FormatMeasValue(type, value);
        }

        private static string FormatMeasValue(string type, double v)
        {
            string unit = "";
            switch (type.ToUpperInvariant())
            {
                case "FREQUENCY": unit = "Hz"; break;
                case "PERIOD":
                case "PWIDTH":
                case "NWIDTH":
                case "RISE":
                case "FALL": unit = "s"; break;
                case "PDUTY":
                case "NDUTY":
                case "OVERSHOOT":
                case "UNDERSHOOT": unit = "%"; break;
                default: unit = "V"; break;
            }

            string val = v.ToString("G6", CultureInfo.InvariantCulture);
            return $"{val} {unit}";
        }

        #endregion
    }
}
