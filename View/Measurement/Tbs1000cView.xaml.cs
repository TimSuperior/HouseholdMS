// tbs1000c_oscilloscope_view.xaml.cs
using HouseholdMS.Services;
using Syncfusion.Licensing;
using Syncfusion.UI.Xaml.Charts;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HouseholdMS.View.Measurement
{
    public partial class Tbs1000cView : UserControl
    {
        static Tbs1000cView()
        {
            SyncfusionLicenseProvider.RegisterLicense("Mzk3NTExMEAzMzMwMmUzMDJlMzAzYjMzMzAzYlZIMmI2R1J4SGJTT0ExYWF0VTR2L3RMaDJEVUJyNkk2elh1YXpNSWFrSzA9;Mzk3NTExMUAzMzMwMmUzMDJlMzAzYjMzMzAzYmZIUkhmT1JKVzRZNDVKeUtra1BnanozdU5NTUtzeGM2MUNrY2Y0T3laN3c9;Mgo+DSMBPh8sVXN0S0d+X1ZPd11dXmJWd1p/THNYflR1fV9DaUwxOX1dQl9mSXlQd0djW31bdHVWQGRXUkQ=;NRAiBiAaIQQuGjN/VkZ+XU9HcVRDX3xKf0x/TGpQb19xflBPallYVBYiSV9jS3tTcUZiW39ccnFRR2ZbV091Xw==;Mgo+DSMBMAY9C3t3VVhhQlJDfV5AQmBIYVp/TGpJfl96cVxMZVVBJAtUQF1hTH5UdURhWX1cdXBUTmNfWkd2;Mzk3NTExNUAzMzMwMmUzMDJlMzAzYjMzMzAzYkhIbUxNNFR5alVJbys5YkVKdHJHVmYwL1p6ZnZrZ1hkaEQ1alZZQlVWVGs9;Mzk3NTExNkAzMzMwMmUzMDJlMzAzYjMzMzAzYmNwQ2s0ZWc5RzJab2l0ZFArM2R2VGIyWWorek1WenBOaHlPdjN2dnpmOGs9");
        }

        private sealed class ChartPoint { public double X { get; set; } public double Y { get; set; } }

        // Your existing VISA
        private ScpiDeviceVisa _visa;
        // Adapter + scheduler
        private IScpiTransport _io;
        private Tbs1000cCommandScheduler _sched;
        private readonly Tbs1000cStateCache _cache = new Tbs1000cStateCache();

        private Timer _poll;
        private volatile bool _connected;
        private volatile bool _pollBusy;

        private int _recordLenHint = 20000;
        private readonly Tbs1000cWaveformRingBuffer _ring = new Tbs1000cWaveformRingBuffer(64);

        // Cursors/gating/markers
        private bool _gateEnabled;
        private double? _gateT1, _gateT2;
        private readonly List<double> _markers = new List<double>();

        public Tbs1000cView()
        {
            InitializeComponent();
            SeriesCH1.EnableAntiAliasing = true;
            SeriesCH2.EnableAntiAliasing = true;
            SeriesMATH.EnableAntiAliasing = true;
            CmbTimebase.SelectedIndex = 9; // 1e-3
        }

        // ===== Connection =====
        private async void Connect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Status("Connecting...");
                _sched?.Dispose();
                _visa?.Dispose();
                _cache.Clear();

                _visa = new ScpiDeviceVisa();
                _visa.Open(VisaAddressBox.Text.Trim(), 15000);
                _visa.SetTimeout(15000);
                _io = new ScpiTransportAdapter(_visa);
                _sched = new Tbs1000cCommandScheduler(_io);

                await _sched.EnqueueWriteAsync("WAVeform:FORMat BYTE");
                await _sched.EnqueueWriteAsync("ACQuire:STATE STOP");
                await _sched.EnqueueWriteAsync("HORizontal:MAIn:SCAle 1e-3", waitOpc: true);
                await QueryRecordLength();

                _connected = true;
                Status("Connected.");
            }
            catch (Exception ex) { _connected = false; Status("Error: " + ex.Message); }
        }

        private void Disconnect_Click(object sender, RoutedEventArgs e)
        {
            try { StopPolling(); _sched?.Dispose(); _visa?.Dispose(); _connected = false; Status("Disconnected."); }
            catch (Exception ex) { Status("Error: " + ex.Message); }
        }

        private async void ReadIDN_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureConn()) return;
            try { var idn = await _sched.EnqueueQueryAsync("*IDN?", timeoutMs: 5000); Status(string.IsNullOrWhiteSpace(idn) ? "No response" : idn.Trim()); }
            catch (Exception ex) { Status("IDN failed: " + ex.Message); }
        }

        private bool EnsureConn() { if (!_connected || _visa == null) { Status("Not connected"); return false; } return true; }

        // ===== Acquisition =====
        private async void Acq_Run_Click(object s, RoutedEventArgs e) { if (!EnsureConn()) return; await _sched.EnqueueWriteAsync("ACQuire:STATE RUN"); StartPolling(); }
        private async void Acq_Stop_Click(object s, RoutedEventArgs e) { if (!EnsureConn()) return; await _sched.EnqueueWriteAsync("ACQuire:STATE STOP"); StopPolling(); }
        private async void Acq_Single_Click(object s, RoutedEventArgs e) { if (!EnsureConn()) return; await _sched.EnqueueWriteAsync("ACQuire:STOPAfter SEQ;ACQuire:STATE RUN", waitOpc: true); StartPolling(); }

        private async void Acq_Autoset_Click(object s, RoutedEventArgs e)
        {
            if (!EnsureConn()) return;
            try { await _sched.EnqueueWriteAsync("AUTOSet EXECute", waitOpc: true); await QueryRecordLength(); Status("Autoset done."); }
            catch (Exception ex) { Status("Autoset error: " + ex.Message); }
        }

        private async void Acq_SetTimebase_Click(object s, RoutedEventArgs e)
        {
            if (!EnsureConn()) return;
            var it = CmbTimebase.SelectedItem as ComboBoxItem; var val = (it != null && it.Content != null) ? it.Content.ToString() : "1e-3";
            await _sched.EnqueueWriteAsync("HORizontal:MAIn:SCAle " + val, waitOpc: true);
        }
        private async void Acq_RecordLength_Click(object s, RoutedEventArgs e) { if (!EnsureConn()) return; await QueryRecordLength(); }

        private async Task QueryRecordLength()
        {
            try
            {
                string s = await _sched.EnqueueQueryAsync("HORizontal:RECOrdlength?", timeoutMs: 4000);
                if (int.TryParse((s ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var rl) && rl > 10)
                    _recordLenHint = Math.Min(20000, rl);
                Status("RecordLength=" + _recordLenHint);
            }
            catch { }
        }

        // ===== Trigger =====
        private async void Acq_ApplyTrigger_Click(object s, RoutedEventArgs e)
        {
            if (!EnsureConn()) return;
            string type = GetComboText(CmbTrigType);
            string src = GetComboText(CmbTrigSrc);
            string slope = GetComboText(CmbTrigSlope);
            double level = ParseD(TxtTrigLevel.Text, 0);

            try
            {
                switch (type)
                {
                    case "EDGE":
                        await _sched.EnqueueWriteAsync($"TRIGger:MAIn:EDGE:SOURce {src}");
                        await _sched.EnqueueWriteAsync($"TRIGger:MAIn:EDGE:SLOPe {slope}");
                        await _sched.EnqueueWriteAsync($"TRIGger:MAIn:LEVel {level.ToString("G17", CultureInfo.InvariantCulture)}", waitOpc: true);
                        break;
                    case "PULSE":
                        double pw = ParseD(TxtPulseWidth.Text, 1e-3);
                        await _sched.EnqueueWriteAsync($"TRIGger:MAIn:PULSe:SOURce {src}");
                        await _sched.EnqueueWriteAsync($"TRIGger:MAIn:PULSe:POLarity {(slope == "FALL" ? "NEG" : "POS")}");
                        await _sched.EnqueueWriteAsync($"TRIGger:MAIn:PULSe:WIDTh:WIDTh {pw.ToString("G17", CultureInfo.InvariantCulture)}", waitOpc: true);
                        break;
                    case "RUNT":
                        double rl = ParseD(TxtRuntLow.Text, 0.2);
                        double rh = ParseD(TxtRuntHigh.Text, 1.0);
                        await _sched.EnqueueWriteAsync($"TRIGger:MAIn:RUNT:SOURce {src}");
                        await _sched.EnqueueWriteAsync($"TRIGger:MAIn:RUNT:POLarity {(slope == "FALL" ? "NEG" : "POS")}");
                        await _sched.EnqueueWriteAsync($"TRIGger:MAIn:RUNT:THReshold:LOWer {rl.ToString("G17", CultureInfo.InvariantCulture)}");
                        await _sched.EnqueueWriteAsync($"TRIGger:MAIn:RUNT:THReshold:UPPer {rh.ToString("G17", CultureInfo.InvariantCulture)}", waitOpc: true);
                        break;
                }
                Status("Trigger applied.");
            }
            catch (Exception ex) { Status("Trigger error: " + ex.Message); }
        }

        // ===== Channels =====
        private async void Ch_ApplyCh1_Click(object s, RoutedEventArgs e) { if (!EnsureConn()) return; await ApplyChannel(1); }
        private async void Ch_ApplyCh2_Click(object s, RoutedEventArgs e) { if (!EnsureConn()) return; await ApplyChannel(2); }
        private async void Ch_SetProbe_Click(object s, RoutedEventArgs e)
        {
            if (!EnsureConn()) return;
            string p = GetComboText(CmbProbe);
            if (ChkCh1.IsChecked == true && _cache.ShouldSend("CH1:PROBe", p)) await _sched.EnqueueWriteAsync("CH1:PROBe " + p);
            if (ChkCh2.IsChecked == true && _cache.ShouldSend("CH2:PROBe", p)) await _sched.EnqueueWriteAsync("CH2:PROBe " + p);
        }
        private async Task ApplyChannel(int ch)
        {
            string coupling = GetComboText(CmbCoupling);
            string scale = GetComboText(CmbVScale);
            if (_cache.ShouldSend($"SELect:CH{ch}", "ON")) await _sched.EnqueueWriteAsync($"SELect:CH{ch} ON");
            if (_cache.ShouldSend($"CH{ch}:COUPling", coupling)) await _sched.EnqueueWriteAsync($"CH{ch}:COUPling {coupling}");
            if (_cache.ShouldSend($"CH{ch}:SCAle", scale)) await _sched.EnqueueWriteAsync($"CH{ch}:SCAle {scale}", waitOpc: true);
        }

        // ===== Polling/backpressure =====
        private void StartPolling()
        {
            StopPolling();
            int interval = (int)SldPoll.Value; if (interval < 100) interval = 100;
            _pollBusy = false;
            _poll = new Timer(interval) { AutoReset = true };
            _poll.Elapsed += async (s, e) => await PollOnceSafe();
            _poll.Start();
        }
        private void StopPolling() { if (_poll != null) { _poll.Stop(); _poll.Dispose(); _poll = null; } _pollBusy = false; }
        private async Task PollOnceSafe() { if (_pollBusy) return; _pollBusy = true; try { await PollOnce(); } finally { _pollBusy = false; } }

        private sealed class SimpleWave { public double[] T; public double[] Y; }

        private async Task PollOnce()
        {
            if (!_connected) return;

            bool needCh1 = ChkCh1.IsChecked == true;
            bool needCh2 = ChkCh2.IsChecked == true;
            bool needMath = ChkMath.IsChecked == true;
            string measureSource = GetComboText(CmbSource);

            try
            {
                SimpleWave ch1 = null, ch2 = null;
                if (needCh1) ch1 = await ReadWaveformBinary(1);
                if (needCh2) ch2 = await ReadWaveformBinary(2);

                double[] t1 = ch1?.T, y1 = ch1?.Y, t2 = ch2?.T, y2 = ch2?.Y;
                double[] tm = null, ym = null;
                if (needMath && t1 != null && y1 != null && t2 != null && y2 != null)
                {
                    int n = Math.Min(y1.Length, y2.Length);
                    tm = new double[n]; ym = new double[n];
                    for (int i = 0; i < n; i++) { tm[i] = t1[i]; ym[i] = y1[i] - y2[i]; }
                }

                double? g1 = _gateEnabled ? _gateT1 : null;
                double? g2 = _gateEnabled ? _gateT2 : null;

                double[] tForMeasure = (measureSource == "CH1") ? t1 : (measureSource == "CH2") ? t2 : tm;
                double[] yForMeasure = (measureSource == "CH1") ? y1 : (measureSource == "CH2") ? y2 : ym;
                if (measureSource == "MATH" && (tm == null || ym == null)) { tForMeasure = (t1 != null) ? t1 : t2; yForMeasure = (ym != null) ? ym : (y1 != null) ? y1 : y2; }

                var mv = ComputeMeasurements(tForMeasure, yForMeasure, g1, g2);

                if (tForMeasure != null && yForMeasure != null)
                    _ring.Add(new Tbs1000cWaveformFrame { TimestampUtc = DateTime.UtcNow, Source = measureSource, Time = tForMeasure, Volts = yForMeasure, Measurements = mv });

                var stats = ComputeRollingStats(measureSource, window: 32);

                Dispatcher.Invoke(() =>
                {
                    SetSeries(SeriesCH1, t1, y1, needCh1);
                    SetSeries(SeriesCH2, t2, y2, needCh2);
                    SetSeries(SeriesMATH, tm, ym, needMath);
                    AutoScaleAxes();
                    UpdateMeasurementBlock(mv);
                    RollingStatsBlock.Text = $"N={stats.Count}, mean(Vpp)={stats.MeanVpp:G5}, σ={stats.SigmaVpp:G5}, min={stats.MinVpp:G5}, max={stats.MaxVpp:G5}, outliers={stats.Outliers}";
                });
            }
            catch (Exception ex) { Dispatcher.Invoke(() => Status("Poll error: " + ex.Message)); }
        }

        // ===== Binary waveform I/O =====
        private async Task<SimpleWave> ReadWaveformBinary(int channel)
        {
            if (!_connected) return null;

            await _sched.EnqueueWriteAsync($"WAVeform:SOURce CHAN{channel}");
            string pre = await _sched.EnqueueQueryAsync("WAVeform:PREamble?", timeoutMs: 5000);
            var wp = Tbs1000cWaveformPreamble.Parse(pre);
            if (wp == null || wp.XINCR == 0)
            {
                // fallback to legacy names
                double xincr = await QueryDoubleAny(new[] { "WFMOutpre:XINCR?", "WFMPRE:XINCR?" });
                double xzero = await QueryDoubleAny(new[] { "WFMOutpre:XZERO?", "WFMPRE:XZERO?" });
                double ymult = await QueryDoubleAny(new[] { "WFMOutpre:YMULT?", "WFMPRE:YMULT?" });
                double yzero = await QueryDoubleAny(new[] { "WFMOutpre:YZERO?", "WFMPRE:YZERO?" });
                double yoff = await QueryDoubleAny(new[] { "WFMOutpre:YOFF?", "WFMPRE:YOFF?" });
                wp = new Tbs1000cWaveformPreamble { XINCR = xincr, XZERO = xzero, YMULT = ymult, YZERO = yzero, YOFF = yoff, NR_PT = _recordLenHint };
            }

            int n = Math.Max(100, Math.Min(_recordLenHint, wp.NR_PT > 0 ? wp.NR_PT : _recordLenHint));
            await _sched.EnqueueWriteAsync("WAVeform:STARt 1");
            await _sched.EnqueueWriteAsync("WAVeform:STOP " + n);

            byte[] raw = _io.QueryBinary("WAVeform:DATA?", timeoutOverrideMs: 12000);
            if (raw == null || raw.Length == 0) return null;

            var y = new double[raw.Length];
            for (int i = 0; i < raw.Length; i++) y[i] = ((raw[i] - wp.YOFF) * wp.YMULT) + wp.YZERO;
            var t = new double[raw.Length];
            for (int i = 0; i < raw.Length; i++) t[i] = wp.XZERO + i * wp.XINCR;

            return new SimpleWave { T = t, Y = y };
        }

        private async Task<double> QueryDoubleAny(string[] cmds)
        {
            for (int k = 0; k < cmds.Length; k++)
            {
                try
                {
                    string s = await _sched.EnqueueQueryAsync(cmds[k], timeoutMs: 4000);
                    if (!string.IsNullOrWhiteSpace(s))
                        if (double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return v;
                }
                catch { }
            }
            return 0.0;
        }

        // ===== Plot helpers =====
        private void SetSeries(FastLineBitmapSeries series, double[] t, double[] y, bool visible)
        {
            if (series == null) return;
            if (!visible || t == null || y == null || t.Length == 0 || y.Length == 0)
            { series.ItemsSource = null; series.Visibility = visible ? Visibility.Visible : Visibility.Collapsed; return; }

            var list = DecimateToPoints(t, y, 2500);
            series.ItemsSource = list;
            series.Visibility = Visibility.Visible;
        }

        private List<ChartPoint> DecimateToPoints(double[] t, double[] y, int target)
        {
            int n = t.Length;
            if (n <= target) { var all = new List<ChartPoint>(n); for (int i = 0; i < n; i++) all.Add(new ChartPoint { X = t[i], Y = y[i] }); return all; }
            int step = Math.Max(1, n / target);
            var list = new List<ChartPoint>(n / step + 1);
            for (int i = 0; i < n; i += step) list.Add(new ChartPoint { X = t[i], Y = y[i] });
            return list;
        }

        private void AutoScaleAxes() { try { if (ScopeChart != null) { ScopeChart.UpdateLayout(); ScopeChart.InvalidateVisual(); } } catch { } }

        // ===== Measurements + gating =====
        private Tbs1000cMeasurementValues ComputeMeasurements(double[] t, double[] y, double? gateT1 = null, double? gateT2 = null)
        {
            var m = new Tbs1000cMeasurementValues();
            if (t == null || y == null || y.Length < 2) return m;

            int start = 0, end = y.Length - 1;
            if (gateT1.HasValue && gateT2.HasValue)
            {
                double gmin = Math.Min(gateT1.Value, gateT2.Value);
                double gmax = Math.Max(gateT1.Value, gateT2.Value);
                start = Array.FindIndex(t, v => v >= gmin); if (start < 0) start = 0;
                end = Array.FindLastIndex(t, v => v <= gmax); if (end < 0) end = y.Length - 1;
                if (end <= start) { start = 0; end = y.Length - 1; }
            }

            int n = end - start + 1; if (n < 2) return m;

            double vmax = double.MinValue, vmin = double.MaxValue, sum = 0.0, sumsq = 0.0;
            for (int i = start; i <= end; i++) { var v = y[i]; if (v > vmax) vmax = v; if (v < vmin) vmin = v; sum += v; sumsq += v * v; }
            double vpp = vmax - vmin, vavg = sum / n, vrms = Math.Sqrt(sumsq / n);

            m.Vmax = vmax; m.Vmin = vmin; m.Vpp = vpp; m.Vavg = vavg; m.Vrms = vrms; m.Vpos = vmax; m.Vneg = vmin;

            double thr = (vmax + vmin) / 2.0; var rising = new List<int>(); var falling = new List<int>();
            bool prevHigh = y[start] > thr;
            for (int i = start + 1; i <= end; i++)
            {
                bool high = y[i] > thr;
                if (!prevHigh && high) rising.Add(i);
                if (prevHigh && !high) falling.Add(i);
                prevHigh = high;
            }

            if (rising.Count >= 2)
            {
                int i2 = rising[rising.Count - 1], i1 = rising[rising.Count - 2];
                double period = t[i2] - t[i1]; if (period > 1e-12) { m.Period = period; m.Freq = 1.0 / period; }
                int total = Math.Max(1, i2 - i1), above = 0; for (int i = i1; i < i2; i++) if (y[i] > thr) above++;
                double duty = 100.0 * above / total; m.Duty = duty;
                double dt = (t[i2] - t[i1]) / total; m.PwPos = above * dt; m.PwNeg = (total - above) * dt;
            }

            if (rising.Count > 0) m.Rise = EdgeTime(t, y, rising[rising.Count - 1], vmin + 0.1 * vpp, vmin + 0.9 * vpp, true);
            if (falling.Count > 0) m.Fall = EdgeTime(t, y, falling[falling.Count - 1], vmin + 0.9 * vpp, vmin + 0.1 * vpp, false);

            if (Math.Abs(vavg) > 1e-12) m.RipplePct = vpp / Math.Abs(vavg) * 100.0;
            if (vrms > 1e-12) m.Crest = Math.Max(Math.Abs(vmax), Math.Abs(vmin)) / vrms;
            if (Math.Abs(vavg) > 1e-12) { m.OvershootPct = (vmax - vavg) / Math.Abs(vavg) * 100.0; m.UndershootPct = (vavg - vmin) / Math.Abs(vavg) * 100.0; }

            int zc = 0; for (int i = start + 1; i <= end; i++) { bool s1 = y[i - 1] >= vavg, s2 = y[i] >= vavg; if (s1 != s2) zc++; }
            double duration = t[end] - t[start]; if (duration > 1e-12) m.ZeroCrossRate = zc / duration;

            return m;
        }

        private double? EdgeTime(double[] t, double[] y, int idxAround, double vLow, double vHigh, bool risingEdge)
        {
            try
            {
                int i0 = Math.Max(1, idxAround - 1000), i1 = Math.Min(y.Length - 2, idxAround + 1000);
                int lowIdx = -1, highIdx = -1;

                if (risingEdge)
                {
                    for (int i = i0; i < idxAround; i++) if (y[i] < vLow && y[i + 1] >= vLow) { lowIdx = i; break; }
                    for (int i = idxAround; i < i1; i++) if (y[i] < vHigh && y[i + 1] >= vHigh) { highIdx = i; break; }
                }
                else
                {
                    for (int i = i0; i < idxAround; i++) if (y[i] > vLow && y[i + 1] <= vLow) { lowIdx = i; break; }
                    for (int i = idxAround; i < i1; i++) if (y[i] > vHigh && y[i + 1] <= vHigh) { highIdx = i; break; }
                }

                if (lowIdx < 0 || highIdx < 0) return null;
                double tLow = InterpTime(t, y, lowIdx, vLow), tHigh = InterpTime(t, y, highIdx, vHigh);
                return tHigh - tLow;
            }
            catch { return null; }
        }

        private static double InterpTime(double[] t, double[] y, int i, double yTarget)
        {
            double y1 = y[i], y2 = y[i + 1], x1 = t[i], x2 = t[i + 1];
            double denom = y2 - y1; double frac = (Math.Abs(denom) < 1e-18) ? 0.0 : (yTarget - y1) / denom;
            if (frac < 0.0) frac = 0.0; else if (frac > 1.0) frac = 1.0;
            return x1 + (x2 - x1) * frac;
        }

        private (int Count, double MeanVpp, double SigmaVpp, double MinVpp, double MaxVpp, int Outliers) ComputeRollingStats(string source, int window)
        {
            var frames = _ring.Snapshot().Where(f => f.Source == source && f.Measurements?.Vpp != null).ToArray();
            if (frames.Length == 0) return (0, 0, 0, 0, 0, 0);

            var arr = GetLast(frames.Select(f => f.Measurements.Vpp.Value), window).ToArray();
            if (arr.Length == 0) return (0, 0, 0, 0, 0, 0);

            double mean = arr.Average();
            double sigma = Math.Sqrt(arr.Select(v => (v - mean) * (v - mean)).Average());
            double min = arr.Min(), max = arr.Max();
            int outliers = arr.Count(v => Math.Abs(v - mean) > 3 * sigma);
            return (arr.Length, mean, sigma, min, max, outliers);
        }

        // ===== Cursors & markers (unchanged) =====
        private void BtnEnableGate_Checked(object s, RoutedEventArgs e) { _gateEnabled = true; UpdateCursorVisibility(); }
        private void BtnEnableGate_Unchecked(object s, RoutedEventArgs e) { _gateEnabled = false; UpdateCursorVisibility(); }
        private void SetCursorA_Click(object s, RoutedEventArgs e) { if (TryGetChartMidX(out var tx)) { _gateT1 = tx; CursorA.X1 = tx; CursorA.Visibility = Visibility.Visible; } }
        private void SetCursorB_Click(object s, RoutedEventArgs e) { if (TryGetChartMidX(out var tx)) { _gateT2 = tx; CursorB.X1 = tx; CursorB.Visibility = Visibility.Visible; } }
        private void UpdateCursorVisibility() { CursorA.Visibility = (_gateEnabled && _gateT1.HasValue) ? Visibility.Visible : Visibility.Collapsed; CursorB.Visibility = (_gateEnabled && _gateT2.HasValue) ? Visibility.Visible : Visibility.Collapsed; }
        private void AddMarker_Click(object s, RoutedEventArgs e) { if (TryGetChartMidX(out var tx)) { _markers.Add(tx); MarkersList.Items.Add($"t={tx:G6}s"); } }
        private void ClearMarkers_Click(object s, RoutedEventArgs e) { _markers.Clear(); MarkersList.Items.Clear(); }

        private void FindRising_Click(object s, RoutedEventArgs e) => FindEdge(rising: true);
        private void FindFalling_Click(object s, RoutedEventArgs e) => FindEdge(rising: false);
        private void FindRunt_Click(object s, RoutedEventArgs e)
        {
            var low = ParseD(TxtFindRuntLow.Text, 0.2);
            var high = ParseD(TxtFindRuntHigh.Text, 1.0);
            FindRunt(low, high);
        }

        private void FindEdge(bool rising)
        {
            var src = GetComboText(CmbSource);
            var frame = _ring.Snapshot().Reverse().FirstOrDefault(f => f.Source == src);
            if (frame == null) return;

            double thrDefault = (frame.Volts.Max() + frame.Volts.Min()) / 2.0;
            double thr = ParseD(TxtEdgeThresh.Text, thrDefault);
            for (int i = 1; i < frame.Volts.Length; i++)
            {
                bool prev = frame.Volts[i - 1] > thr;
                bool curr = frame.Volts[i] > thr;
                if (rising && (!prev && curr)) { JumpToTime(frame.Time[i]); return; }
                if (!rising && (prev && !curr)) { JumpToTime(frame.Time[i]); return; }
            }
        }

        private void FindRunt(double low, double high)
        {
            var src = GetComboText(CmbSource);
            var frame = _ring.Snapshot().Reverse().FirstOrDefault(f => f.Source == src);
            if (frame == null) return;

            bool aboveLowPrev = frame.Volts[0] > low;
            for (int i = 1; i < frame.Volts.Length; i++)
            {
                bool aboveLow = frame.Volts[i] > low;
                bool aboveHigh = frame.Volts[i] > high;

                if (!aboveLowPrev && aboveLow && !aboveHigh)
                { JumpToTime(frame.Time[i]); return; }
                aboveLowPrev = aboveLow;
            }
        }

        private void JumpToTime(double t)
        {
            _markers.Add(t);
            MarkersList.Items.Add($"t={t:G6}s (found)");
            CursorA.X1 = t;
            CursorB.X1 = t;
            CursorA.Visibility = _gateEnabled ? Visibility.Visible : Visibility.Collapsed;
            CursorB.Visibility = _gateEnabled ? Visibility.Visible : Visibility.Collapsed;
        }

        // ===== Export =====
        private async void Exp_SaveCsv_Click(object s, RoutedEventArgs e)
        {
            if (!EnsureConn()) return;
            try
            {
                string src = GetComboText(CmbSource);
                var wf = await ReadWaveformComposite(src);
                if (wf == null) { Status("No data"); return; }

                string f = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                                        "scope_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + src + ".csv");
                using (var sw = new StreamWriter(f))
                {
                    sw.WriteLine("time_s,volts");
                    for (int i = 0; i < wf.T.Length; i++)
                        sw.WriteLine(wf.T[i].ToString("G17", CultureInfo.InvariantCulture) + "," +
                                     wf.Y[i].ToString("G17", CultureInfo.InvariantCulture));
                }
                ExportResult.Text = "Saved: " + f;
                Status("Saved CSV.");
            }
            catch (Exception ex) { Status("Save CSV error: " + ex.Message); }
        }

        private void Exp_SavePng_Click(object s, RoutedEventArgs e)
        {
            try
            {
                string f = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                                        "scope_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png");

                var element = ScopeChart as FrameworkElement;
                int w = (int)Math.Max(2.0, element.ActualWidth), h = (int)Math.Max(2.0, element.ActualHeight);
                if (w < 2) w = 1024; if (h < 2) h = 512;

                var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
                element.Measure(new Size(w, h)); element.Arrange(new Rect(new Size(w, h)));
                rtb.Render(element);

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(rtb));
                using (var fs = File.Create(f)) encoder.Save(fs);

                ExportResult.Text = "Saved: " + f;
                Status("Saved PNG.");
            }
            catch (Exception ex) { Status("Save PNG error: " + ex.Message); }
        }

        private async Task<SimpleWave> ReadWaveformComposite(string src)
        {
            if (src == "CH1") return await ReadWaveformBinary(1);
            if (src == "CH2") return await ReadWaveformBinary(2);
            var ch1 = await ReadWaveformBinary(1);
            var ch2 = await ReadWaveformBinary(2);
            if (ch1 == null || ch2 == null) return null;
            int n = Math.Min(ch1.Y.Length, ch2.Y.Length);
            var T = new double[n]; var Y = new double[n];
            for (int i = 0; i < n; i++) { T[i] = ch1.T[i]; Y[i] = ch1.Y[i] - ch2.Y[i]; }
            return new SimpleWave { T = T, Y = Y };
        }

        // ===== UI helpers =====
        private void SldPoll_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { if (_poll != null) StartPolling(); }
        private void Status(string s) => StatusBlock.Text = s ?? "";
        private static string GetComboText(ComboBox cb) { var it = cb.SelectedItem as ComboBoxItem; return (it != null && it.Content != null) ? it.Content.ToString() : ""; }
        private static double ParseD(string s, double defv) { return double.TryParse((s ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : defv; }

        private bool TryGetChartMidX(out double t)
        {
            string src = GetComboText(CmbSource);
            var frame = _ring.Snapshot().Reverse().FirstOrDefault(f => f.Source == src);
            if (frame == null || frame.Time == null || frame.Time.Length == 0) { t = 0; return false; }
            int mid = frame.Time.Length / 2; t = frame.Time[mid]; return true;
        }

        private void UpdateMeasurementBlock(Tbs1000cMeasurementValues m)
        {
            string F(double? v) => v.HasValue ? v.Value.ToString("G5", CultureInfo.InvariantCulture) : "-";
            ValVpp.Text = (M_Vpp.IsChecked == true) ? F(m.Vpp) : "-";
            ValVavg.Text = (M_Vavg.IsChecked == true) ? F(m.Vavg) : "-";
            ValVrms.Text = (M_Vrms.IsChecked == true) ? F(m.Vrms) : "-";
            ValVmax.Text = (M_Vmax.IsChecked == true) ? F(m.Vmax) : "-";
            ValVmin.Text = (M_Vmin.IsChecked == true) ? F(m.Vmin) : "-";
            ValVpos.Text = (M_Vpos.IsChecked == true) ? F(m.Vpos) : "-";
            ValVneg.Text = (M_Vneg.IsChecked == true) ? F(m.Vneg) : "-";
            ValFreq.Text = (M_Freq.IsChecked == true) ? F(m.Freq) : "-";
            ValPer.Text = (M_Per.IsChecked == true) ? F(m.Period) : "-";
            ValDuty.Text = (M_Duty.IsChecked == true) ? F(m.Duty) : "-";
            ValRise.Text = (M_Rise.IsChecked == true) ? F(m.Rise) : "-";
            ValFall.Text = (M_Fall.IsChecked == true) ? F(m.Fall) : "-";
            ValPwPos.Text = (M_PwPos.IsChecked == true) ? F(m.PwPos) : "-";
            ValPwNeg.Text = (M_PwNeg.IsChecked == true) ? F(m.PwNeg) : "-";
            ValRipple.Text = (M_Ripple.IsChecked == true) ? F(m.RipplePct) : "-";
            ValCrest.Text = (M_Crest.IsChecked == true) ? F(m.Crest) : "-";
            ValOver.Text = (M_Overshoot.IsChecked == true) ? F(m.OvershootPct) : "-";
            ValUnder.Text = (M_Undershoot.IsChecked == true) ? F(m.UndershootPct) : "-";
            ValZeroCross.Text = (M_ZeroCross.IsChecked == true) ? F(m.ZeroCrossRate) : "-";
        }

        private static IEnumerable<T> GetLast<T>(IEnumerable<T> src, int n)
        {
            var q = new Queue<T>(n);
            foreach (var x in src) { if (q.Count == n) q.Dequeue(); q.Enqueue(x); }
            return q.ToArray();
        }
    }
}
