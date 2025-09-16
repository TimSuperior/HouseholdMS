using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using HouseholdMS.Services;
using Syncfusion.Licensing;
using Syncfusion.UI.Xaml.Charts;


namespace HouseholdMS.View.Measurement
{
    public partial class OscilloscopeView : UserControl
    {
        static OscilloscopeView()
        {
            SyncfusionLicenseProvider.RegisterLicense("Mzk3NTExMEAzMzMwMmUzMDJlMzAzYjMzMzAzYlZIMmI2R1J4SGJTT0ExYWF0VTR2L3RMaDJEVUJyNkk2elh1YXpNSWFrSzA9;Mzk3NTExMUAzMzMwMmUzMDJlMzAzYjMzMzAzYmZIUkhmT1JKVzRZNDVKeUtra1BnanozdU5NTUtzeGM2MUNrY2Y0T3laN3c9;Mgo+DSMBPh8sVXN0S0d+X1ZPd11dXmJWd1p/THNYflR1fV9DaUwxOX1dQl9mSXlQd0djW31bdHVWQGRXUkQ=;NRAiBiAaIQQuGjN/VkZ+XU9HcVRDX3xKf0x/TGpQb19xflBPallYVBYiSV9jS3tTcUZiW39ccnFRR2ZbV091Xw==;Mgo+DSMBMAY9C3t3VVhhQlJDfV5AQmBIYVp/TGpJfl96cVxMZVVBJAtUQF1hTH5UdURhWX1cdXBUTmNfWkd2;Mzk3NTExNUAzMzMwMmUzMDJlMzAzYjMzMzAzYkhIbUxNNFR5alVJbys5YkVKdHJHVmYwL1p6ZnZrZ1hkaEQ1alZZQlVWVGs9;Mzk3NTExNkAzMzMwMmUzMDJlMzAzYjMzMzAzYmNwQ2s0ZWc5RzJab2l0ZFArM2R2VGIyWWorek1WenBOaHlPdjN2dnpmOGs9");
        }

        private sealed class ChartPoint { public double X { get; set; } public double Y { get; set; } }

        private ScpiDeviceVisa _visa;
        private readonly object _ioLock = new object();
        private Timer _poll;
        private volatile bool _connected;
        private volatile bool _pollBusy;

        // limit for live plotting (CSV still uses full precision of what we hold)
        private int _recordLenHint = 2500;

        public OscilloscopeView()
        {
            InitializeComponent();
            if (SeriesCH1 != null) SeriesCH1.EnableAntiAliasing = true;
            if (SeriesCH2 != null) SeriesCH2.EnableAntiAliasing = true;
            if (SeriesMATH != null) SeriesMATH.EnableAntiAliasing = true;
        }

        // ===== Connection =====
        private async void Connect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Status("Connecting...");
                _visa?.Dispose();
                _visa = new ScpiDeviceVisa();
                _visa.Open(VisaAddressBox.Text.Trim(), 15000);
                _visa.SetTimeout(15000);
                _connected = true;

                await SendAsync("*CLS");
                await SendAsync("DATa:ENCdg ASCii");
                await SendAsync("ACQuire:STATE STOP");
                await SendAsync("HORizontal:MAIn:SCAle 1e-3");
                await QueryRecordLength();
                Status("Connected.");
            }
            catch (Exception ex) { _connected = false; Status("Error: " + ex.Message); }
        }

        private void Disconnect_Click(object sender, RoutedEventArgs e)
        {
            try { StopPolling(); _visa?.Dispose(); _connected = false; Status("Disconnected."); }
            catch (Exception ex) { Status("Error: " + ex.Message); }
        }

        private async void ReadIDN_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureConn()) return;
            try { var idn = await QueryAsync("*IDN?", 5000); Status(idn == null ? "No response" : idn.Trim()); }
            catch (Exception ex) { Status("IDN failed: " + ex.Message); }
        }

        // ===== Acquisition =====
        private async void Acq_RunRequested(object s, RoutedEventArgs e) { if (!EnsureConn()) return; await SendAsync("ACQuire:STATE RUN"); StartPolling(); }
        private async void Acq_StopRequested(object s, RoutedEventArgs e) { if (!EnsureConn()) return; await SendAsync("ACQuire:STATE STOP"); StopPolling(); }
        private async void Acq_SingleRequested(object s, RoutedEventArgs e) { if (!EnsureConn()) return; await SendAsync("ACQuire:STOPAfter SEQ"); await SendAsync("ACQuire:STATE RUN"); StartPolling(); }
        private async void Acq_AutosetRequested(object s, RoutedEventArgs e)
        {
            if (!EnsureConn()) return;
            try { await SendAsync("AUTOSet EXECute"); await QueryAsync("*OPC?", 30000); await QueryRecordLength(); Status("Autoset done."); }
            catch (Exception ex) { Status("Autoset error: " + ex.Message); }
        }
        private async void Acq_SetTimebaseRequested(object s, RoutedEventArgs e) { if (!EnsureConn()) return; await SendAsync("HORizontal:MAIn:SCAle " + AcqTab.SelectedTimebase); await QueryAsync("*OPC?", 8000); }
        private async void Acq_ReadRecordLengthRequested(object s, RoutedEventArgs e) { if (!EnsureConn()) return; await QueryRecordLength(); }

        // ===== Channels =====
        private async void Ch_ApplyCh1Requested(object s, RoutedEventArgs e) { if (!EnsureConn()) return; await ApplyChannel(1, ChTab.Coupling, ChTab.Scale); }
        private async void Ch_ApplyCh2Requested(object s, RoutedEventArgs e) { if (!EnsureConn()) return; await ApplyChannel(2, ChTab.Coupling, ChTab.Scale); }
        private async void Ch_SetProbeRequested(object s, RoutedEventArgs e)
        {
            if (!EnsureConn()) return;
            string p = ChTab.Probe;
            if (ChTab.PlotCh1) await SendAsync("CH1:PROBe " + p);
            if (ChTab.PlotCh2) await SendAsync("CH2:PROBe " + p);
        }
        private async Task ApplyChannel(int ch, string coupling, string scale)
        { await SendAsync("SELect:CH" + ch + " ON"); await SendAsync("CH" + ch + ":COUPling " + coupling); await SendAsync("CH" + ch + ":SCAle " + scale); }

        // ===== Measurements tab polling rate change =====
        private void Meas_PollIntervalChanged(object s, EventArgs e) { if (_poll != null) StartPolling(); }

        // ===== Export =====
        private async void Exp_SaveCsvRequested(object s, RoutedEventArgs e)
        {
            if (!EnsureConn()) return;
            try
            {
                string src = "CH1"; Dispatcher.Invoke(() => { src = MeasTab.MeasureSource; });

                (double[] T, double[] Y)? wf = null;
                if (src == "CH1") wf = await ReadWaveform(1);
                else if (src == "CH2") wf = await ReadWaveform(2);
                else
                {
                    var ch1 = await ReadWaveform(1);
                    var ch2 = await ReadWaveform(2);
                    if (ch1.HasValue && ch2.HasValue)
                    {
                        int n = Math.Min(ch1.Value.Y.Length, ch2.Value.Y.Length);
                        var T = new double[n]; var Y = new double[n];
                        for (int i = 0; i < n; i++) { T[i] = ch1.Value.T[i]; Y[i] = ch1.Value.Y[i] - ch2.Value.Y[i]; }
                        wf = (T, Y);
                    }
                }
                if (!wf.HasValue) { Status("No data"); return; }

                string f = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                                        "scope_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + src + ".csv");
                using (var sw = new StreamWriter(f))
                {
                    sw.WriteLine("time_s,volts");
                    for (int i = 0; i < wf.Value.T.Length; i++)
                        sw.WriteLine(wf.Value.T[i].ToString("G17", CultureInfo.InvariantCulture) + "," +
                                     wf.Value.Y[i].ToString("G17", CultureInfo.InvariantCulture));
                }
                ExpTab.ShowResult("Saved: " + f);
                Status("Saved CSV.");
            }
            catch (Exception ex) { Status("Save CSV error: " + ex.Message); }
        }

        private void Exp_SavePngRequested(object s, RoutedEventArgs e)
        {
            try
            {
                string f = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                                        "scope_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png");

                var element = ScopeChart as FrameworkElement;
                int w = (int)Math.Max(2.0, element.ActualWidth), h = (int)Math.Max(2.0, element.ActualHeight);
                if (w < 2) w = 1024; if (h < 2) h = 512;

                var rtb = new RenderTargetBitmap(w, h, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                element.Measure(new Size(w, h)); element.Arrange(new Rect(new Size(w, h)));
                rtb.Render(element);

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(rtb));
                using (var fs = File.Create(f)) encoder.Save(fs);

                ExpTab.ShowResult("Saved: " + f);
                Status("Saved PNG.");
            }
            catch (Exception ex) { Status("Save PNG error: " + ex.Message); }
        }

        // ===== Polling =====
        private void StartPolling()
        {
            StopPolling();
            int interval = 500; Dispatcher.Invoke(() => { interval = MeasTab.PollIntervalMs; });
            if (interval < 100) interval = 100;

            _pollBusy = false;
            _poll = new Timer(interval) { AutoReset = true };
            _poll.Elapsed += async (s, e) => await PollOnceSafe();
            _poll.Start();
        }
        private void StopPolling() { if (_poll != null) { _poll.Stop(); _poll.Dispose(); _poll = null; } _pollBusy = false; }
        private async Task PollOnceSafe() { if (_pollBusy) return; _pollBusy = true; try { await PollOnce(); } finally { _pollBusy = false; } }

        private async Task PollOnce()
        {
            if (!_connected) return;

            bool needCh1 = false, needCh2 = false, needMath = false; string measureSource = "CH1";
            Dispatcher.Invoke(() => { needCh1 = ChTab.PlotCh1; needCh2 = ChTab.PlotCh2; needMath = ChTab.PlotMath; measureSource = MeasTab.MeasureSource; });

            try
            {
                var tasks = new List<Task<(double[] T, double[] Y)?>>();
                if (needCh1) tasks.Add(ReadWaveform(1));
                if (needCh2) tasks.Add(ReadWaveform(2));
                var results = await Task.WhenAll(tasks);

                double[] t1 = null, y1 = null, t2 = null, y2 = null;
                int idx = 0;
                if (needCh1 && results.Length > idx && results[idx].HasValue) { t1 = results[idx].Value.T; y1 = results[idx].Value.Y; idx++; }
                if (needCh2 && results.Length > idx && results[idx].HasValue) { t2 = results[idx].Value.T; y2 = results[idx].Value.Y; }

                double[] tm = null, ym = null;
                if (needMath && t1 != null && y1 != null && t2 != null && y2 != null)
                { int n = Math.Min(y1.Length, y2.Length); tm = new double[n]; ym = new double[n]; for (int i = 0; i < n; i++) { tm[i] = t1[i]; ym[i] = y1[i] - y2[i]; } }

                // Pick source for measurements
                double[] tForMeasure = (measureSource == "CH1") ? t1 : (measureSource == "CH2") ? t2 : tm;
                double[] yForMeasure = (measureSource == "CH1") ? y1 : (measureSource == "CH2") ? y2 : ym;
                if (measureSource == "MATH" && (tm == null || ym == null)) { tForMeasure = (t1 != null) ? t1 : t2; yForMeasure = (ym != null) ? ym : (y1 != null) ? y1 : y2; }

                MeasurementValues mv = ComputeMeasurements(tForMeasure, yForMeasure);

                await Dispatcher.InvokeAsync(() =>
                {
                    SetSeries(SeriesCH1, t1, y1, needCh1);
                    SetSeries(SeriesCH2, t2, y2, needCh2);
                    SetSeries(SeriesMATH, tm, ym, needMath);
                    AutoScaleAxes(
                        new[] { t1, t2, tm },
                        new[] { y1, y2, ym },
                        new[] { needCh1, needCh2, needMath });
                    MeasTab.UpdateValues(mv);
                });
            }
            catch (Exception ex) { await Dispatcher.InvokeAsync(() => Status("Poll error: " + ex.Message)); }
        }

        // ===== Waveform I/O =====
        private async Task<(double[] T, double[] Y)?> ReadWaveform(int channel)
        {
            if (!_connected) return null;

            await SendAsync("DATa:SOUrce CH" + channel);
            int n = _recordLenHint;
            await SendAsync("DATa:STARt 1");
            await SendAsync("DATa:STOP " + n);

            double xincr = await QueryDoubleAny(new[] { "WFMOutpre:XINCR?", "WFMPRE:XINCR?" });
            double xzero = await QueryDoubleAny(new[] { "WFMOutpre:XZERO?", "WFMPRE:XZERO?" });
            double ymult = await QueryDoubleAny(new[] { "WFMOutpre:YMULT?", "WFMPRE:YMULT?" });
            double yzero = await QueryDoubleAny(new[] { "WFMOutpre:YZERO?", "WFMPRE:YZERO?" });
            double yoff = await QueryDoubleAny(new[] { "WFMOutpre:YOFF?", "WFMPRE:YOFF?" });

            string payload = await QueryAsync("CURVe?", 12000);
            if (string.IsNullOrWhiteSpace(payload)) return null;

            // tokenize, filter, decimate to ~3000 points and guarantee monotonic X
            string[] parts = payload.Replace("\n", "").Replace("\r", "")
                                    .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);

            int target = Math.Max(400, Math.Min(_recordLenHint, 3000));
            int step = Math.Max(1, parts.Length / target);
            int len = (parts.Length + step - 1) / step;

            var y = new double[len]; var t = new double[len];
            int j = 0;
            for (int i = 0; i < parts.Length; i += step)
            {
                double code;
                if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out code))
                    code = double.NaN;

                double yy = (code - yoff) * ymult + yzero;
                double tt = xzero + i * xincr;

                if (double.IsNaN(yy) || double.IsInfinity(yy)) continue;

                t[j] = tt; y[j] = yy; j++;
                if (j >= len) break;
            }

            if (j == 0) return null;
            if (j < len) { Array.Resize(ref t, j); Array.Resize(ref y, j); }

            // ensure strictly increasing X (guard against any preamble oddities)
            for (int k = 1; k < t.Length; k++)
                if (t[k] <= t[k - 1]) t[k] = t[k - 1] + Math.Abs(xincr);

            return (t, y);
        }

        private void SetSeries(FastLineBitmapSeries series, double[] t, double[] y, bool visible)
        {
            if (series == null) return;
            if (!visible || t == null || y == null || t.Length == 0 || y.Length == 0)
            { series.ItemsSource = null; series.Visibility = visible ? Visibility.Visible : Visibility.Collapsed; return; }

            var list = new List<ChartPoint>(t.Length);
            for (int i = 0; i < t.Length; i++)
                list.Add(new ChartPoint { X = t[i], Y = y[i] });

            series.ItemsSource = list;
            series.Visibility = Visibility.Visible;
        }

        private void AutoScaleAxes(double[][] tx, double[][] ty, bool[] visible)
        {
            // Older Syncfusion builds have read-only ranges and no axis zoom properties.
            // Force a relayout; axes will auto-calc based on the current ItemsSource.
            try
            {
                if (ScopeChart != null)
                {
                    ScopeChart.UpdateLayout();        // triggers range recomputation
                    ScopeChart.InvalidateVisual();    // ensure redraw
                }
            }
            catch { /* ignore */ }
        }

        // ===== Measurements =====
        private MeasurementValues ComputeMeasurements(double[] t, double[] y)
        {
            var m = new MeasurementValues();
            if (t == null || y == null || y.Length < 2) return m;

            int n = y.Length;
            double vmax = y.Max(), vmin = y.Min(), vpp = vmax - vmin, vavg = y.Average();
            double vrms = Math.Sqrt(y.Select(v => v * v).Average());

            m.Vmax = vmax; m.Vmin = vmin; m.Vpp = vpp; m.Vavg = vavg; m.Vrms = vrms;
            m.Vpos = vmax; m.Vneg = vmin;

            // simple logic threshold for edges
            double thr = (vmax + vmin) / 2.0; var rising = new List<int>(); var falling = new List<int>();
            bool prevHigh = y[0] > thr;
            for (int i = 1; i < n; i++)
            {
                bool high = y[i] > thr;
                if (!prevHigh && high) rising.Add(i);
                if (prevHigh && !high) falling.Add(i);
                prevHigh = high;
            }

            // Period/Frequency and duty using last complete cycle
            if (rising.Count >= 2)
            {
                int i2 = rising[rising.Count - 1], i1 = rising[rising.Count - 2];
                double period = t[i2] - t[i1];
                if (period > 1e-12) { m.Period = period; m.Freq = 1.0 / period; }

                int total = Math.Max(1, i2 - i1), above = 0;
                for (int i = i1; i < i2; i++) if (y[i] > thr) above++;
                double duty = 100.0 * above / total; m.Duty = duty;
                double dt = (t[i2] - t[i1]) / total; m.PwPos = above * dt; m.PwNeg = (total - above) * dt;
            }

            // Rise/Fall 10-90% based on last detected edges (FIXED: correct index list)
            if (rising.Count > 0) m.Rise = EdgeTime(t, y, rising[rising.Count - 1], vmin + 0.1 * vpp, vmin + 0.9 * vpp, true);
            if (falling.Count > 0) m.Fall = EdgeTime(t, y, falling[falling.Count - 1], vmin + 0.9 * vpp, vmin + 0.1 * vpp, false);

            if (Math.Abs(vavg) > 1e-12) m.RipplePct = vpp / Math.Abs(vavg) * 100.0;
            if (vrms > 1e-12) m.Crest = Math.Max(Math.Abs(vmax), Math.Abs(vmin)) / vrms;

            if (Math.Abs(vavg) > 1e-12) { m.OvershootPct = (vmax - vavg) / Math.Abs(vavg) * 100.0; m.UndershootPct = (vavg - vmin) / Math.Abs(vavg) * 100.0; }

            // zero-crossings per second around the average
            int zc = 0; for (int i = 1; i < n; i++) { bool s1 = y[i - 1] >= vavg, s2 = y[i] >= vavg; if (s1 != s2) zc++; }
            double duration = t[n - 1] - t[0]; if (duration > 1e-12) m.ZeroCrossRate = zc / duration;

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

        // ===== VISA helpers =====
        private bool EnsureConn() { if (!_connected || _visa == null) { Status("Not connected"); return false; } return true; }

        private Task SendAsync(string cmd) => Task.Run(() =>
        {
            lock (_ioLock)
            {
                try { _visa.Write(cmd); }
                catch (System.Runtime.InteropServices.COMException ex) { Status("I/O write error: " + ex.Message); throw; }
            }
        });

        private Task<string> QueryAsync(string cmd, int timeoutOverrideMs) => Task.Run(() =>
        {
            lock (_ioLock)
            {
                int old = _visa.TimeoutMs; if (timeoutOverrideMs > 0) _visa.TimeoutMs = timeoutOverrideMs;
                try { return _visa.Query(cmd); }
                catch (System.Runtime.InteropServices.COMException ex) { Status("I/O read error: " + ex.Message); throw; }
                finally { if (timeoutOverrideMs > 0) _visa.TimeoutMs = old; }
            }
        });

        private Task<string> QueryAsync(string cmd) => QueryAsync(cmd, 0);

        private async Task<double> QueryDoubleAny(string[] cmds)
        {
            for (int k = 0; k < cmds.Length; k++)
            {
                try
                {
                    string s = await QueryAsync(cmds[k], 4000);
                    if (!string.IsNullOrWhiteSpace(s))
                    {
                        double v;
                        if (double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return v;
                    }
                }
                catch { /* try next */ }
            }
            return 0.0;
        }

        private async Task QueryRecordLength()
        {
            try
            {
                string s = await QueryAsync("HORizontal:RECOrdlength?", 4000);
                int rl;
                if (int.TryParse((s ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out rl) && rl > 10)
                    _recordLenHint = Math.Min(5000, rl);
                Status("RecordLength=" + _recordLenHint);
            }
            catch { /* keep default */ }
        }

        private void Status(string s) => Dispatcher.Invoke(() => StatusBlock.Text = s ?? "");
    }
}
