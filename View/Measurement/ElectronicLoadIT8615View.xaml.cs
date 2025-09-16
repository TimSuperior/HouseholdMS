using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using HouseholdMS.Services;
using Ivi.Visa.Interop;                // for local scanning (no change to ScpiDeviceVisa)
using Syncfusion.Licensing;

namespace HouseholdMS.View.Measurement
{
    public partial class ElectronicLoadIT8615View : UserControl
    {
        static ElectronicLoadIT8615View()
        {
            // Syncfusion license
            SyncfusionLicenseProvider.RegisterLicense(
                "Mzk3NTExMEAzMzMwMmUzMDJlMzAzYjMzMzAzYlZIMmI2R1J4SGJTT0ExYWF0VTR2L3RMaDJEVUJyNkk2elh1YXpNSWFrSzA9;Mzk3NTExMUAzMzMwMmUzMDJlMzAzYjMzMzAzYmZIUkhmT1JKVzRZNDVKeUtra1BnanozdU5NTUtzeGM2MUNrY2Y0T3laN3c9;Mgo+DSMBPh8sVXN0S0d+X1ZPd11dXmJWd1p/THNYflR1fV9DaUwxOX1dQl9mSXlQd0djW31bdHVWQGRXUkQ=;NRAiBiAaIQQuGjN/VkZ+XU9HcVRDX3xKf0x/TGpQb19xflBPallYVBYiSV9jS3tTcUZiW39ccnFRR2ZbV091Xw==;Mgo+DSMBMAY9C3t3VVhhQlJDfV5AQmBIYVp/TGpJfl96cVxMZVVBJAtUQF1hTH5UdURhWX1cdXBUTmNfWkd2;Mzk3NTExNUAzMzMwMmUzMDJlMzAzYjMzMzAzYkhIbUxNNFR5alVJbys5YkVKdHJHVmYwL1p6ZnZrZ1hkaEQ1alZZQlVWVGs9;Mzk3NTExNkAzMzMwMmUzMDJlMzAzYjMzMzAzYmNwQ2s0ZWc5RzJab2l0ZFArM2R2VGIyWWorek1WenBOaHlPdjN2dnpmOGs9");
        }

        private sealed class XY { public double X { get; set; } public double Y { get; set; } }

        private ScpiDeviceVisa _visa;
        private readonly object _ioLock = new object();
        private volatile bool _connected;
        private Timer _poll;

        public ElectronicLoadIT8615View()
        {
            InitializeComponent();
            if (SeriesV != null) SeriesV.EnableAntiAliasing = true;
            if (SeriesI != null) SeriesI.EnableAntiAliasing = true;
        }

        // ===== helpers for .NET Fx 4.8 / C# 7.3 =====
        private static bool IsFinite(double v) { return !(double.IsNaN(v) || double.IsInfinity(v)); }

        // ==== VISA discovery (embedded; no dependency on extra files) ====
        private static List<string> VisaFind(string expression)
        {
            var list = new List<string>();
            ResourceManager rm = null;
            try
            {
                rm = new ResourceManager();
                object res = rm.FindRsrc(expression);
                var arr = res as Array;
                if (arr != null)
                {
                    foreach (var o in arr)
                    {
                        var s = Convert.ToString(o);
                        if (!string.IsNullOrWhiteSpace(s)) list.Add(s.Trim());
                    }
                }
                else
                {
                    var s = res as string;
                    if (!string.IsNullOrWhiteSpace(s)) list.Add(s.Trim());
                }
            }
            catch { }
            finally { try { (rm as IDisposable)?.Dispose(); } catch { } }
            return list;
        }

        private static readonly Regex IncompleteUsb =
            new Regex(@"^USB\d+::0x[0-9A-Fa-f]+::0x[0-9A-Fa-f]+::INSTR$", RegexOptions.IgnoreCase);

        private async Task<string> ResolveConcreteAddressAsync(string addr)
        {
            if (string.IsNullOrWhiteSpace(addr)) return null;
            if (!IncompleteUsb.IsMatch(addr)) return addr;

            // Expand generic vendor/product to concrete resource (with serial)
            string[] parts = addr.Split(new[] { "::" }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3)
            {
                string pattern = "USB?*::" + parts[1] + "::" + parts[2] + "::*::INSTR";
                var matches = VisaFind(pattern);
                foreach (var r in matches)
                {
                    var idn = await ProbeIdn(r, 2500);
                    if (!string.IsNullOrWhiteSpace(idn)) return r;
                }
            }

            // Fallback: any ITECH
            var itech = VisaFind("USB?*::0x2EC7::*::INSTR");
            foreach (var r in itech)
            {
                var idn = await ProbeIdn(r, 2500);
                if (!string.IsNullOrWhiteSpace(idn)) return r;
            }
            return addr; // last resort; may still be incomplete
        }

        private async Task<string> ProbeIdn(string resource, int timeoutMs)
        {
            ScpiDeviceVisa tmp = null;
            try
            {
                tmp = new ScpiDeviceVisa();
                tmp.Open(resource, timeoutMs);
                tmp.SetTimeout(timeoutMs);
                try { tmp.Write("*CLS"); } catch { }
                string s = tmp.Query("*IDN?");
                if (!string.IsNullOrWhiteSpace(s)) return s.Trim();
            }
            catch { }
            finally { if (tmp != null) tmp.Dispose(); }
            return null;
        }

        // ===== Top bar (Scan) =====
        private async void Scan_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Status("Scanning USB VISA…");
                var candidates = VisaFind("USB?*::0x2EC7::*::INSTR");
                if (candidates.Count == 0) candidates = VisaFind("USB?*::INSTR");

                var good = new List<Tuple<string, string>>();
                foreach (var r in candidates)
                {
                    var idn = await ProbeIdn(r, 2500);
                    if (!string.IsNullOrWhiteSpace(idn))
                        good.Add(Tuple.Create(r, idn));
                }

                if (good.Count == 0) { Status("No instruments responded to *IDN?."); return; }

                // pick ITECH/IT8615 first
                good.Sort((a, b) =>
                {
                    string sa = (a.Item2 ?? "").ToUpperInvariant();
                    string sb = (b.Item2 ?? "").ToUpperInvariant();
                    int scoreA = (sa.Contains("ITECH") ? 2 : 0) + (sa.Contains("IT8615") ? 2 : 0);
                    int scoreB = (sb.Contains("ITECH") ? 2 : 0) + (sb.Contains("IT8615") ? 2 : 0);
                    return scoreB.CompareTo(scoreA);
                });

                VisaResultsBox.ItemsSource = good;
                VisaResultsBox.DisplayMemberPath = "Item1";
                VisaResultsBox.Visibility = good.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
                VisaResultsBox.SelectedIndex = 0;

                VisaAddressBox.Text = good[0].Item1;
                Status("Selected: " + good[0].Item1 + "  [" + good[0].Item2.Trim() + "]");
            }
            catch (Exception ex) { Status("Scan error: " + ex.Message); }
        }

        private void VisaResultsBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var t = VisaResultsBox.SelectedItem as Tuple<string, string>;
            if (t != null) VisaAddressBox.Text = t.Item1;
        }

        // ===== Connection =====
        private async void Connect_Click(object s, RoutedEventArgs e)
        {
            try
            {
                string addr = await ResolveConcreteAddressAsync(VisaAddressBox.Text.Trim());
                if (string.IsNullOrWhiteSpace(addr))
                {
                    Status("No VISA address. Click Scan.");
                    return;
                }
                if (IncompleteUsb.IsMatch(addr))
                {
                    Status("Address missing serial. Click Scan and select the exact device.");
                    return;
                }

                Status("Connecting to " + addr + " …");

                _connected = false; // guard against stray polls
                StopPolling();
                if (_visa != null) _visa.Dispose();

                _visa = new ScpiDeviceVisa();
                _visa.Open(addr, 15000);
                _visa.SetTimeout(15000);

                // sanity: only mark connected if the session is truly open
                _connected = _visa != null && _visa.IsOpen;

                if (!_connected)
                {
                    Status("Open failed (session not open).");
                    return;
                }

                await SendAsync("*CLS");
                await SendAsync("SYST:REM");
                await SendAsync("WAVE:TRIGger:MODE AUTO");
                await SendAsync("WAVE:TRIGger:SOURce VOLTage");
                await SendAsync("WAVE:TRIGger:DIVTime 0.005");

                var idn = await TryIdnSequence();
                Status(string.IsNullOrWhiteSpace(idn) ? "Connected (no *IDN? reply)" : idn.Trim());

                StartPolling();
            }
            catch (Exception ex)
            {
                _connected = false;
                Status("Error: " + ex.Message);
            }
        }

        private void Disconnect_Click(object s, RoutedEventArgs e)
        {
            try { StopPolling(); if (_visa != null) _visa.Dispose(); _connected = false; Status("Disconnected."); }
            catch (Exception ex) { Status("Error: " + ex.Message); }
        }

        private async void ReadIDN_Click(object s, RoutedEventArgs e)
        {
            if (!EnsureConn()) return;
            try
            {
                var idn = await TryIdnSequence();
                Status(string.IsNullOrWhiteSpace(idn) ? "No response to *IDN?" : idn.Trim());
            }
            catch (COMException ex)
            {
                if ((uint)ex.HResult == 0x80040011)
                    Status("Likely VISA timeout / wrong resource. Click Scan and choose the entry with serial.");
                else
                    Status("IDN failed: " + ex.Message);
            }
            catch (Exception ex) { Status("IDN failed: " + ex.Message); }
        }

        private async Task<string> TryIdnSequence()
        {
            try { var s1 = await QueryAsync("*IDN?", 8000); if (!string.IsNullOrWhiteSpace(s1)) return s1; } catch { }
            try { var s2 = await QueryAsync("*IDN?\n", 8000); if (!string.IsNullOrWhiteSpace(s2)) return s2; } catch { }
            try { await SendAsync("*CLS"); await SendAsync("SYST:REM"); var s3 = await QueryAsync("*IDN?\n", 10000); if (!string.IsNullOrWhiteSpace(s3)) return s3; } catch { }
            return null;
        }

        private async void InputEnableBox_Changed(object s, RoutedEventArgs e)
        {
            if (!EnsureConn()) return;
            try
            {
                await SendAsync("INPut:STATe " + (InputEnableBox.IsChecked == true ? "ON" : "OFF"));
                Status(InputEnableBox.IsChecked == true ? "INPUT=ON" : "INPUT=OFF");
            }
            catch (Exception ex) { Status("INPUT error: " + ex.Message); }
        }

        // ===== Mode & setpoints =====
        private async void ModeBox_SelectionChanged(object s, SelectionChangedEventArgs e)
        {
            if (!EnsureConn()) return;
            try
            {
                string mode = ((ComboBoxItem)ModeBox.SelectedItem).Content.ToString();
                string fn;
                switch (mode)
                {
                    case "CC": fn = "CURRent"; break;
                    case "CV": fn = "VOLTage"; break;
                    case "CR": fn = "RESistance"; break;
                    case "CP": fn = "POWer"; break;
                    case "SHORT": fn = "SHORt"; break;
                    default: fn = "CURRent"; break;
                }
                await SendAsync("FUNCtion " + fn);

                CcPanel.Visibility = mode == "CC" ? Visibility.Visible : Visibility.Collapsed;
                CvPanel.Visibility = mode == "CV" ? Visibility.Visible : Visibility.Collapsed;
                CrPanel.Visibility = mode == "CR" ? Visibility.Visible : Visibility.Collapsed;
                CpPanel.Visibility = mode == "CP" ? Visibility.Visible : Visibility.Collapsed;
                ShortPanel.Visibility = mode == "SHORT" ? Visibility.Visible : Visibility.Collapsed;

                Status("Mode=" + mode);
            }
            catch (Exception ex) { Status("Mode error: " + ex.Message); }
        }

        private async void CcApply_Click(object s, RoutedEventArgs e)
        {
            if (!EnsureConn()) return;
            double a; if (double.TryParse(CcSetBox.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out a))
                await SendAsync("CURRent:LEVel:IMMediate:AMPLitude " + a.ToString("G17", CultureInfo.InvariantCulture));
        }

        private async void OcpApply_Click(object s, RoutedEventArgs e)
        {
            if (!EnsureConn()) return;
            double a; if (double.TryParse(OcpLevelBox.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out a))
                await SendAsync("CURRent:PROTection:LEVel " + a.ToString("G17", CultureInfo.InvariantCulture));
        }

        private async void OcpEnableBox_Changed(object s, RoutedEventArgs e)
        { if (EnsureConn()) await SendAsync("CURRent:PROTection:STATe " + (OcpEnableBox.IsChecked == true ? "ON" : "OFF")); }

        private async void CvApply_Click(object s, RoutedEventArgs e)
        {
            if (!EnsureConn()) return;
            double v; if (double.TryParse(CvSetBox.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                await SendAsync("VOLTage:LEVel:IMMediate:AMPLitude " + v.ToString("G17", CultureInfo.InvariantCulture));
        }

        private async void CvILimitApply_Click(object s, RoutedEventArgs e)
        {
            if (!EnsureConn()) return;
            double a; if (double.TryParse(CvILimitBox.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out a))
                await SendAsync("CURRent:LIMit:LEVel:CV " + a.ToString("G17", CultureInfo.InvariantCulture));
        }

        private async void CrApply_Click(object s, RoutedEventArgs e)
        {
            if (!EnsureConn()) return;
            double r; if (double.TryParse(CrSetBox.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out r))
                await SendAsync("RESistance:LEVel:IMMediate:AMPLitude " + r.ToString("G17", CultureInfo.InvariantCulture));
        }

        private async void CpApply_Click(object s, RoutedEventArgs e)
        {
            if (!EnsureConn()) return;
            double w; if (double.TryParse(CpSetBox.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out w))
                await SendAsync("POWer:LEVel:IMMediate:AMPLitude " + w.ToString("G17", CultureInfo.InvariantCulture));
        }

        private async void OppApply_Click(object s, RoutedEventArgs e)
        {
            if (!EnsureConn()) return;
            double w; if (double.TryParse(OppLevelBox.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out w))
                await SendAsync("POWer:PROTection:LEVel " + w.ToString("G17", CultureInfo.InvariantCulture));
        }

        private async void OppEnableBox_Changed(object s, RoutedEventArgs e)
        { if (EnsureConn()) await SendAsync("POWer:PROTection:STATe " + (OppEnableBox.IsChecked == true ? "ON" : "OFF")); }

        private async void ShortEnableBox_Changed(object s, RoutedEventArgs e)
        { if (EnsureConn()) await SendAsync("INPut:SHORt:FUNCtion " + (ShortEnableBox.IsChecked == true ? "ON" : "OFF")); }

        // ===== AC params =====
        private async void PfApply_Click(object s, RoutedEventArgs e)
        {
            if (!EnsureConn()) return;
            double pf; if (double.TryParse(PfBox.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out pf))
                await SendAsync("PFACtor:LEVel:IMMediate:AMPLitude " + pf.ToString("G17", CultureInfo.InvariantCulture));
        }

        private async void CfApply_Click(object s, RoutedEventArgs e)
        {
            if (!EnsureConn()) return;
            double cf; if (double.TryParse(CfBox.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out cf))
                await SendAsync("CFACtor:LEVel:IMMediate:AMPLitude " + cf.ToString("G17", CultureInfo.InvariantCulture));
        }

        private async void RateCcApply_Click(object s, RoutedEventArgs e)
        { if (EnsureConn()) await SendAsync("RATE:CC " + ((ComboBoxItem)RateCcBox.SelectedItem).Content); }

        private async void RateCvDcApply_Click(object s, RoutedEventArgs e)
        { if (EnsureConn()) await SendAsync("RATE:DCCV " + ((ComboBoxItem)RateCvDcBox.SelectedItem).Content); }

        // ===== Wave/Trigger =====
        private async void TrigSourceApply_Click(object s, RoutedEventArgs e)
        { if (EnsureConn()) await SendAsync("WAVE:TRIGger:SOURce " + ((ComboBoxItem)TrigSourceBox.SelectedItem).Content); }

        private async void TrigModeApply_Click(object s, RoutedEventArgs e)
        { if (EnsureConn()) await SendAsync("WAVE:TRIGger:MODE " + ((ComboBoxItem)TrigModeBox.SelectedItem).Content); }

        private async void TrigSlopeApply_Click(object s, RoutedEventArgs e)
        { if (EnsureConn()) await SendAsync("WAVE:TRIGger:SLOPe " + ((ComboBoxItem)TrigSlopeBox.SelectedItem).Content); }

        private async void TrigLevelApply_Click(object s, RoutedEventArgs e)
        {
            if (!EnsureConn()) return;
            double v; if (double.TryParse(TrigLevelBox.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                await SendAsync("WAVE:TRIGger:VOLTage:LEVel " + v.ToString("G17", CultureInfo.InvariantCulture));
        }

        private async void TrigDelayApply_Click(object s, RoutedEventArgs e)
        {
            if (!EnsureConn()) return;
            double d; if (double.TryParse(TrigDelayBox.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                await SendAsync("WAVE:TRIGger:DELay:TIME " + d.ToString("G17", CultureInfo.InvariantCulture));
        }

        private async void DivTimeApply_Click(object s, RoutedEventArgs e)
        { if (EnsureConn()) await SendAsync("WAVE:TRIGger:DIVTime " + ((ComboBoxItem)DivTimeBox.SelectedItem).Content); }

        private async void Wave_Run_Click(object s, RoutedEventArgs e) { if (EnsureConn()) await SendAsync("WAVE:RUN"); }
        private async void Wave_Stop_Click(object s, RoutedEventArgs e) { if (EnsureConn()) await SendAsync("WAVE:STOP"); }
        private async void Wave_Single_Click(object s, RoutedEventArgs e) { if (EnsureConn()) await SendAsync("WAVE:SINGle"); }

        private async void ExportCsv_Click(object s, RoutedEventArgs e)
        {
            if (!EnsureConn()) return;
            try
            {
                var tuple = await ReadWave(3000);
                if (tuple.Item1 == null) { Status("No waveform"); return; }
                var t = tuple.Item1; var v = tuple.Item2; var i = tuple.Item3;

                string f = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                                        "it8615_wave_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv");
                using (var sw = new StreamWriter(f))
                {
                    sw.WriteLine("time_s,voltage_v,current_a");
                    for (int k = 0; k < t.Length; k++)
                        sw.WriteLine(t[k].ToString("G17", CultureInfo.InvariantCulture) + "," +
                                     v[k].ToString("G17", CultureInfo.InvariantCulture) + "," +
                                     i[k].ToString("G17", CultureInfo.InvariantCulture));
                }
                Status("Saved: " + f);
            }
            catch (Exception ex) { Status("Export error: " + ex.Message); }
        }

        // ===== Polling =====
        private void StartPolling()
        {
            StopPolling();
            _poll = new Timer(500) { AutoReset = true };
            _poll.Elapsed += async delegate { await PollOnce(); };
            _poll.Start();
        }

        private void StopPolling()
        {
            if (_poll != null) { _poll.Stop(); _poll.Dispose(); _poll = null; }
        }

        private async Task PollOnce()
        {
            if (!_connected) return;
            try
            {
                double vrms = await QueryDouble("MEASure:VOLTage:RMS?");
                double irms = await QueryDouble("MEASure:CURRent:RMS?");
                double p = await QueryDouble("MEASure:POWer:ACTive?");
                double s = await QueryDouble("MEASure:POWer:APPArent?");
                double pf = await QueryDouble("MEASure:POWer:PFACtor?");
                double frq = await QueryDouble("MEASure:FREQuency?");
                double thd = await QueryDouble("MEASure:VOLTage:THDistort?");
                double q = (double.IsNaN(s) || double.IsNaN(p)) ? double.NaN : Math.Sqrt(Math.Max(0.0, s * s - p * p));

                await Dispatcher.InvokeAsync(delegate {
                    VrmsText.Text = vrms.ToString("G6", CultureInfo.InvariantCulture);
                    IrmsText.Text = irms.ToString("G6", CultureInfo.InvariantCulture);
                    PText.Text = p.ToString("G6", CultureInfo.InvariantCulture);
                    SText.Text = s.ToString("G6", CultureInfo.InvariantCulture);
                    QText.Text = double.IsNaN(q) ? "" : q.ToString("G6", CultureInfo.InvariantCulture);
                    PFText.Text = pf.ToString("G6", CultureInfo.InvariantCulture);
                    FreqText.Text = frq.ToString("G6", CultureInfo.InvariantCulture);
                    THDText.Text = thd.ToString("G6", CultureInfo.InvariantCulture);
                });

                var tuple = await ReadWave(3000);
                double[] t = tuple.Item1, vArr = tuple.Item2, iArr = tuple.Item3;
                if (t != null)
                {
                    var vList = new List<XY>(t.Length);
                    var iList = new List<XY>(t.Length);
                    for (int k = 0; k < t.Length; k++) { vList.Add(new XY { X = t[k], Y = vArr[k] }); iList.Add(new XY { X = t[k], Y = iArr[k] }); }
                    await Dispatcher.InvokeAsync(delegate {
                        SeriesV.ItemsSource = vList;
                        SeriesI.ItemsSource = iList;
                        WaveChart.UpdateLayout();
                        WaveChart.InvalidateVisual();
                    });
                }
            }
            catch (Exception ex) { await Dispatcher.InvokeAsync(delegate { Status("Poll error: " + ex.Message); }); }
        }

        private async Task<Tuple<double[], double[], double[]>> ReadWave(int maxPoints)
        {
            if (!EnsureConn()) return new Tuple<double[], double[], double[]>(null, null, null);

            double div = await QueryDouble("WAVE:TRIGger:DIVTime?");
            if (!(div > 0)) div = 0.005;

            string vStr = await QueryAsync("WAVE:VOLTage:DATA?\n", 8000);
            string iStr = await QueryAsync("WAVE:CURRent:DATA?\n", 8000);
            if (string.IsNullOrWhiteSpace(vStr) || string.IsNullOrWhiteSpace(iStr))
                return new Tuple<double[], double[], double[]>(null, null, null);

            string[] vParts = vStr.Split(new char[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string[] iParts = iStr.Split(new char[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            int n = Math.Min(vParts.Length, iParts.Length);
            int step = Math.Max(1, n / Math.Max(400, Math.Min(maxPoints, 3000)));
            int len = (n + step - 1) / step;

            double[] V = new double[len], I = new double[len], T = new double[len];
            double total = 10.0 * div, dt = total / Math.Max(1, n - 1);

            int j = 0;
            for (int k = 0; k < n; k += step)
            {
                double vv, ii;
                if (!double.TryParse(vParts[k], NumberStyles.Float, CultureInfo.InvariantCulture, out vv)) vv = double.NaN;
                if (!double.TryParse(iParts[k], NumberStyles.Float, CultureInfo.InvariantCulture, out ii)) ii = double.NaN;
                if (!IsFinite(vv) || !IsFinite(ii)) continue;

                V[j] = vv; I[j] = ii; T[j] = k * dt; j++; if (j >= len) break;
            }
            if (j == 0) return new Tuple<double[], double[], double[]>(null, null, null);
            if (j < len) { Array.Resize(ref V, j); Array.Resize(ref I, j); Array.Resize(ref T, j); }
            return new Tuple<double[], double[], double[]>(T, V, I);
        }

        // ===== VISA helpers =====
        private bool EnsureConn()
        {
            if (_visa == null || !_visa.IsOpen || !_connected)
            {
                Status("Not connected");
                return false;
            }
            return true;
        }

        private Task SendAsync(string cmd)
        {
            if (!EnsureConn()) return Task.CompletedTask;
            return Task.Run(delegate
            {
                lock (_ioLock)
                {
                    // guard to avoid throwing from ScpiDeviceVisa when session closed
                    if (_visa != null && _visa.IsOpen) _visa.Write(cmd);
                }
            });
        }

        private Task<string> QueryAsync(string cmd, int timeoutOverrideMs)
        {
            if (!EnsureConn()) return Task.FromResult<string>(string.Empty);
            return Task.Run(delegate
            {
                lock (_ioLock)
                {
                    if (_visa == null || !_visa.IsOpen) return string.Empty;
                    int old = _visa.TimeoutMs; if (timeoutOverrideMs > 0) _visa.TimeoutMs = timeoutOverrideMs;
                    try { return _visa.Query(cmd); }
                    catch (COMException ex)
                    {
                        if ((uint)ex.HResult == 0x80040011)
                            return string.Empty; // swallow typical read-timeout; caller will treat as empty
                        throw;
                    }
                    finally { if (timeoutOverrideMs > 0) _visa.TimeoutMs = old; }
                }
            });
        }

        private Task<string> QueryAsync(string cmd) => QueryAsync(cmd, 0);

        private async Task<double> QueryDouble(string cmd)
        {
            try
            {
                string s = await QueryAsync(cmd, 4000);
                double v; if (double.TryParse((s ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return v;
            }
            catch { }
            return double.NaN;
        }

        private void Status(string s) { Dispatcher.Invoke(delegate { StatusBlock.Text = s ?? ""; }); }
    }
}
