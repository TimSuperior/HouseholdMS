using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using HouseholdMS.Drivers;
using HouseholdMS.Models;
using HouseholdMS.Services;

namespace HouseholdMS.Controls
{
    public partial class It8615Control : UserControl
    {
        // Data for Syncfusion charts (bind in XAML)
        public ObservableCollection<SamplePoint> ScopeV { get; private set; } = new ObservableCollection<SamplePoint>();
        public ObservableCollection<SamplePoint> ScopeI { get; private set; } = new ObservableCollection<SamplePoint>();
        public ObservableCollection<SamplePoint> Harmonics { get; private set; } = new ObservableCollection<SamplePoint>();

        private readonly CommandLogger _logger = new CommandLogger();
        private readonly VisaSession _visa = new VisaSession();
        private ItechIt8615 _it;
        private AcquisitionService _acq;
        private readonly ObservableCollection<SequenceStep> _steps = new ObservableCollection<SequenceStep>();

        public It8615Control()
        {
            InitializeComponent();
            this.DataContext = this;

            _logger.OnLog += delegate (string s)
            {
                Dispatcher.Invoke(delegate
                {
                    ListLog.Items.Add(s);
                    if (ListLog.Items.Count > 2000) ListLog.Items.RemoveAt(0);
                    TxtLogPath.Text = _logger.LogDirectory;
                });
            };

            GridSteps.ItemsSource = _steps;
            _steps.Add(new SequenceStep { Index = 1, DurationMs = 1000, AcDc = "AC", Function = "CURR", Setpoint = 1.0, Pf = 1.0, Cf = 1.41, Repeat = 1 });
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
            // ItemsSource may be an array of strings OR ComboBoxItem elements.
            if (cbo.SelectedItem is string s1) return s1;
            var item = cbo.SelectedItem as ComboBoxItem;
            if (item != null) return item.Content != null ? item.Content.ToString() : fallback;

            if (cbo.Items.Count > 0)
            {
                cbo.SelectedIndex = 0;
                if (cbo.SelectedItem is string s2) return s2;
                var first = cbo.SelectedItem as ComboBoxItem;
                if (first != null && first.Content != null) return first.Content.ToString();
            }
            return fallback;
        }

        private static double ParseDoubleOr(TextBox tb, double @default)
        {
            double v;
            return double.TryParse(tb.Text, out v) ? v : @default;
        }

        private static int ParseIntOr(TextBox tb, int @default, int min, int max)
        {
            int v;
            if (!int.TryParse(tb.Text, out v)) v = @default;
            if (v < min) v = min;
            if (v > max) v = max;
            return v;
        }

        // ----- Connection -----
        private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var list = _visa.DiscoverResources(new string[] { "USB?*INSTR", "TCPIP?*INSTR", "GPIB?*INSTR" });
                ResourceCombo.ItemsSource = list;
                if (list.Count > 0) ResourceCombo.SelectedIndex = 0;
                SetStatus("Found " + list.Count + " VISA resources.");
            }
            catch (Exception ex) { Fail(ex); }
        }

        private async void BtnConnect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (ResourceCombo.SelectedItem == null) { MessageBox.Show("Pick a VISA resource."); return; }
                int t; if (!int.TryParse(TxtTimeout.Text, out t)) t = 2000; _visa.TimeoutMs = t;
                int r; if (!int.TryParse(TxtRetries.Text, out r)) r = 2; _visa.Retries = r;

                _visa.Open((string)ResourceCombo.SelectedItem, _logger);
                _it = new ItechIt8615(_visa, _logger);

                string idn = await _it.IdentifyAsync();
                TxtIdn.Text = idn;

                await _it.ToRemoteAsync();
                await _it.DrainErrorQueueAsync();
                await _it.CacheRangesAsync();
                TxtLimits.Text = _it.DescribeRanges();

                _acq = new AcquisitionService(_it, _logger);
                _acq.OnReading += delegate (InstrumentReading rr) { Dispatcher.Invoke(delegate { UpdateMeter(rr); }); };
                _acq.Start();

                // Make sure trigger combo boxes have a selection to avoid NREs later
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
                if (_acq != null) _acq.Stop();
                if (_it != null)
                {
                    try { await _it.EnableInputAsync(false); } catch { }
                    try { await _it.ToLocalAsync(); } catch { }
                }
            }
            catch { }
        }

        private void UpdateMeter(InstrumentReading r)
        {
            ValVrms.Text = r.Vrms.ToString("F3") + " V";
            ValIrms.Text = r.Irms.ToString("F3") + " A";
            ValPower.Text = r.Power.ToString("F3") + " W";
            ValPf.Text = r.Pf.ToString("F3");
            ValFreq.Text = r.Freq.ToString("F2") + " Hz";
            ValCf.Text = r.CrestFactor.ToString("F2");
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
                SetStatus("PF/CF applied.");
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
                SetStatus("E-STOP executed.");
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

        // replace the whole method with this
        private async Task RefreshScopeAsync()
        {
            if (!EnsureConnected()) return;

            // ValueTuple cannot be null; check the inner arrays instead
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

        // ----- Sequence -----
        private void BtnAddStep_Click(object sender, RoutedEventArgs e)
        {
            _steps.Add(new SequenceStep { Index = _steps.Count + 1, DurationMs = 1000, AcDc = "AC", Function = "CURR", Setpoint = 1, Pf = 1.0, Cf = 1.41, Repeat = 1 });
        }

        private void BtnDeleteStep_Click(object sender, RoutedEventArgs e)
        {
            var st = GridSteps.SelectedItem as SequenceStep;
            if (st != null) _steps.Remove(st);
            Reindex();
        }

        private void BtnPreflight_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureConnected()) return;
            var list = SequenceRunner.Preflight(_steps.ToList(), _it);
            if (list.Count > 0) MessageBox.Show("Preflight issues:\n" + string.Join("\n", list), "Preflight");
            else MessageBox.Show("Preflight OK");
        }

        private async void BtnRunSeq_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!EnsureConnected()) return;
                await SequenceRunner.RunAsync(_steps.ToList(), _it, _logger, ChkLoopSeq.IsChecked == true, CancellationToken.None);
            }
            catch (Exception ex) { Fail(ex); }
        }

        private void BtnLoadSeq_Click(object sender, RoutedEventArgs e)
        {
            var d = new OpenFileDialog();
            d.Filter = "JSON|*.json";
            if (d.ShowDialog() == true)
            {
                var arr = Newtonsoft.Json.JsonConvert.DeserializeObject<SequenceStep[]>(File.ReadAllText(d.FileName));
                _steps.Clear();
                for (int i = 0; i < arr.Length; i++) { arr[i].Index = i + 1; _steps.Add(arr[i]); }
            }
        }

        private void BtnSaveSeq_Click(object sender, RoutedEventArgs e)
        {
            var d = new SaveFileDialog();
            d.Filter = "JSON|*.json";
            if (d.ShowDialog() == true)
            {
                File.WriteAllText(d.FileName, Newtonsoft.Json.JsonConvert.SerializeObject(_steps.ToArray(), Newtonsoft.Json.Formatting.Indented));
            }
        }

        private void Reindex()
        {
            for (int i = 0; i < _steps.Count; i++) _steps[i].Index = i + 1;
            GridSteps.Items.Refresh();
        }

        // ----- Settings/results -----
        private void BtnSaveSettings_Click(object sender, RoutedEventArgs e)
        {
            var d = new SaveFileDialog(); d.Filter = "JSON|*.json";
            if (d.ShowDialog() == true)
            {
                var cfg = new UserSettings();
                cfg.AcDc = GetComboText(CboAcDc, "AC");
                cfg.Function = GetComboText(CboFunction, "CURR");
                cfg.Setpoint = ParseDoubleOr(TxtSetpoint, 1.0);
                cfg.Pf = ParseDoubleOr(TxtPf, 1.0);
                cfg.Cf = ParseDoubleOr(TxtCf, 1.41);
                JsonFileStore.Save<UserSettings>(d.FileName, cfg);
                TxtStatus.Text = "Saved " + d.FileName;
            }
        }

        private void BtnLoadSettings_Click(object sender, RoutedEventArgs e)
        {
            var d = new OpenFileDialog(); d.Filter = "JSON|*.json";
            if (d.ShowDialog() == true)
            {
                var cfg = JsonFileStore.Load<UserSettings>(d.FileName);
                CboAcDc.SelectedItem = cfg.AcDc;
                CboFunction.SelectedItem = cfg.Function;
                TxtSetpoint.Text = cfg.Setpoint.ToString("F3");
                TxtPf.Text = cfg.Pf.ToString("F2");
                TxtCf.Text = cfg.Cf.ToString("F2");
                TxtStatus.Text = "Loaded " + d.FileName;
            }
        }

        private void BtnExportCsv_Click(object sender, RoutedEventArgs e)
        {
            var d = new SaveFileDialog(); d.Filter = "CSV|*.csv";
            if (d.ShowDialog() == true && _acq != null)
            {
                _acq.ExportCsv(d.FileName);
                TxtStatus.Text = "Exported " + d.FileName;
            }
        }

        private void BtnReport_Click(object sender, RoutedEventArgs e)
        {
            var d = new SaveFileDialog(); d.Filter = "PDF|*.pdf";
            if (d.ShowDialog() == true)
            {
                var kv = new System.Tuple<string, string>[] {
                    System.Tuple.Create("Vrms", ValVrms.Text),
                    System.Tuple.Create("Irms", ValIrms.Text),
                    System.Tuple.Create("Power", ValPower.Text),
                    System.Tuple.Create("PF", ValPf.Text),
                    System.Tuple.Create("Freq", ValFreq.Text)
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

        private void SetStatus(string s) { TxtStatus.Text = s; }
        private void Fail(Exception ex) { _logger.Log("ERR: " + ex.Message); MessageBox.Show(ex.Message, "Error"); }
    }
}
