using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// NOTE: VisaComTransport below uses TekVISA/NI-VISA COM interop.
using Ivi.Visa.Interop; // for VisaComTransport

namespace HouseholdMS.Services
{
    // --- Transport abstraction ---
    public interface IScpiTransport
    {
        int TimeoutMs { get; set; }
        void Write(string command);
        string Query(string command);
        byte[] QueryBinary(string command, int timeoutOverrideMs = 0);
    }

    /// <summary>
    /// Adapter to wrap any existing device with delegates.
    /// </summary>
    public sealed class DelegateScpiTransport : IScpiTransport
    {
        private readonly Func<int> _getTimeout;
        private readonly Action<int> _setTimeout;
        private readonly Action<string> _write;
        private readonly Func<string, string> _query;
        private readonly Func<string, int, byte[]> _queryBinary;

        public DelegateScpiTransport(
            Func<int> getTimeout,
            Action<int> setTimeout,
            Action<string> write,
            Func<string, string> query,
            Func<string, int, byte[]> queryBinary)
        {
            _getTimeout = getTimeout ?? throw new ArgumentNullException(nameof(getTimeout));
            _setTimeout = setTimeout ?? throw new ArgumentNullException(nameof(setTimeout));
            _write = write ?? throw new ArgumentNullException(nameof(write));
            _query = query ?? throw new ArgumentNullException(nameof(query));
            _queryBinary = queryBinary ?? throw new ArgumentNullException(nameof(queryBinary));
        }

        public int TimeoutMs { get => _getTimeout(); set => _setTimeout(value); }
        public void Write(string command) => _write(command);
        public string Query(string command) => _query(command);
        public byte[] QueryBinary(string command, int timeoutOverrideMs = 0) => _queryBinary(command, timeoutOverrideMs);
    }

    /// <summary>
    /// VISA-COM transport. Use Open(resource) or OpenFirstTekUsb(out resource).
    /// Thread-safe: calls are serialized via a private lock.
    /// </summary>
    public sealed class VisaComTransport : IScpiTransport, IDisposable
    {
        private readonly ResourceManager _rm;
        private readonly IMessage _session;
        private readonly FormattedIO488 _io;
        private readonly object _lock = new object();
        private bool _disposed;
        private int _timeoutMs;

        private VisaComTransport(ResourceManager rm, IMessage session, FormattedIO488 io, int timeoutMs)
        {
            _rm = rm;
            _session = session;
            _io = io;
            _timeoutMs = timeoutMs;
        }

        public static VisaComTransport Open(string resourceAddress, int timeoutMs = 3000)
        {
            if (string.IsNullOrWhiteSpace(resourceAddress))
                throw new ArgumentNullException(nameof(resourceAddress));

            var rm = new ResourceManager();
            var session = (IMessage)rm.Open(resourceAddress, AccessMode.NO_LOCK, timeoutMs, string.Empty);
            session.Timeout = timeoutMs;
            session.TerminationCharacterEnabled = true;
            session.TerminationCharacter = 10; // '\n'
            TryClear(session);

            var io = new FormattedIO488 { IO = session };
            return new VisaComTransport(rm, session, io, timeoutMs);
        }

        /// <summary>
        /// Finds the first USB instrument (prefers Tektronix VID 0x0699) and opens it.
        /// </summary>
        public static VisaComTransport OpenFirstTekUsb(out string resourceAddress, int timeoutMs = 3000)
        {
            var rm = new ResourceManager();
            string[] list = null;

            try { list = rm.FindRsrc("USB?*::0x0699?*::INSTR"); } catch { }
            if (list == null || list.Length == 0)
            {
                try { list = rm.FindRsrc("USB?*::INSTR"); } catch { }
            }
            if (list == null || list.Length == 0)
            {
                try { list = rm.FindRsrc("?*INSTR"); } catch { }
            }
            if (list == null || list.Length == 0)
                throw new InvalidOperationException("No VISA instruments found.");

            var pick =
                list.FirstOrDefault(s => s.IndexOf("0x0699", StringComparison.OrdinalIgnoreCase) >= 0) // Tek VID
                ?? list.FirstOrDefault(s => s.IndexOf("TEK", StringComparison.OrdinalIgnoreCase) >= 0)
                ?? list[0];

            resourceAddress = pick;

            var session = (IMessage)rm.Open(pick, AccessMode.NO_LOCK, timeoutMs, string.Empty);
            session.Timeout = timeoutMs;
            session.TerminationCharacterEnabled = true;
            session.TerminationCharacter = 10; // '\n'
            TryClear(session);

            var io = new FormattedIO488 { IO = session };
            return new VisaComTransport(rm, session, io, timeoutMs);
        }

        public int TimeoutMs
        {
            get => _timeoutMs;
            set
            {
                _timeoutMs = value;
                try { _session.Timeout = value; } catch { /* ignore */ }
            }
        }

        public void Write(string command)
        {
            EnsureNotDisposed();
            if (string.IsNullOrEmpty(command)) return;
            if (!command.EndsWith("\n")) command += "\n";

            lock (_lock)
            {
                try
                {
                    _io.WriteString(command);
                }
                catch (System.Runtime.InteropServices.COMException)
                {
                    TryClear(_session);
                    throw;
                }
            }
        }

        public string Query(string command)
        {
            EnsureNotDisposed();
            if (string.IsNullOrEmpty(command)) return string.Empty;
            if (!command.EndsWith("\n")) command += "\n";

            lock (_lock)
            {
                try
                {
                    _io.WriteString(command);
                    // ReadString returns up to termination; instrument already appends LF.
                    return _io.ReadString();
                }
                catch (System.Runtime.InteropServices.COMException)
                {
                    TryClear(_session);
                    throw;
                }
            }
        }

        public byte[] QueryBinary(string command, int timeoutOverrideMs = 0)
        {
            EnsureNotDisposed();
            if (string.IsNullOrEmpty(command)) return Array.Empty<byte>();
            if (!command.EndsWith("\n")) command += "\n";

            lock (_lock)
            {
                int old = _session.Timeout;
                if (timeoutOverrideMs > 0) _session.Timeout = timeoutOverrideMs;

                try
                {
                    _io.WriteString(command);
                    // Read an IEEE 488.2 definite-length block of 8-bit data.
                    object block = _io.ReadIEEEBlock(IEEEBinaryType.BinaryType_UI1);
                    if (block is Array arr)
                    {
                        int len = arr.Length;
                        var bytes = new byte[len];
                        for (int i = 0; i < len; i++) bytes[i] = Convert.ToByte(arr.GetValue(i));
                        return bytes;
                    }
                    return Array.Empty<byte>();
                }
                catch (System.Runtime.InteropServices.COMException)
                {
                    TryClear(_session);
                    throw;
                }
                finally
                {
                    if (timeoutOverrideMs > 0) _session.Timeout = old;
                }
            }
        }

        private static void TryClear(IMessage session)
        {
            try { session?.Clear(); } catch { }
        }

        private void EnsureNotDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(VisaComTransport));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { if (_io != null) { try { _io.IO = null; } catch { } } } catch { }
            try { (_session as IDisposable)?.Dispose(); } catch { }
            try { (_rm as IDisposable)?.Dispose(); } catch { }

            GC.SuppressFinalize(this);
        }
    }

    // --- Command scheduler ---
    /// <summary>
    /// Serializes SCPI I/O with retry, optional *OPC? wait, and light error-drain.
    /// Bounded queue prevents UI stalls if callers flood commands.
    /// </summary>
    public sealed class Tbs1000cCommandScheduler : IDisposable
    {
        private readonly IScpiTransport _io;
        private readonly BlockingCollection<ScheduledCommand> _queue;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly object _ioLock = new object();
        private readonly Task _worker;
        private volatile bool _disposed;

        public Tbs1000cCommandScheduler(IScpiTransport io, int boundedCapacity = 512)
        {
            if (boundedCapacity < 64) boundedCapacity = 64;
            _io = io ?? throw new ArgumentNullException(nameof(io));
            _queue = new BlockingCollection<ScheduledCommand>(new ConcurrentQueue<ScheduledCommand>(), boundedCapacity);
            _worker = Task.Factory.StartNew(WorkerLoop, TaskCreationOptions.LongRunning);
        }

        public void EnqueueWrite(string cmd, int timeoutMs = 0, int retries = 1, bool waitOpc = false, Action<Exception> onError = null)
            => TryAdd(ScheduledCommand.Write(cmd, timeoutMs, retries, waitOpc, onError));

        public void EnqueueQuery(string cmd, Action<string> onReply, int timeoutMs = 0, int retries = 1, bool waitOpc = false, Action<Exception> onError = null)
            => TryAdd(ScheduledCommand.Query(cmd, onReply, timeoutMs, retries, waitOpc, onError));

        public Task<string> EnqueueQueryAsync(string cmd, int timeoutMs = 0, int retries = 1, bool waitOpc = false)
        {
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

            if (_disposed || _queue.IsAddingCompleted)
            {
                tcs.TrySetException(new ObjectDisposedException(nameof(Tbs1000cCommandScheduler)));
                return tcs.Task;
            }

            bool ok = TryAdd(ScheduledCommand.Query(cmd, s => tcs.TrySetResult(s), timeoutMs, retries, waitOpc,
                                                    ex => tcs.TrySetException(ex)));
            if (!ok)
                tcs.TrySetException(new ObjectDisposedException(nameof(Tbs1000cCommandScheduler)));

            return tcs.Task;
        }

        public Task EnqueueWriteAsync(string cmd, int timeoutMs = 0, int retries = 1, bool waitOpc = false)
        {
            var tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);

            if (_disposed || _queue.IsAddingCompleted)
            {
                tcs.TrySetException(new ObjectDisposedException(nameof(Tbs1000cCommandScheduler)));
                return tcs.Task;
            }

            bool ok = TryAdd(ScheduledCommand.Write(cmd, timeoutMs, retries, waitOpc,
                                                    ex => tcs.TrySetException(ex),
                                                    () => tcs.TrySetResult(null)));
            if (!ok)
                tcs.TrySetException(new ObjectDisposedException(nameof(Tbs1000cCommandScheduler)));

            return tcs.Task;
        }

        public Task<byte[]> EnqueueQueryBinaryAsync(string cmd, int timeoutMs = 0, int retries = 1, bool waitOpc = false)
        {
            var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);

            if (_disposed || _queue.IsAddingCompleted)
            {
                tcs.TrySetException(new ObjectDisposedException(nameof(Tbs1000cCommandScheduler)));
                return tcs.Task;
            }

            bool ok = TryAdd(ScheduledCommand.QueryBinary(cmd, bytes => tcs.TrySetResult(bytes), timeoutMs, retries, waitOpc,
                                                          ex => tcs.TrySetException(ex)));
            if (!ok)
                tcs.TrySetException(new ObjectDisposedException(nameof(Tbs1000cCommandScheduler)));

            return tcs.Task;
        }

        // Return bool so callers can handle failure if queue is closed/disposed
        private bool TryAdd(ScheduledCommand sc)
        {
            if (_disposed) return false;
            try
            {
                if (!_queue.TryAdd(sc, 50))
                {
                    var snapshot = _queue.ToArray();
                    var olderWrite = snapshot.FirstOrDefault(x => !x.IsQuery && !x.IsBinaryQuery);
                    if (olderWrite != null)
                    {
                        var drained = new List<ScheduledCommand>();
                        while (_queue.TryTake(out var item))
                        {
                            if (!ReferenceEquals(item, olderWrite))
                                drained.Add(item);
                            else
                                break;
                        }
                        foreach (var d in drained)
                            _queue.TryAdd(d);
                    }
                    if (!_queue.TryAdd(sc, 50))
                        return false;
                }
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private void WorkerLoop()
        {
            try
            {
                foreach (var item in _queue.GetConsumingEnumerable(_cts.Token))
                {
                    if (_cts.IsCancellationRequested) break;
                    ExecuteOne(item);
                }
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            catch (Exception) { /* swallow to avoid crashing process */ }
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
                            else if (sc.IsBinaryQuery)
                            {
                                byte[] res = _io.QueryBinary(sc.Command, sc.TimeoutMs);
                                if (sc.WaitOpc) _io.Query("*OPC?");
                                DrainErrorQueue();
                                sc.OnReplyBytes?.Invoke(res ?? Array.Empty<byte>());
                            }
                            else
                            {
                                _io.Write(sc.Command);
                                if (sc.WaitOpc) _io.Query("*OPC?");
                                DrainErrorQueue();
                                sc.OnCompleted?.Invoke();
                            }
                        }
                        finally
                        {
                            if (sc.TimeoutMs > 0) _io.TimeoutMs = old;
                        }
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

            if (last != null)
            {
                try { sc.OnError?.Invoke(last); } catch { }
            }
        }

        private void DrainErrorQueue()
        {
            try
            {
                for (int i = 0; i < 2; i++)
                {
                    var ev = _io.Query("ALLEV?");
                    if (string.IsNullOrWhiteSpace(ev)) break;
                    if (ev.TrimStart().StartsWith("0", StringComparison.Ordinal)) break;
                    if (ev.IndexOf("No events", StringComparison.OrdinalIgnoreCase) >= 0) break;
                }
            }
            catch { /* ignore */ }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { _queue.CompleteAdding(); } catch { }
            try { _cts.Cancel(); } catch { }

            // Fail any pending items so awaiting tasks don't hang.
            try
            {
                while (_queue.TryTake(out var pending))
                {
                    try { pending.OnError?.Invoke(new ObjectDisposedException(nameof(Tbs1000cCommandScheduler))); } catch { }
                }
            }
            catch { }

            try { _worker?.Wait(500); } catch { }
            try { _cts.Dispose(); } catch { }
        }

        private sealed class ScheduledCommand
        {
            public bool IsQuery;
            public bool IsBinaryQuery;
            public string Command;
            public int TimeoutMs;
            public int Retries;
            public bool WaitOpc;

            public Action<string> OnReply;
            public Action<byte[]> OnReplyBytes;
            public Action OnCompleted;
            public Action<Exception> OnError;

            public static ScheduledCommand Write(string cmd, int to, int retries, bool waitOpc, Action<Exception> onError, Action onCompleted = null)
                => new ScheduledCommand { IsQuery = false, IsBinaryQuery = false, Command = cmd, TimeoutMs = to, Retries = retries, WaitOpc = waitOpc, OnError = onError, OnCompleted = onCompleted };

            public static ScheduledCommand Query(string cmd, Action<string> onReply, int to, int retries, bool waitOpc, Action<Exception> onError)
                => new ScheduledCommand { IsQuery = true, IsBinaryQuery = false, Command = cmd, TimeoutMs = to, Retries = retries, WaitOpc = waitOpc, OnReply = onReply, OnError = onError };

            public static ScheduledCommand QueryBinary(string cmd, Action<byte[]> onReplyBytes, int to, int retries, bool waitOpc, Action<Exception> onError)
                => new ScheduledCommand { IsQuery = false, IsBinaryQuery = true, Command = cmd, TimeoutMs = to, Retries = retries, WaitOpc = waitOpc, OnReplyBytes = onReplyBytes, OnError = onError };
        }
    }

    // --- Lightweight state cache for idempotent set commands ---
    public sealed class Tbs1000cStateCache
    {
        private readonly ConcurrentDictionary<string, string> _dict =
            new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public bool ShouldSend(string key, string value)
        {
            if (key == null) return true;
            if (!_dict.TryGetValue(key, out var old)) { _dict[key] = value; return true; }
            if (!string.Equals(old, value, StringComparison.OrdinalIgnoreCase)) { _dict[key] = value; return true; }
            return false;
        }

        public void Clear() => _dict.Clear();
    }

    // --- Waveform metadata and frames ---
    public sealed class Tbs1000cWaveformPreamble
    {
        public double XINCR, XZERO, YMULT, YOFF, YZERO;
        public int NR_PT;
    }

    public sealed class Tbs1000cWaveformFrame
    {
        public DateTime TimestampUtc { get; set; }
        public string Source { get; set; }
        public double[] Time { get; set; }
        public double[] Volts { get; set; }
        public Tbs1000cMeasurementValues Measurements { get; set; }
    }

    public sealed class Tbs1000cWaveformRingBuffer
    {
        private readonly int _capacity;
        private readonly Queue<Tbs1000cWaveformFrame> _q;
        private readonly object _gate = new object();

        public Tbs1000cWaveformRingBuffer(int capacity = 64)
        { _capacity = Math.Max(4, capacity); _q = new Queue<Tbs1000cWaveformFrame>(_capacity); }

        public void Add(Tbs1000cWaveformFrame f)
        {
            lock (_gate)
            {
                if (_q.Count >= _capacity) _q.Dequeue();
                _q.Enqueue(f);
            }
        }

        public Tbs1000cWaveformFrame[] Snapshot()
        {
            lock (_gate) { return _q.ToArray(); }
        }
    }

    public class Tbs1000cMeasurementValues
    {
        public double? Vpp, Vavg, Vrms, Vmax, Vmin, Vpos, Vneg,
                       Freq, Period, Duty, PwPos, PwNeg,
                       Rise, Fall, RipplePct, Crest,
                       OvershootPct, UndershootPct, ZeroCrossRate;
    }
}
