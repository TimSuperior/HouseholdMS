using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using HouseholdMS.Drivers;
using HouseholdMS.Models;
using HouseholdMS.Services;

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

        private readonly CommandLogger _logger = new CommandLogger();
        private readonly VisaSession _visa = new VisaSession();
        private ItechIt8615 _it;
        private AcquisitionService _acq;

        // Smooth log flushing
        private readonly ConcurrentQueue<string> _logQueue = new ConcurrentQueue<string>();
        private DispatcherTimer _logFlushTimer;

        public It8615Control()
        {
            InitializeComponent();
            DataContext = this;

            // Seed mini-charts so they render immediately
            SeedMiniCharts();

            // Batch logs to UI
            _logger.OnLog += s => _logQueue.Enqueue(s);
            _logFlushTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _logFlushTimer.Tick += (s, e) => FlushLogs();
            _logFlushTimer.Start();
            TxtLogPath.Text = _logger.LogDirectory;

            // Cleanup
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

        private void FlushLogs()
        {
            int added = 0;
            while (added < 100 && _logQueue.TryDequeue(out var line))
            {
                ListLog.Items.Add(line);
                added++;
            }
            while (ListLog.Items.Count > 500)
                ListLog.Items.RemoveAt(0);
        }

        // ---------- helpers ----------
        private bool EnsureConnected()
        {
            if (_it != null) return true;
            MessageBox.Show("Connect to the instrument first.", "Not connected");
            return false;
        }

        private static string GetComboText(ComboBox cbo, string fallback)
        {
            // If it's a simple string item (e.g., "AC"/"DC")
            if (cbo.SelectedItem is string s1)
                return s1;

            // If it's a ComboBoxItem, prefer Tag (machine token) when present; else use Content (display text)
            if (cbo.SelectedItem is ComboBoxItem cbi)
            {
                if (cbi.Tag != null) return cbi.Tag.ToString();
                if (cbi.Content != null) return cbi.Content.ToString();
            }

            // If nothing selected yet, pick first item and try again
            if (cbo.Items.Count > 0)
            {
                cbo.SelectedIndex = 0;

                if (cbo.SelectedItem is string s2)
                    return s2;

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
            => double.TryParse(tb.Text, out var v) ? v : @default;

        private static int ParseIntOr(TextBox tb, int @default, int min, int max)
        {
            if (!int.TryParse(tb.Text, out var v)) v = @default;
            if (v < min) v = min;
            if (v > max) v = max;
            return v;
        }

        // ----- Connection -----
        private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var list = _visa.DiscoverResources(new[] { "USB?*INSTR", "TCPIP?*INSTR", "GPIB?*INSTR" });
                ResourceCombo.ItemsSource = list;
                if (list.Count > 0) ResourceCombo.SelectedIndex = 0;
                SetStatus($"Found {list.Count} VISA resources.");
            }
            catch (Exception ex) { Fail(ex); }
        }

        private async void BtnConnect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (ResourceCombo.SelectedItem == null)
                {
                    MessageBox.Show("Pick a VISA resource.");
                    return;
                }
                _visa.TimeoutMs = int.TryParse(TxtTimeout.Text, out var t) ? t : 2000;
                _visa.Retries = int.TryParse(TxtRetries.Text, out var r) ? r : 2;

                _visa.Open((string)ResourceCombo.SelectedItem, _logger);
                _it = new ItechIt8615(_visa, _logger);

                string idn = await _it.IdentifyAsync();
                TxtIdn.Text = idn;

                await _it.ToRemoteAsync();
                await _it.DrainErrorQueueAsync();
                await _it.CacheRangesAsync();
                TxtLimits.Text = _it.DescribeRanges();

                _acq = new AcquisitionService(_it, _logger);
                _acq.OnReading += rr => Dispatcher.Invoke(() => UpdateMeter(rr));
                _acq.Start(hz: 5);

                if (CboTrigSource.SelectedIndex < 0 && CboTrigSource.Items.Count > 0) CboTrigSource.SelectedIndex = 0;
                if (CboTrigSlope.SelectedIndex < 0 && CboTrigSlope.Items.Count > 0) CboTrigSlope.SelectedIndex = 0;

                SetStatus("Connected.");
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
                SetStatus("Disconnected.");
            }
            catch (Exception ex) { Fail(ex); }
        }

        private async Task SafeShutdownAsync()
        {
            try
            {
                _logFlushTimer?.Stop();
                _acq?.Stop();
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
                SetStatus("Setpoint applied.");
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
                SetStatus("Power factor / crest factor applied.");
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
                SetStatus("Emergency stop executed.");
            }
            catch (Exception ex) { Fail(ex); }
        }

        // ----- Scope -----
        private async void BtnScopeRun_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!EnsureConnected()) return;

                string src = GetComboText(CboTrigSource, "VOLTage");
                string slp = GetComboText(CboTrigSlope, "POSitive");
                double lev = ParseDoubleOr(TxtTrigLevel, 0);

                await _it.ScopeConfigureAsync(src, slp, lev);
                await _it.ScopeRunAsync();
                await RefreshScopeAsync();
            }
            catch (Exception ex) { Fail(ex); }
        }

        private async void BtnScopeSingle_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!EnsureConnected()) return;

                string src = GetComboText(CboTrigSource, "VOLTage");
                string slp = GetComboText(CboTrigSlope, "POSitive");
                double lev = ParseDoubleOr(TxtTrigLevel, 0);

                await _it.ScopeConfigureAsync(src, slp, lev);
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
            }
            catch (Exception ex) { Fail(ex); }
        }

        private async Task RefreshScopeAsync()
        {
            if (!EnsureConnected()) return;

            var tup = await _it.FetchWaveformsAsync();
            var v = tup.v ?? Array.Empty<double>();
            var i = tup.i ?? Array.Empty<double>();

            ScopeV.Clear();
            ScopeI.Clear();

            for (int k = 0; k < v.Length; k++)
                ScopeV.Add(new SamplePoint { Index = k, Value = v[k] });
            for (int k = 0; k < i.Length; k++)
                ScopeI.Add(new SamplePoint { Index = k, Value = i[k] });
        }

        // ----- Harmonics -----
        private async void BtnMeasureHarmonics_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!EnsureConnected()) return;

                int n = ParseIntOr(TxtHarmonicsN, 50, 1, 50);
                double[] harm = await _it.MeasureVoltageHarmonicsAsync(n);
                if (harm == null) return;

                Harmonics.Clear();
                for (int k = 0; k < harm.Length; k++)
                    Harmonics.Add(new SamplePoint { Index = k + 1, Value = harm[k] });
            }
            catch (Exception ex) { Fail(ex); }
        }

        // ----- Results -----
        private void BtnExportCsv_Click(object sender, RoutedEventArgs e)
        {
            var d = new SaveFileDialog { Filter = "CSV|*.csv" };
            if (d.ShowDialog() == true && _acq != null)
            {
                _acq.ExportCsv(d.FileName);
                TxtStatus.Text = "Exported " + d.FileName;
            }
        }

        private void BtnReport_Click(object sender, RoutedEventArgs e)
        {
            var d = new SaveFileDialog { Filter = "PDF|*.pdf" };
            if (d.ShowDialog() == true)
            {
                var kv = new Tuple<string, string>[] {
                    Tuple.Create("Voltage RMS", ValVrms.Text),
                    Tuple.Create("Current RMS", ValIrms.Text),
                    Tuple.Create("Power",       ValPower.Text),
                    Tuple.Create("Power Factor",ValPf.Text),
                    Tuple.Create("Frequency",   ValFreq.Text)
                };
                ReportBuilderPdf.WriteSimplePdf(d.FileName, "IT8615 Report", kv);
                TxtStatus.Text = "Report " + d.FileName;
            }
        }

        private void BtnOpenLogFolder_Click(object sender, RoutedEventArgs e)
        {
            var dir = _logger.LogDirectory;
            if (Directory.Exists(dir))
                System.Diagnostics.Process.Start("explorer.exe", dir);
        }

        private void SetStatus(string s) => TxtStatus.Text = s;
        private void Fail(Exception ex)
        {
            _logger.Log("ERR: " + ex.Message);
            MessageBox.Show(ex.Message, "Error");
        }
    }
}
