using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using HouseholdMS.Drivers;
using HouseholdMS.Models;
using HouseholdMS.Services;
using HouseholdMS.Resources; // <-- for Strings

namespace HouseholdMS.Controls
{
    public partial class It8615Control : UserControl
    {
        // Big scope series
        public ObservableCollection<SamplePoint> ScopeV { get; } = new ObservableCollection<SamplePoint>();
        public ObservableCollection<SamplePoint> ScopeI { get; } = new ObservableCollection<SamplePoint>();
        public ObservableCollection<SamplePoint> Harmonics { get; } = new ObservableCollection<SamplePoint>();

        // Tiny sparkline series
        public ObservableCollection<SamplePoint> VrmsHistory { get; } = new ObservableCollection<SamplePoint>();
        public ObservableCollection<SamplePoint> IrmsHistory { get; } = new ObservableCollection<SamplePoint>();
        public ObservableCollection<SamplePoint> PowerHistory { get; } = new ObservableCollection<SamplePoint>();
        public ObservableCollection<SamplePoint> PfHistory { get; } = new ObservableCollection<SamplePoint>();
        public ObservableCollection<SamplePoint> FreqHistory { get; } = new ObservableCollection<SamplePoint>();
        public ObservableCollection<SamplePoint> CfHistory { get; } = new ObservableCollection<SamplePoint>();

        private int _idxVrms, _idxIrms, _idxPow, _idxPf, _idxFreq, _idxCf;
        private const int MiniMaxPoints = 200;

        private readonly VisaSession _visa = new VisaSession();
        private ItechIt8615 _it;
        private AcquisitionService _acq;

        // LIVE scope polling
        private DispatcherTimer _scopeTimer;
        private bool _scopeTickBusy;

        public It8615Control()
        {
            InitializeComponent();
            DataContext = this;

            // Localized items for the "Knob" ComboBox — keep the token as prefix (UR/AR/UB/AB/TL/TD/T/d)
            CboKnob.ItemsSource = new[]
            {
                Strings.IT8615_Knob_UR,
                Strings.IT8615_Knob_AR,
                Strings.IT8615_Knob_UB,
                Strings.IT8615_Knob_AB,
                Strings.IT8615_Knob_TL,
                Strings.IT8615_Knob_TD,
                Strings.IT8615_Knob_Tdiv
            };

            SeedMiniCharts();

            // LIVE scope timer (~6-7 Hz)
            _scopeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            _scopeTimer.Tick += async (s, e) => await ScopeTimerTickAsync();

            // Safe defaults for trigger pickers even before connect
            if (CboTrigSource != null && CboTrigSource.Items.Count > 0 && CboTrigSource.SelectedIndex < 0) CboTrigSource.SelectedIndex = 0;
            if (CboTrigSlope != null && CboTrigSlope.Items.Count > 0 && CboTrigSlope.SelectedIndex < 0) CboTrigSlope.SelectedIndex = 0;

            Unloaded += async (_, __) => await SafeShutdownAsync();
        }

        private void SeedMiniCharts()
        {
            VrmsHistory.Add(new SamplePoint { Index = 0, Value = 0 });
            IrmsHistory.Add(new SamplePoint { Index = 0, Value = 0 });
            PowerHistory.Add(new SamplePoint { Index = 0, Value = 0 });
            PfHistory.Add(new SamplePoint { Index = 0, Value = 0 });
            FreqHistory.Add(new SamplePoint { Index = 0, Value = 0 });
            CfHistory.Add(new SamplePoint { Index = 0, Value = 0 });
        }

        // ---------- helpers ----------
        private bool EnsureConnected()
        {
            if (_it != null) return true;
            MessageBox.Show(Strings.IT8615_Msg_NotConnectedBody, Strings.IT8615_Msg_NotConnectedTitle);
            return false;
        }

        private static string GetComboText(ComboBox cbo, string fallback)
        {
            if (cbo == null) return fallback;
            if (cbo.SelectedItem is string s1) return s1;

            if (cbo.SelectedItem is ComboBoxItem cbi)
            {
                if (cbi.Tag != null) return cbi.Tag.ToString();
                if (cbi.Content != null) return cbi.Content.ToString();
            }

            if (cbo.Items.Count > 0)
            {
                cbo.SelectedIndex = 0;

                if (cbo.SelectedItem is string s2) return s2;

                var first = cbo.SelectedItem as ComboBoxItem;
                if (first != null)
                {
                    if (first.Tag != null) return first.Tag.ToString();
                    if (first.Content != null) return first.Content.ToString();
                }
            }

            return fallback;
        }

        private static double ParseDoubleOr(TextBox tb, double @default)
            => (tb != null && double.TryParse(tb.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)) ? v : @default;

        private static int ParseIntOr(TextBox tb, int @default, int min, int max)
        {
            int v;
            if (tb == null || !int.TryParse(tb.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) v = @default;
            if (v < min) v = min;
            if (v > max) v = max;
            return v;
        }

        private static void ReplaceSeries(ObservableCollection<SamplePoint> series, double[] data)
        {
            series.Clear();
            if (data == null || data.Length == 0) return;
            for (int k = 0; k < data.Length; k++)
                series.Add(new SamplePoint { Index = k, Value = data[k] });
        }

        private void ReplaceScopeSeries(double[] v, double[] i)
        {
            ReplaceSeries(ScopeV, v);
            ReplaceSeries(ScopeI, i);
        }

        private (string src, string slp, double lvl) ReadTriggerInputs()
        {
            var src = GetComboText(CboTrigSource, "VOLTage");
            var slp = GetComboText(CboTrigSlope, "POSitive");
            var lvl = ParseDoubleOr(TxtTrigLevel, 0);
            return (src, slp, lvl);
        }

        // ----- Connection -----
        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var list = _visa.DiscoverResources(new[] { "USB?*INSTR", "TCPIP?*INSTR", "GPIB?*INSTR" })
                                .Distinct()
                                .ToList();
                ResourceCombo.ItemsSource = list;

                int idx = list.FindIndex(s =>
                    s.IndexOf("IT8615", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    s.IndexOf("ITECH", StringComparison.OrdinalIgnoreCase) >= 0);
                ResourceCombo.SelectedIndex = (idx >= 0) ? idx : (list.Count > 0 ? 0 : -1);

                SetStatus(string.Format(CultureInfo.InvariantCulture, Strings.IT8615_Status_DiscoveredFmt, list.Count));
            }
            catch (Exception ex) { Fail(ex); }
        }

        private async void BtnConnect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (ResourceCombo.SelectedItem == null)
                {
                    MessageBox.Show(Strings.IT8615_Msg_PickResource, Strings.IT8615_Error_Title);
                    return;
                }

                _visa.TimeoutMs = int.TryParse(TxtTimeout.Text, out var t) ? t : 2000;
                _visa.Retries = int.TryParse(TxtRetries.Text, out var r) ? r : 2;

                _visa.Open((string)ResourceCombo.SelectedItem);

                var probe = new ItechIt8615(_visa);
                string idn = (await probe.IdentifyAsync()) ?? string.Empty;

                if (!(idn.IndexOf("ITECH", StringComparison.OrdinalIgnoreCase) >= 0 &&
                      (idn.IndexOf("IT8615", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       idn.IndexOf("IT86", StringComparison.OrdinalIgnoreCase) >= 0)))
                {
                    _visa.Close();
                    MessageBox.Show(Strings.IT8615_Msg_WrongInstrumentBody, Strings.IT8615_Msg_WrongInstrumentTitle);
                    return;
                }

                _it = probe;
                TxtIdn.Text = idn;

                await _it.ToRemoteAsync();
                await _it.DrainErrorQueueAsync();
                await _it.CacheRangesAsync();
                TxtLimits.Text = _it.DescribeRanges();

                _acq = new AcquisitionService(_it);
                _acq.OnReading += rr => Dispatcher.Invoke(() => UpdateMeter(rr));
                _acq.Start(hz: 5);

                if (CboTrigSource != null && CboTrigSource.SelectedIndex < 0 && CboTrigSource.Items.Count > 0) CboTrigSource.SelectedIndex = 0;
                if (CboTrigSlope != null && CboTrigSlope.SelectedIndex < 0 && CboTrigSlope.Items.Count > 0) CboTrigSlope.SelectedIndex = 0;

                SetStatus(Strings.IT8615_Status_Connected);
            }
            catch (Exception ex) { Fail(ex); }
        }

        private async void BtnDisconnect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await SafeShutdownAsync();
                _visa.Close();
                _it = null;
                _acq = null;
                SetStatus(Strings.IT8615_Status_Disconnected);
            }
            catch (Exception ex) { Fail(ex); }
        }

        private async Task SafeShutdownAsync()
        {
            try
            {
                StopScopeLive();
                _acq?.Stop();
                await Task.Delay(150);

                if (_it != null)
                {
                    try { await _it.EnableInputAsync(false); } catch { }
                    try { await _it.ToLocalAsync(); } catch { }
                }
            }
            catch { }
        }

        // ----- Meter + tiny charts -----
        private void UpdateMeter(InstrumentReading r)
        {
            ValVrms.Text = r.Vrms.ToString("F3") + " V";
            ValIrms.Text = r.Irms.ToString("F3") + " A";
            ValPower.Text = r.Power.ToString("F3") + " W";
            ValPf.Text = r.Pf.ToString("F3");
            ValFreq.Text = r.Freq.ToString("F2") + " Hz";
            ValCf.Text = r.CrestFactor.ToString("F2");

            AddMiniPoint(VrmsHistory, ref _idxVrms, r.Vrms);
            AddMiniPoint(IrmsHistory, ref _idxIrms, r.Irms);
            AddMiniPoint(PowerHistory, ref _idxPow, r.Power);
            AddMiniPoint(PfHistory, ref _idxPf, r.Pf);
            AddMiniPoint(FreqHistory, ref _idxFreq, r.Freq);
            AddMiniPoint(CfHistory, ref _idxCf, r.CrestFactor);
        }

        private void AddMiniPoint(ObservableCollection<SamplePoint> series, ref int idx, double value)
        {
            series.Add(new SamplePoint { Index = idx++, Value = value });
            if (series.Count > MiniMaxPoints)
                series.RemoveAt(0);
        }

        // ----- Parameters -----
        private async void BtnApplySetpoint_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!EnsureConnected()) return;

                string acdc = GetComboText(CboAcDc, "AC");
                string f = GetComboText(CboFunction, "CURR");
                double set = ParseDoubleOr(TxtSetpoint, 1.0);

                await _it.SetAcDcAsync(acdc == "AC");
                await _it.SetFunctionAsync(f);
                await _it.SetSetpointAsync(set);
                TxtSetpointUnits.Text = _it.CurrentUnits;
                SetStatus(Strings.IT8615_Status_SetpointApplied);
            }
            catch (Exception ex) { Fail(ex); }
        }

        private async void ChkEnable_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!EnsureConnected()) { ChkEnable.IsChecked = false; return; }
                await _it.EnableInputAsync(ChkEnable.IsChecked == true);
            }
            catch (Exception ex) { Fail(ex); }
        }

        private async void BtnApplyPfCf_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!EnsureConnected()) return;
                double pf = ParseDoubleOr(TxtPf, 1.0);
                double cf = ParseDoubleOr(TxtCf, 1.41);
                await _it.SetPfCfAsync(pf, cf);
                SetStatus(Strings.IT8615_Status_PfCfApplied);
            }
            catch (Exception ex) { Fail(ex); }
        }

        private async void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!EnsureConnected()) return;
                await _it.EnableInputAsync(true);
                ChkEnable.IsChecked = true;
            }
            catch (Exception ex) { Fail(ex); }
        }

        private async void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!EnsureConnected()) return;
                await _it.EnableInputAsync(false);
                ChkEnable.IsChecked = false;
            }
            catch (Exception ex) { Fail(ex); }
        }

        private async void BtnEStop_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!EnsureConnected()) return;
                await _it.EStopAsync();
                ChkEnable.IsChecked = false;
                SetStatus(Strings.IT8615_Status_EStopExecuted);
            }
            catch (Exception ex) { Fail(ex); }
        }

        // ===================== SCOPE =====================
        private void StartScopeLive()
        {
            if (_scopeTimer != null && !_scopeTimer.IsEnabled)
                _scopeTimer.Start();
        }

        private void StopScopeLive()
        {
            if (_scopeTimer != null && _scopeTimer.IsEnabled)
                _scopeTimer.Stop();
        }

        private async Task ScopeTimerTickAsync()
        {
            if (_scopeTickBusy || _it == null) return;
            _scopeTickBusy = true;

            try
            {
                var tup = await _it.FetchWaveformsAsync();
                var v = tup.v ?? Array.Empty<double>();
                var i = tup.i ?? Array.Empty<double>();

                if (v.Length > 0 || i.Length > 0)
                {
                    ReplaceScopeSeries(v, i);
                    SetStatus(string.Format(CultureInfo.InvariantCulture, Strings.IT8615_Status_ScopeLiveFmt, v.Length, i.Length));
                }
            }
            catch (Exception ex)
            {
                StopScopeLive();
                Fail(ex);
            }
            finally
            {
                _scopeTickBusy = false;
            }
        }

        private async void BtnScopeRun_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!EnsureConnected()) return;

                await ApplyTriggerInternalAsync();
                await _it.ScopeRunAsync();

                StartScopeLive();
            }
            catch (Exception ex) { Fail(ex); }
        }

        private async void BtnScopeSingle_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!EnsureConnected()) return;

                StopScopeLive();
                await ApplyTriggerInternalAsync();
                await _it.ScopeSingleAsync();
                await RefreshScopeAsync();
            }
            catch (Exception ex) { Fail(ex); }
        }

        private async void BtnScopeStop_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!EnsureConnected()) return;
                await _it.ScopeStopAsync();
                StopScopeLive();
                SetStatus(Strings.IT8615_Status_ScopeStopped);
            }
            catch (Exception ex) { Fail(ex); }
        }

        private async Task RefreshScopeAsync()
        {
            if (!EnsureConnected()) return;

            const int maxTries = 10;
            const int delayMs = 120;

            double[] v = null;
            double[] i = null;

            for (int t = 0; t < maxTries; t++)
            {
                var tup = await _it.FetchWaveformsAsync();
                v = tup.v ?? Array.Empty<double>();
                i = tup.i ?? Array.Empty<double>();

                if ((v.Length > 8) || (i.Length > 8)) break;
                await Task.Delay(delayMs);
            }

            ReplaceScopeSeries(v, i);

            if ((v == null || v.Length == 0) && (i == null || i.Length == 0))
                SetStatus(Strings.IT8615_Status_ScopeNoData);
            else
                SetStatus(string.Format(CultureInfo.InvariantCulture, Strings.IT8615_Status_ScopePointsFmt,
                    v != null ? v.Length : 0, i != null ? i.Length : 0));
        }

        private async void BtnApplyDivTime_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!EnsureConnected()) return;
                var cbi = CboDivTime.SelectedItem as ComboBoxItem;
                var t = (cbi?.Content as string) ?? "0.01";
                await _it.ScopeSetDivTimeAsync(t);
                SetStatus(string.Format(CultureInfo.InvariantCulture, Strings.IT8615_Status_TimeDivFmt, t));
            }
            catch (Exception ex) { Fail(ex); }
        }

        private async void BtnApplyTrigger_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!EnsureConnected()) return;
                await ApplyTriggerInternalAsync();
                await RefreshTrigStateAsync();
                SetStatus(Strings.IT8615_Status_TriggerApplied);
            }
            catch (Exception ex) { Fail(ex); }
        }

        private async void BtnApplyVertical_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!EnsureConnected()) return;
                double vb = ParseDoubleOr(TxtVBase, 0);
                double vr = ParseDoubleOr(TxtVRange, 260);
                double ab = ParseDoubleOr(TxtABase, 0);
                double ar = ParseDoubleOr(TxtARange, 20);

                await _it.ScopeSetVoltageBaseAsync(vb);
                await _it.ScopeSetVoltageRangeAsync(vr);
                await _it.ScopeSetCurrentBaseAsync(ab);
                await _it.ScopeSetCurrentRangeAsync(ar);
                SetStatus(string.Format(CultureInfo.InvariantCulture, Strings.IT8615_Status_VerticalSetFmt, vb, vr, ab, ar));
            }
            catch (Exception ex) { Fail(ex); }
        }

        private async void BtnApplyDisplayAndAverage_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!EnsureConnected()) return;
                var disp = GetComboText(CboScopeSel, "UA");
                int avg = ParseIntOr(TxtAvgCount, 4, 1, 16);
                await _it.ScopeSetDisplaySelectionAsync(disp);
                await _it.ScopeSetAverageCountAsync(avg);
                SetStatus(string.Format(CultureInfo.InvariantCulture, Strings.IT8615_Status_DisplayAvgFmt, disp, avg));
            }
            catch (Exception ex) { Fail(ex); }
        }

        private async void BtnApplyKnob_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!EnsureConnected()) return;
                var human = (CboKnob.SelectedItem as string) ?? Strings.IT8615_Knob_UR;
                var token = human.StartsWith("UR") ? "UR"
                         : human.StartsWith("AR") ? "AR"
                         : human.StartsWith("UB") ? "UB"
                         : human.StartsWith("AB") ? "AB"
                         : human.StartsWith("TL") ? "TL"
                         : human.StartsWith("TD") ? "TD"
                         : "T/d";
                await _it.ScopeSetKnobSelectionAsync(token);
                SetStatus(string.Format(CultureInfo.InvariantCulture, Strings.IT8615_Status_KnobFmt, token));
            }
            catch (Exception ex) { Fail(ex); }
        }

        private async Task ApplyTriggerInternalAsync()
        {
            var t = ReadTriggerInputs();
            string mode = GetComboText(CboTrigMode, "AUTO");
            double dly = ParseDoubleOr(TxtTrigDelay, 0);

            await _it.ScopeSetTriggerSourceAsync(t.src);
            await _it.ScopeSetTriggerSlopeAsync(t.slp);
            await _it.ScopeSetTriggerModeAsync(mode);
            await _it.ScopeSetTriggerDelayAsync(dly);

            if (t.src.Equals("VOLTage", StringComparison.OrdinalIgnoreCase))
                await _it.ScopeSetTriggerLevelVoltageAsync(t.lvl);
            else
                await _it.ScopeSetTriggerLevelCurrentAsync(t.lvl);
        }

        private async Task RefreshTrigStateAsync()
        {
            try
            {
                var s = await _it.ScopeQueryTriggerStateAsync();
                if (TxtTrigState != null) TxtTrigState.Text = Strings.IT8615_Trig_StatePrefix + s;
            }
            catch
            {
                if (TxtTrigState != null) TxtTrigState.Text = "";
            }
        }

        private void SetStatus(string s) => TxtStatus.Text = s;

        private void Fail(Exception ex)
        {
            SetStatus("ERR: " + ex.Message);
            MessageBox.Show(ex.Message, Strings.Common_ErrorCaption);
        }

        // --------- Harmonics ----------
        private async void BtnMeasureHarmonics_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!EnsureConnected()) return;

                int n = ParseIntOr(TxtHarmonicsN, 50, 1, 1000);

                var amps = await _it.MeasureVoltageHarmonicsAsync(n);
                if (amps == null || amps.Length == 0)
                {
                    Harmonics.Clear();
                    SetStatus(Strings.IT8615_Status_Harm_NoData);
                    return;
                }

                double fundamental = Math.Abs(amps[0]);
                Harmonics.Clear();
                for (int k = 0; k < amps.Length && k < n; k++)
                {
                    double a = amps[k];
                    double y = (fundamental > 1e-12) ? (a / fundamental) : a;
                    Harmonics.Add(new SamplePoint { Index = k + 1, Value = y });
                }

                SetStatus(string.Format(CultureInfo.InvariantCulture, Strings.IT8615_Status_Harm_CapturedFmt, Harmonics.Count));
            }
            catch (Exception ex)
            {
                Fail(ex);
            }
        }
    }

    internal static class ItechIt8615ScopeExtensions
    {
        private static object GetVisa(ItechIt8615 it)
        {
            var t = it.GetType();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

            string[] fieldNames = { "_io", "_visa", "io", "visa" };
            for (int i = 0; i < fieldNames.Length; i++)
            {
                var f = t.GetField(fieldNames[i], flags);
                if (f != null)
                {
                    var v = f.GetValue(it);
                    if (v != null) return v;
                }
            }

            string[] propNames = { "Io", "IO", "Visa", "Session" };
            for (int i = 0; i < propNames.Length; i++)
            {
                var p = t.GetProperty(propNames[i], flags);
                if (p != null)
                {
                    var v = p.GetValue(it, null);
                    if (v != null) return v;
                }
            }

            var anyVisa = t.GetFields(flags)
                           .Select(f => f.GetValue(it))
                           .FirstOrDefault(v => v != null && v.GetType().Name.Contains("VisaSession"));
            if (anyVisa != null) return anyVisa;

            throw new InvalidOperationException("Could not access internal Visa session of ItechIt8615.");
        }

        // Cache reflection per driver instance to avoid repeated lookups.
        private sealed class SessionAccessor
        {
            public object Visa;
            public MethodInfo WriteMethod;
            public MethodInfo QueryMethod;
        }

        private static readonly ConditionalWeakTable<ItechIt8615, SessionAccessor> _cache =
            new ConditionalWeakTable<ItechIt8615, SessionAccessor>();

        private static SessionAccessor GetAccessor(ItechIt8615 it)
        {
            if (_cache.TryGetValue(it, out var acc)) return acc;

            var visa = GetVisa(it);
            var vt = visa.GetType();
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            var write = vt.GetMethod("WriteAsync", flags)
                    ?? vt.GetMethod("WriteLineAsync", flags)
                    ?? vt.GetMethod("Write", flags);

            var query = vt.GetMethod("QueryAsync", flags)
                    ?? vt.GetMethod("QueryStringAsync", flags)
                    ?? vt.GetMethod("Query", flags);

            if (write == null) throw new MissingMethodException("VisaSession.WriteAsync/Write not found.");
            if (query == null) throw new MissingMethodException("VisaSession.QueryAsync/Query not found.");

            acc = new SessionAccessor { Visa = visa, WriteMethod = write, QueryMethod = query };
            _cache.Add(it, acc);
            return acc;
        }

        private static async Task ScpiWriteAsync(ItechIt8615 it, string cmd)
        {
            var acc = GetAccessor(it);
            var ret = acc.WriteMethod.Invoke(acc.Visa, new object[] { cmd });
            if (ret is Task task) await task;
        }

        private static async Task<string> ScpiQueryStringAsync(ItechIt8615 it, string cmd)
        {
            var acc = GetAccessor(it);
            var ret = acc.QueryMethod.Invoke(acc.Visa, new object[] { cmd });

            if (ret is Task<string> ts) return await ts;
            if (ret is string s) return s;
            if (ret is Task t) { await t; return string.Empty; }

            return string.Empty;
        }

        private static string F(double v) => v.ToString("G", CultureInfo.InvariantCulture);

        public static Task ScopeSetTriggerSourceAsync(this ItechIt8615 it, string src)
            => ScpiWriteAsync(it, "WAVE:TRIGger:SOURce " + src);

        public static Task ScopeSetTriggerSlopeAsync(this ItechIt8615 it, string slope)
            => ScpiWriteAsync(it, "WAVE:TRIGger:SLOPe " + slope);

        public static Task ScopeSetTriggerModeAsync(this ItechIt8615 it, string mode)
            => ScpiWriteAsync(it, "WAVE:TRIGger:MODE " + mode);

        public static Task ScopeSetTriggerDelayAsync(this ItechIt8615 it, double seconds)
            => ScpiWriteAsync(it, "WAVE:TRIGger:DELay:TIME " + F(seconds));

        public static Task ScopeSetTriggerLevelVoltageAsync(this ItechIt8615 it, double levelV)
            => ScpiWriteAsync(it, "WAVE:TRIGger:VOLTage:LEVel " + F(levelV));

        public static Task ScopeSetTriggerLevelCurrentAsync(this ItechIt8615 it, double levelA)
            => ScpiWriteAsync(it, "WAVE:TRIGger:CURRent:LEVel " + F(levelA));

        public static Task ScopeSetDivTimeAsync(this ItechIt8615 it, string secondsPerDiv)
            => ScpiWriteAsync(it, "WAVE:TRIGger:DIVTime " + secondsPerDiv);

        public static Task ScopeSetVoltageBaseAsync(this ItechIt8615 it, double vBase)
            => ScpiWriteAsync(it, "WAVE:VOLTage:BASE " + F(vBase));

        public static Task ScopeSetVoltageRangeAsync(this ItechIt8615 it, double vRange)
            => ScpiWriteAsync(it, "WAVE:VOLTage:RANGe " + F(vRange));

        public static Task ScopeSetCurrentBaseAsync(this ItechIt8615 it, double aBase)
            => ScpiWriteAsync(it, "WAVE:CURRent:BASE " + F(aBase));

        public static Task ScopeSetCurrentRangeAsync(this ItechIt8615 it, double aRange)
            => ScpiWriteAsync(it, "WAVE:CURRent:RANGe " + F(aRange));

        public static Task ScopeSetDisplaySelectionAsync(this ItechIt8615 it, string selection /* U|A|UA */)
            => ScpiWriteAsync(it, "WAVE:SCOPe:SELection " + selection);

        public static Task ScopeSetAverageCountAsync(this ItechIt8615 it, int count /*1-16*/)
            => ScpiWriteAsync(it, "AVERage:COUNt " + count);

        public static Task ScopeSetKnobSelectionAsync(this ItechIt8615 it, string token /*UR|AR|UB|AB|TL|TD|T/d*/)
            => ScpiWriteAsync(it, "WAVE:KNOB:SELection " + token);

        public static Task<string> ScopeQueryTriggerStateAsync(this ItechIt8615 it)
            => ScpiQueryStringAsync(it, "WAVE:TRIGger:STATe?");
    }
}
