// tbs1000c_core.cs
// Transport-agnostic core used by TBS1000C view.
// Provides: IScpiTransport, ScpiTransportAdapter (wraps your ScpiDeviceVisa),
// CommandScheduler (timeouts/retry/backoff/*OPC?/ALLev?), StateCache, Preamble, RingBuffer, Measurement DTOs.

using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HouseholdMS.Services
{
    // === VISA transport interface (no direct dependency on a specific visa class) ===
    public interface IScpiTransport
    {
        int TimeoutMs { get; set; }
        void Write(string command);
        string Query(string command);
        byte[] QueryBinary(string command, int timeoutOverrideMs = 0);
    }

    // === Adapter over your existing ScpiDeviceVisa ===
    // NOTE: Keep your original ScpiDeviceVisa.cs in the same namespace (HouseholdMS.Services).
    public sealed class ScpiTransportAdapter : IScpiTransport
    {
        private readonly ScpiDeviceVisa _visa;
        public ScpiTransportAdapter(ScpiDeviceVisa visa) { _visa = visa ?? throw new ArgumentNullException(nameof(visa)); }
        public int TimeoutMs { get => _visa.TimeoutMs; set => _visa.TimeoutMs = value; }
        public void Write(string command) => _visa.Write(command);
        public string Query(string command) => _visa.Query(command);
        public byte[] QueryBinary(string command, int timeoutOverrideMs = 0) => _visa.QueryBinary(command, timeoutOverrideMs);
    }

    // === Command Scheduler with retry/backoff/*OPC?/ALLev? drain ===
    public sealed class Tbs1000cCommandScheduler : IDisposable
    {
        private readonly IScpiTransport _io;
        private readonly BlockingCollection<ScheduledCommand> _queue = new BlockingCollection<ScheduledCommand>(new ConcurrentQueue<ScheduledCommand>());
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly object _ioLock = new object();
        private readonly Task _worker;

        public Tbs1000cCommandScheduler(IScpiTransport io)
        {
            _io = io ?? throw new ArgumentNullException(nameof(io));
            _worker = Task.Factory.StartNew(WorkerLoop, TaskCreationOptions.LongRunning);
        }

        public void EnqueueWrite(string cmd, int timeoutMs = 0, int retries = 1, bool waitOpc = false, Action<Exception> onError = null)
            => _queue.Add(ScheduledCommand.Write(cmd, timeoutMs, retries, waitOpc, onError));

        public void EnqueueQuery(string cmd, Action<string> onReply, int timeoutMs = 0, int retries = 1, bool waitOpc = false, Action<Exception> onError = null)
            => _queue.Add(ScheduledCommand.Query(cmd, onReply, timeoutMs, retries, waitOpc, onError));

        public Task<string> EnqueueQueryAsync(string cmd, int timeoutMs = 0, int retries = 1, bool waitOpc = false)
        {
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            _queue.Add(ScheduledCommand.Query(cmd, s => tcs.TrySetResult(s), timeoutMs, retries, waitOpc, ex => tcs.TrySetException(ex)));
            return tcs.Task;
        }

        public Task EnqueueWriteAsync(string cmd, int timeoutMs = 0, int retries = 1, bool waitOpc = false)
        {
            var tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            _queue.Add(ScheduledCommand.Write(cmd, timeoutMs, retries, waitOpc, ex => tcs.TrySetException(ex), () => tcs.TrySetResult(null)));
            return tcs.Task;
        }

        private void WorkerLoop()
        {
            foreach (var item in _queue.GetConsumingEnumerable(_cts.Token))
            {
                if (_cts.IsCancellationRequested) break;
                ExecuteOne(item);
            }
        }

        private void ExecuteOne(ScheduledCommand sc)
        {
            int attempt = 0;
            int backoff = 100;
            Exception last = null;

            while (attempt <= sc.Retries && !_cts.IsCancellationRequested)
            {
                try
                {
                    lock (_ioLock)
                    {
                        int old = _io.TimeoutMs;
                        if (sc.TimeoutMs > 0) _io.TimeoutMs = sc.TimeoutMs;

                        try
                        {
                            if (sc.IsQuery)
                            {
                                string res = _io.Query(sc.Command);
                                if (sc.WaitOpc) _io.Query("*OPC?");
                                DrainErrorQueue();
                                sc.OnReply?.Invoke(res);
                            }
                            else
                            {
                                _io.Write(sc.Command);
                                if (sc.WaitOpc) _io.Query("*OPC?");
                                DrainErrorQueue();
                                sc.OnCompleted?.Invoke();
                            }
                        }
                        finally { if (sc.TimeoutMs > 0) _io.TimeoutMs = old; }
                    }
                    last = null;
                    break;
                }
                catch (Exception ex)
                {
                    last = ex;
                    attempt++;
                    if (attempt > sc.Retries) break;
                    Thread.Sleep(backoff);
                    backoff = Math.Min(2000, backoff * 2);
                }
            }

            if (last != null) { try { sc.OnError?.Invoke(last); } catch { } }
        }

        private void DrainErrorQueue()
        {
            try
            {
                for (int i = 0; i < 4; i++)
                {
                    string ev = _io.Query("ALLev?");
                    if (string.IsNullOrWhiteSpace(ev)) break;
                    if (ev.StartsWith("0") || ev.IndexOf("No events", StringComparison.OrdinalIgnoreCase) >= 0) break;
                }
            }
            catch { /* ignore */ }
        }

        public void Dispose()
        {
            try { _queue.CompleteAdding(); } catch { }
            _cts.Cancel();
            try { _worker?.Wait(1000); } catch { }
        }

        private sealed class ScheduledCommand
        {
            public bool IsQuery;
            public string Command;
            public int TimeoutMs;
            public int Retries;
            public bool WaitOpc;
            public Action<string> OnReply;
            public Action OnCompleted;
            public Action<Exception> OnError;

            public static ScheduledCommand Write(string cmd, int to, int retries, bool waitOpc, Action<Exception> onError, Action onCompleted = null)
                => new ScheduledCommand { IsQuery = false, Command = cmd, TimeoutMs = to, Retries = retries, WaitOpc = waitOpc, OnError = onError, OnCompleted = onCompleted };

            public static ScheduledCommand Query(string cmd, Action<string> onReply, int to, int retries, bool waitOpc, Action<Exception> onError)
                => new ScheduledCommand { IsQuery = true, Command = cmd, TimeoutMs = to, Retries = retries, WaitOpc = waitOpc, OnReply = onReply, OnError = onError };
        }
    }

    // === State cache (avoid SCPI spam) ===
    public sealed class Tbs1000cStateCache
    {
        private readonly ConcurrentDictionary<string, string> _dict = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public bool ShouldSend(string key, string value)
        {
            if (key == null) return true;
            if (!_dict.TryGetValue(key, out var old)) { _dict[key] = value; return true; }
            if (!string.Equals(old, value, StringComparison.OrdinalIgnoreCase)) { _dict[key] = value; return true; }
            return false;
        }
        public void Clear() => _dict.Clear();
    }

    // === Waveform preamble/types ===
    public sealed class Tbs1000cWaveformPreamble
    {
        public double XINCR, XZERO, YMULT, YOFF, YZERO;
        public int NR_PT;

        public static Tbs1000cWaveformPreamble Parse(string pre)
        {
            var wp = new Tbs1000cWaveformPreamble();
            if (string.IsNullOrWhiteSpace(pre)) return wp;

            var parts = pre.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            double PD(string s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0.0;
            int PI(string s) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : 0;

            for (int i = 0; i < parts.Length; i++)
            {
                var t = parts[i].ToUpperInvariant();
                if (t.Contains("XINCR") && i + 1 < parts.Length) wp.XINCR = PD(parts[i + 1]);
                if (t.Contains("XZERO") && i + 1 < parts.Length) wp.XZERO = PD(parts[i + 1]);
                if (t.Contains("YMULT") && i + 1 < parts.Length) wp.YMULT = PD(parts[i + 1]);
                if (t.Contains("YZERO") && i + 1 < parts.Length) wp.YZERO = PD(parts[i + 1]);
                if (t.Contains("YOFF") && i + 1 < parts.Length) wp.YOFF = PD(parts[i + 1]);
                if (t.Contains("NR_PT") && i + 1 < parts.Length) wp.NR_PT = PI(parts[i + 1]);
            }

            // Fallback ordered guess (common Tek layouts)
            if (wp.XINCR == 0 && parts.Length >= 14)
            {
                try
                {
                    wp.NR_PT = PI(parts[5]);
                    wp.XINCR = PD(parts[8]);
                    wp.XZERO = PD(parts[9]);
                    wp.YMULT = PD(parts[11]);
                    wp.YZERO = PD(parts[12]);
                    wp.YOFF = PD(parts[13]);
                }
                catch { }
            }
            return wp;
        }
    }

    public sealed class Tbs1000cWaveformFrame
    {
        public DateTime TimestampUtc { get; set; }
        public string Source { get; set; } // CH1/CH2/MATH
        public double[] Time { get; set; }
        public double[] Volts { get; set; }
        public Tbs1000cMeasurementValues Measurements { get; set; }
    }

    public sealed class Tbs1000cWaveformRingBuffer
    {
        private readonly int _capacity;
        private readonly System.Collections.Generic.Queue<Tbs1000cWaveformFrame> _q;
        private readonly object _gate = new object();

        public Tbs1000cWaveformRingBuffer(int capacity = 64)
        { _capacity = Math.Max(4, capacity); _q = new System.Collections.Generic.Queue<Tbs1000cWaveformFrame>(_capacity); }

        public void Add(Tbs1000cWaveformFrame f)
        {
            lock (_gate) { if (_q.Count >= _capacity) _q.Dequeue(); _q.Enqueue(f); }
        }

        public Tbs1000cWaveformFrame[] Snapshot()
        { lock (_gate) { return _q.ToArray(); } }
    }

    // === Measurements DTO ===
    public class Tbs1000cMeasurementValues
    {
        public double? Vpp, Vavg, Vrms, Vmax, Vmin, Vpos, Vneg,
                       Freq, Period, Duty, PwPos, PwNeg,
                       Rise, Fall, RipplePct, Crest,
                       OvershootPct, UndershootPct, ZeroCrossRate;
    }
}
