using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Threading;
using HouseholdMS.Drivers;
using HouseholdMS.Models;
using HouseholdMS.Services;

namespace HouseholdMS.View.EqTesting
{
    public partial class It8615MiniPanelControl : UserControl
    {
        private readonly VisaSession _visa = new VisaSession();
        private readonly CommandLogger _logger = new CommandLogger(); // optional
        private ItechIt8615 _it;
        private AcquisitionService _acq;

        private CancellationTokenSource _lifecycleCts;
        private bool _autoConnecting;
        private readonly DispatcherTimer _initialKickTimer;

        // Ensure only ONE control holds the loader at a time
        private static readonly SemaphoreSlim _deviceGate = new SemaphoreSlim(1, 1);
        private bool _hasGate;

        public It8615MiniPanelControl()
        {
            InitializeComponent();

            _initialKickTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
            _initialKickTimer.Tick += (s, e) =>
            {
                _initialKickTimer.Stop();
                StartAutoLifecycle(); // discover + autoconnect
            };

            Loaded += (s, e) => _initialKickTimer.Start();
            Unloaded += async (s, e) => await StopLifecycleAsync().ConfigureAwait(false);
            IsVisibleChanged += async (s, e) =>
            {
                if (!IsVisible) await StopLifecycleAsync().ConfigureAwait(false);
                else StartAutoLifecycle();
            };
        }

        // ====== Auto lifecycle ======
        private void StartAutoLifecycle()
        {
            if (_autoConnecting || _it != null) return;

            if (_lifecycleCts != null)
            {
                try { _lifecycleCts.Cancel(); } catch { }
                _lifecycleCts.Dispose();
            }

            _lifecycleCts = new CancellationTokenSource();
            var ct = _lifecycleCts.Token;

            _autoConnecting = true;
            _ = Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    SetStatus("Waiting for loader reservation…");
                    try
                    {
                        await _deviceGate.WaitAsync(ct).ConfigureAwait(true);
                        _hasGate = true;
                    }
                    catch (OperationCanceledException) { return; }

                    SetStatus("Auto-discovering…");
                    var list = await DiscoverResourcesAsync().ConfigureAwait(true);
                    if (list.Count == 0) { SetStatus("No VISA resources."); return; }

                    var chosen = ChooseBestCandidate(list) ?? await ProbeForItechAsync(list, ct).ConfigureAwait(true);
                    if (chosen == null || ct.IsCancellationRequested)
                    {
                        if (!ct.IsCancellationRequested) SetStatus("IT8615 not found.");
                        return;
                    }

                    await ConnectAsync(chosen, ct).ConfigureAwait(true);
                }
                finally
                {
                    _autoConnecting = false;
                }
            }, DispatcherPriority.Background);
        }

        private async Task StopLifecycleAsync()
        {
            try
            {
                if (_lifecycleCts != null)
                {
                    try { _lifecycleCts.Cancel(); } catch { }
                }

                await SafeShutdownAsync().ConfigureAwait(true);
                _visa.Close();
                SetStatus("Stopped.");
            }
            finally
            {
                if (_lifecycleCts != null)
                {
                    _lifecycleCts.Dispose();
                    _lifecycleCts = null;
                }
                if (_hasGate)
                {
                    _deviceGate.Release();
                    _hasGate = false;
                }
            }
        }

        // ====== Core ops ======
        private async Task<bool> ConnectAsync(string resource, CancellationToken ct)
        {
            try
            {
                if (ct.IsCancellationRequested) return false;

                _visa.TimeoutMs = 2000;
                _visa.Retries = 2;
                _visa.Open(resource, _logger);

                var probe = new ItechIt8615(_visa, _logger);
                var idn = await probe.IdentifyAsync().ConfigureAwait(true);

                if (string.IsNullOrWhiteSpace(idn) || idn.IndexOf("ITECH", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    _visa.Close();
                    SetStatus("Wrong instrument.");
                    return false;
                }

                _it = probe;

                await _it.ToRemoteAsync().ConfigureAwait(true);
                await _it.DrainErrorQueueAsync().ConfigureAwait(true);

                _acq = new AcquisitionService(_it, _logger);
                _acq.OnReading += r => Dispatcher.Invoke(() => UpdateReadings(r));
                _acq.Start(hz: 5);

                SetStatus("Connected.");
                return true;
            }
            catch
            {
                SetStatus("Auto-connect failed.");
                try { _visa.Close(); } catch { }
                _it = null;
                _acq = null;
                if (_hasGate) { _deviceGate.Release(); _hasGate = false; }
                return false;
            }
        }

        private async Task SafeShutdownAsync()
        {
            try
            {
                if (_acq != null) _acq.Stop();
                await Task.Delay(120).ConfigureAwait(true);
                if (_it != null)
                {
                    // (no input toggling here)
                    try { await _it.ToLocalAsync().ConfigureAwait(true); } catch { }
                }
            }
            catch { }
            finally
            {
                _acq = null;
                _it = null;
            }
        }

        // ====== Discovery helpers ======
        private Task<System.Collections.Generic.List<string>> DiscoverResourcesAsync()
        {
            return Task.Run(() =>
            {
                try
                {
                    var list = _visa.DiscoverResources(new[] { "USB?*INSTR", "TCPIP?*INSTR", "GPIB?*INSTR" })
                                    .Distinct()
                                    .ToList();
                    return list;
                }
                catch
                {
                    return new System.Collections.Generic.List<string>();
                }
            });
        }

        private string ChooseBestCandidate(System.Collections.Generic.IEnumerable<string> resources)
        {
            var exact = resources.FirstOrDefault(r =>
                r.IndexOf("0x2EC7", StringComparison.OrdinalIgnoreCase) >= 0 &&
                r.IndexOf("0x8615", StringComparison.OrdinalIgnoreCase) >= 0);
            if (exact != null) return exact;

            var usbItech = resources.FirstOrDefault(r =>
                r.StartsWith("USB", StringComparison.OrdinalIgnoreCase) &&
               (r.IndexOf("861", StringComparison.OrdinalIgnoreCase) >= 0 ||
                r.IndexOf("ITECH", StringComparison.OrdinalIgnoreCase) >= 0));
            if (usbItech != null) return usbItech;

            var anyUsb = resources.FirstOrDefault(r => r.StartsWith("USB", StringComparison.OrdinalIgnoreCase));
            if (anyUsb != null) return anyUsb;

            return resources.FirstOrDefault();
        }

        private async Task<string> ProbeForItechAsync(System.Collections.Generic.IEnumerable<string> resources, CancellationToken ct)
        {
            foreach (var res in resources)
            {
                if (ct.IsCancellationRequested) return null;

                var tmp = new VisaSession();
                try
                {
                    tmp.TimeoutMs = 1200;
                    tmp.Retries = 1;
                    tmp.Open(res, _logger);

                    var dev = new ItechIt8615(tmp, _logger);
                    var idn = await dev.IdentifyAsync().ConfigureAwait(true);
                    if (!string.IsNullOrWhiteSpace(idn) &&
                        idn.IndexOf("ITECH", StringComparison.OrdinalIgnoreCase) >= 0 &&
                        idn.IndexOf("IT86", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        try { tmp.Close(); } catch { }
                        return res;
                    }
                }
                catch { }
                finally
                {
                    try { tmp.Close(); } catch { }
                }
            }
            return null;
        }

        // ====== UI update helpers (only measured outputs) ======
        private void UpdateReadings(InstrumentReading r)
        {
            TxtVrms.Text = r.Vrms.ToString("F3") + " V";
            TxtIrms.Text = r.Irms.ToString("F3") + " A";
            TxtPower.Text = r.Power.ToString("F3") + " W";
            TxtPf.Text = r.Pf.ToString("F3");
            TxtFreq.Text = r.Freq.ToString("F2") + " Hz";
            TxtCf.Text = r.CrestFactor.ToString("F2");
        }

        private void SetStatus(string s) => TxtStatus.Text = s;
    }
}
