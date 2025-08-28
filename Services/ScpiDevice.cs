using System;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Globalization;

namespace HouseholdMS.Services
{
    public enum ConnectionState { Disconnected, Connecting, Connected, Faulted }

    public interface IScpiDevice : IDisposable
    {
        // Connection & state
        void Connect();
        void Disconnect();
        bool IsConnected { get; }
        ConnectionState State { get; }
        event EventHandler<ConnectionState> ConnectionStateChanged;

        // Core I/O (may throw; high-level wrappers below avoid it)
        string Query(string command);
        Task<string> QueryAsync(string command);
        void Write(string command);
        Task WriteAsync(string command);

        // Basics (safe: never throw on I/O; return null/empty)
        string ReadMeasurement();
        Task<string> ReadMeasurementAsync();
        string ReadDeviceID();
        Task<string> ReadDeviceIDAsync();

        // Mode/Function (safe)
        void SetFunction(string function);
        Task SetFunctionAsync(string function);

        // UX helpers (safe)
        void SetRate(char speedF_M_L);         // 'F','M','L'
        void SetAveraging(bool on);            // CALC:FUNC AVER / CALC:STAT OFF
        void SetAutoRange(bool on);            // AUTO 1|0
        void SetRange(string rangeToken);      // RANGE <token>
        string GetFunction();                  // FUNC? (safe)
        AveragingStats TryQueryAveragingAll(); // May return null if unsupported
        void SetLineEnding(string newEnding);
    }

    public sealed class AveragingStats
    {
        public double Min;
        public double Max;
        public double Avg;
        public int Count;
        public override string ToString()
            => string.Format(CultureInfo.InvariantCulture, "avg={0:G6}, min={1:G6}, max={2:G6}, n={3}", Avg, Min, Max, Count);
    }

    public class ScpiDevice : IScpiDevice
    {
        protected SerialPort _port;
        protected readonly object _ioLock = new object();

        public string PortName { get; }
        public int BaudRate { get; }
        public Parity Parity { get; }
        public int DataBits { get; }
        public StopBits StopBits { get; }
        public Handshake Handshake { get; }
        public string LineEnding { get; private set; } = "\n";

        public bool IsConnected => _port != null && _port.IsOpen;

        private volatile ConnectionState _state = ConnectionState.Disconnected;
        public ConnectionState State => _state;
        public event EventHandler<ConnectionState> ConnectionStateChanged;

        private void SetState(ConnectionState s)
        {
            if (_state == s) return;
            _state = s;
            try { ConnectionStateChanged?.Invoke(this, s); } catch { /* swallow */ }
        }

        public ScpiDevice(
            string portName,
            int baudRate = 9600,
            Parity parity = Parity.None,
            int dataBits = 8,
            StopBits stopBits = StopBits.One,
            Handshake handshake = Handshake.None,
            string lineEnding = "\n")
        {
            if (string.IsNullOrWhiteSpace(portName))
                throw new ArgumentNullException(nameof(portName));

            PortName = portName;
            BaudRate = baudRate;
            Parity = parity;
            DataBits = dataBits;
            StopBits = stopBits;
            Handshake = handshake;
            LineEnding = lineEnding;
        }

        public virtual void Connect()
        {
            Disconnect();

            try
            {
                SetState(ConnectionState.Connecting);

                _port?.Dispose();

                _port = new SerialPort(PortName, BaudRate, Parity, DataBits)
                {
                    StopBits = StopBits,
                    Handshake = Handshake,
                    ReadTimeout = 2000,
                    WriteTimeout = 2000,
                    NewLine = LineEnding,
                    DtrEnable = true,
                    RtsEnable = false,
                    Encoding = Encoding.ASCII
                };

                _port.Open();

                try
                {
                    // Soft sanity check — will only warn if weird
                    EnsureDeviceResponds();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Warning: device may not be responding yet. " + ex.Message);
                }

                SetState(ConnectionState.Connected);
            }
            catch (Exception ex)
            {
                SetState(ConnectionState.Faulted);
                // Bubble only as InvalidOperation — caught by your Connect_Click handler
                throw new InvalidOperationException($"Failed to open port {PortName}: {ex.Message}", ex);
            }
        }

        public virtual void Disconnect()
        {
            try
            {
                if (_port != null && _port.IsOpen)
                    _port.Close();
            }
            catch { }
            finally
            {
                SetState(ConnectionState.Disconnected);
            }
        }

        // -------- Low-level I/O (can throw) --------
        public virtual void Write(string command)
        {
            Console.WriteLine("Writing SCPI: " + command);
            if (!IsConnected)
                throw new InvalidOperationException("Device not connected.");

            lock (_ioLock)
            {
                try
                {
                    _port.DiscardInBuffer();
                    _port.DiscardOutBuffer();
                    _port.Write(command + LineEnding);
                }
                catch (Exception ex)
                {
                    SetState(ConnectionState.Faulted);
                    throw new IOException($"Write failed: {ex.Message}", ex);
                }
            }
        }

        public Task WriteAsync(string command) => Task.Run(() => Write(command));

        public virtual string Query(string command)
        {
            if (!IsConnected)
                throw new InvalidOperationException("Device not connected.");

            lock (_ioLock)
            {
                try
                {
                    _port.DiscardInBuffer();
                    _port.DiscardOutBuffer();
                    _port.Write(command + LineEnding);
                    return _port.ReadLine();
                }
                catch (TimeoutException ex)
                {
                    SetState(ConnectionState.Faulted);
                    throw new TimeoutException($"Timeout on command '{command}'", ex);
                }
                catch (Exception ex)
                {
                    SetState(ConnectionState.Faulted);
                    throw new IOException($"Query failed on command '{command}': {ex.Message}", ex);
                }
            }
        }

        public Task<string> QueryAsync(string command) => Task.Run(() => Query(command));

        // -------- Safe wrappers (never throw) --------
        private string QuerySafe(string command)
        {
            try
            {
                return Query(command);
            }
            catch (TimeoutException)
            {
                string original = LineEnding;
                try
                {
                    // Some devices expect CRLF
                    SetLineEnding("\r\n");
                    return Query(command);
                }
                catch
                {
                    SetState(ConnectionState.Faulted);
                    return null;   // swallow for UI safety
                }
                finally
                {
                    SetLineEnding(original);
                }
            }
            catch
            {
                SetState(ConnectionState.Faulted);
                return null;       // swallow for UI safety
            }
        }

        private string QueryFirstAvailable(params string[] queries)
        {
            foreach (var cmd in queries)
            {
                var r = QuerySafe(cmd);
                if (!string.IsNullOrWhiteSpace(r))
                    return r;
            }
            return null; // safe: caller decides what to do
        }

        private void EnsureDeviceResponds()
        {
            var id = ReadDeviceID(); // safe call
            if (string.IsNullOrWhiteSpace(id))
                throw new IOException("Device returned empty ID.");
            if (!id.ToLowerInvariant().Contains("mp730889"))
                throw new IOException("Unexpected device ID: " + id);
        }

        // ---------- High-level ops (safe) ----------

        public virtual string ReadMeasurement() => QuerySafe("MEAS?");

        public virtual Task<string> ReadMeasurementAsync() => Task.Run(() => ReadMeasurement());

        public virtual string ReadDeviceID() => QuerySafe("*IDN?") ?? string.Empty;

        public virtual Task<string> ReadDeviceIDAsync() => Task.Run(() => ReadDeviceID());

        // --------- Function setting (tolerant, safe) ---------
        public virtual void SetFunction(string function)
        {
            if (string.IsNullOrWhiteSpace(function))
                return;

            string token = NormalizeTokenNoSense(function); // e.g., "VOLT:AC"

            string[] tryWrites =
            {
                $"FUNC \"{token}\"",
                $"CONF:{TokenToConfNoSense(token)}"
            };

            lock (_ioLock)
            {
                try { Write("SYST:REM"); } catch { /* ignore */ }

                bool anyWriteSucceeded = false;

                foreach (var cmd in tryWrites)
                {
                    try
                    {
                        Write(cmd);
                        anyWriteSucceeded = true;

                        if (token.StartsWith("TEMP", StringComparison.OrdinalIgnoreCase))
                        {
                            string[] tempCfg =
                            {
                                "CONF:TEMP:RTD PT100",
                                "TEMP:RTD:TYPE PT100",
                                "TEMP:UNIT C"
                            };
                            foreach (var t in tempCfg) { try { Write(t); } catch { } }
                        }

                        // fast confirmation (short timeout, few attempts)
                        if (ConfirmFunctionWithRetry(token, attempts: 2, perAttemptTimeoutMs: 250))
                            return; // confirmed OK
                    }
                    catch
                    {
                        // try next form
                    }
                }

                // Couldn’t confirm, but at least one write went out — don’t crash the app.
                if (anyWriteSucceeded)
                {
                    Console.WriteLine($"[WARN] Could not confirm FUNC '{token}' via readback.");
                    return;
                }

                Console.WriteLine($"[ERR] Could not send function '{token}'.");
            }
        }

        public Task SetFunctionAsync(string function) => Task.Run(() => SetFunction(function));

        public string GetFunction() => QueryFirstAvailable("FUNC?", "CONF?") ?? string.Empty;

        private bool ConfirmFunctionWithRetry(string token, int attempts = 2, int perAttemptTimeoutMs = 250)
        {
            for (int i = 0; i < attempts; i++)
            {
                var r1 = QueryFast("FUNC?", perAttemptTimeoutMs);
                if (ReadbackMatches(r1, token)) return true;

                var r2 = QueryFast("CONF?", perAttemptTimeoutMs);
                if (ReadbackMatches(r2, token)) return true;

                Thread.Sleep(60 + i * 40); // short backoff
            }
            return false;
        }

        private string QueryFast(string command, int timeoutMs)
        {
            if (!IsConnected) return null;

            lock (_ioLock)
            {
                int old = _port.ReadTimeout;
                try
                {
                    _port.ReadTimeout = timeoutMs;
                    _port.DiscardInBuffer();
                    _port.DiscardOutBuffer();
                    _port.Write(command + LineEnding);
                    return _port.ReadLine();
                }
                catch { return null; }
                finally { _port.ReadTimeout = old; }
            }
        }

        private static bool ReadbackMatches(string resp, string token)
        {
            if (string.IsNullOrWhiteSpace(resp)) return false;

            string norm = NormalizeReadback(resp);
            token = token.ToUpperInvariant();

            if (norm.Contains(token)) return true;
            foreach (var alias in AliasesFor(token))
                if (norm.Contains(alias)) return true;

            return false;
        }

        private static string NormalizeReadback(string s)
        {
            s = s.Trim().ToUpperInvariant();
            s = s.Replace("\"", "").Replace(" ", "");
            s = s.Replace(',', ':');
            while (s.Contains("::")) s = s.Replace("::", ":");
            return s;
        }

        private static string[] AliasesFor(string token)
        {
            switch (token)
            {
                case "VOLT:DC": return new[] { "VDC", "DCV", "V:DC" };
                case "VOLT:AC": return new[] { "VAC", "ACV", "V:AC" };
                case "CURR:DC": return new[] { "ADC", "DCA", "A:DC" };
                case "CURR:AC": return new[] { "AAC", "ACA", "A:AC" };
                case "RES": return new[] { "OHM", "RESISTANCE" };
                case "FREQ": return new[] { "HZ", "FREQUENCY" };
                case "PER": return new[] { "PERIOD" };
                case "CAP": return new[] { "CAPACITANCE" };
                case "CONT": return new[] { "CONTINUITY" };
                case "DIOD": return new[] { "DIODE" };
                case "TEMP": return new[] { "TEMP:RTD", "RTD" };
                default: return Array.Empty<string>();
            }
        }

        // ---------- Math/Averaging (safe) ----------

        public void SetAveraging(bool on)
        {
            try
            {
                if (on) Write("CALC:FUNC AVER");
                else Write("CALC:STAT OFF");
            }
            catch { }
        }

        /// <summary>Best-effort query for avg/min/max/count. Returns null if unsupported.</summary>
        public AveragingStats TryQueryAveragingAll()
        {
            try
            {
                var s = QuerySafe("CALC:AVER:ALL?");
                if (string.IsNullOrWhiteSpace(s)) return null;

                var parts = s.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 4) return null;

                double a = ParseDouble(parts[0]);
                double b = ParseDouble(parts[1]);
                double c = ParseDouble(parts[2]);
                double d = ParseDouble(parts[3]);

                var stats = new AveragingStats();

                int countCandidate = -1, which = -1;
                for (int i = 0; i < 4; i++)
                {
                    if (TryParseInt(parts[i], out int cnt) && cnt > countCandidate)
                    {
                        countCandidate = cnt; which = i;
                    }
                }
                if (which >= 0)
                {
                    stats.Count = countCandidate;
                    double[] vals = new double[3];
                    int idx = 0;
                    for (int i = 0; i < 4; i++)
                        if (i != which) vals[idx++] = ParseDouble(parts[i]);
                    Array.Sort(vals);
                    stats.Min = vals[0];
                    stats.Max = vals[2];
                    stats.Avg = vals[1];
                }
                else
                {
                    stats.Min = a; stats.Max = b; stats.Avg = c; stats.Count = (int)Math.Round(d);
                }

                return stats;
            }
            catch
            {
                return null;
            }
        }

        // ---------- Speed/Rate (safe) ----------

        public void SetRate(char speedF_M_L)
        {
            try
            {
                char v = char.ToUpperInvariant(speedF_M_L);
                if (v != 'F' && v != 'M' && v != 'S') return;
                Write("RATE " + v);
                Thread.Sleep(50);
            }
            catch { }
        }

        // ---------- Range/Auto (safe) ----------

        public void SetAutoRange(bool on)
        {
            try { Write(on ? "AUTO 1" : "AUTO 0"); } catch { }
        }

        public void SetRange(string rangeToken)
        {
            if (string.IsNullOrWhiteSpace(rangeToken)) return;
            try { Write("RANGE " + rangeToken); } catch { }
        }

        // ---------- Utilities ----------

        public void SetLineEnding(string newEnding)
        {
            LineEnding = newEnding;
            if (_port != null)
                _port.NewLine = newEnding;
        }

        public void Dispose()
        {
            try
            {
                Disconnect();
                _port?.Dispose();
                _port = null;
            }
            catch { }
        }

        // ---------- Private helpers ----------

        private static double ParseDouble(string s)
        {
            return double.Parse(s, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture);
        }

        private static bool TryParseInt(string s, out int value)
        {
            if (s.IndexOfAny(new[] { 'e', 'E', '.', ',' }) >= 0)
            {
                value = 0; return false;
            }
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        private static string NormalizeTokenNoSense(string f)
        {
            f = (f ?? "").Trim().ToUpperInvariant();
            if (f == "VOLT") f = "VOLT:DC";
            if (f == "CURR") f = "CURR:DC";
            if (f == "PERIOD") f = "PER";
            if (f == "DIODE") f = "DIOD";
            switch (f)
            {
                case "VOLT:DC":
                case "VOLT:AC":
                case "CURR:DC":
                case "CURR:AC":
                case "RES":
                case "FRES":
                case "FREQ":
                case "PER":
                case "CAP":
                case "CONT":
                case "DIOD":
                case "TEMP":
                    return f;
            }
            return "VOLT:DC"; // safest fallback
        }

        private static string TokenToConfNoSense(string token)
        {
            switch (token)
            {
                case "VOLT:DC": return "VOLT:DC";
                case "VOLT:AC": return "VOLT:AC";
                case "CURR:DC": return "CURR:DC";
                case "CURR:AC": return "CURR:AC";
                case "RES": return "RES";
                case "FRES": return "FRES";
                case "FREQ": return "FREQ";
                case "PER": return "PER";
                case "CAP": return "CAP";
                case "CONT": return "CONT";
                case "DIOD": return "DIOD";
                case "TEMP": return "TEMP:RTD";
                default: return "VOLT:DC";
            }
        }
    }
}
