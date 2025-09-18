// File: Services/ScpiDevice.cs
using System;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

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
        string GetFunction();

        // UX helpers (safe)
        void SetRate(char speedF_M_L);                   // 'F','M','S'
        void SetAveraging(bool on);
        void SetAutoRange(bool on);
        void SetRange(string rangeToken);
        AveragingStats TryQueryAveragingAll();
        void SetLineEnding(string newEnding);

        // Math suite (safe)
        void MathRelEnable();
        void MathRelZero();
        void MathOff();
        void MathDb(int referenceOhms);
        void MathDbm(int referenceOhms);

        // Continuity / beeper (safe)
        void SetContinuityThreshold(double ohms);
        void SetBeeper(bool on);

        // Remote/Local & queries (safe)
        void SetRemote();
        void SetLocal();
        string QueryRate();
        string QueryAverMax();
        string QueryAverMin();
        string QueryAverAvg();
        string QueryRange();
        string QueryFunction();

        // Configure ranges (safe)
        void ConfVoltDC(string range);
        void ConfVoltAC(string range);
        void ConfMilliVoltDC(string range);
        void ConfMilliVoltAC(string range);
        void ConfCurrDC_Amps(string range);
        void ConfCurrAC_Amps(string range);
        void ConfCurrDC_mA(string range);
        void ConfCurrAC_mA(string range);
        void ConfRes();
        void ConfFres(string range); // 4-wire resistance
        void ConfCap(string range);
        void ConfPer();

        // Temperature helpers (safe)
        string QueryTempType();
        void ConfigureTempTherKITS90();
        string ReadTempOnce();
        string QueryTempUnit();
        void SetTempUnit(char unitK_F_C);

        // Watchdog / robustness
        void WatchdogBump(Exception ex);
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
        // ----------------- Fields / Properties -----------------
        protected SerialPort _port;
        protected readonly object _ioLock = new object();

        public string PortName { get; }
        public int BaudRate { get; private set; }
        public Parity Parity { get; }
        public int DataBits { get; }
        public StopBits StopBits { get; }
        public Handshake Handshake { get; }
        public string LineEnding { get; private set; } = "\n";

        public bool IsConnected => _port != null && _port.IsOpen;

        private volatile ConnectionState _state = ConnectionState.Disconnected;
        public ConnectionState State => _state;
        public event EventHandler<ConnectionState> ConnectionStateChanged;

        private int _watchdogConsecutiveTimeouts;
        private DateTime _lastRestartUtc = DateTime.MinValue;

        private void SetState(ConnectionState s)
        {
            if (_state == s) return;
            _state = s;
            try { ConnectionStateChanged?.Invoke(this, s); } catch { }
        }

        // ----------------- Construction -----------------
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
            LineEnding = lineEnding ?? "\n";
        }

        // ----------------- Connect / Disconnect -----------------
        public virtual void Connect()
        {
            Disconnect();

            try
            {
                SetState(ConnectionState.Connecting);

                _port?.Dispose();
                _port = BuildPort(PortName, BaudRate, LineEnding);
                _port.Open();

                // Quick handshake: try *IDN? with current line ending first.
                if (!HandshakeTry())
                {
                    // Try other common line endings first to avoid unnecessary re-open
                    string originalEnding = LineEnding;

                    // \r\n -> \r -> \n
                    foreach (var ending in new[] { "\r\n", "\r", "\n" })
                    {
                        if (LineEnding != ending)
                        {
                            SetLineEnding(ending);
                            if (HandshakeTry())
                                break;
                        }
                    }

                    if (!IsConnected || string.IsNullOrWhiteSpace(ReadDeviceID()))
                    {
                        // Still no IDN: try a tiny baud fallback set
                        int[] fallbacks = (BaudRate == 9600)
                            ? new[] { 19200, 115200 }
                            : (BaudRate == 115200 ? new[] { 9600, 19200 } : new[] { 9600, 115200 });

                        foreach (var b in fallbacks)
                        {
                            if (TryOpenWithBaud(b, LineEnding) && HandshakeTry())
                            {
                                BaudRate = b; // lock-in working baud
                                break;
                            }
                        }
                    }

                    // If handshakes failed under altered ending, restore original ending
                    if (string.IsNullOrWhiteSpace(ReadDeviceID()))
                        SetLineEnding(originalEnding);
                }

                SetState(ConnectionState.Connected);
            }
            catch (Exception ex)
            {
                SetState(ConnectionState.Faulted);
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

        private SerialPort BuildPort(string port, int baud, string newLine)
        {
            var sp = new SerialPort(port, baud, Parity, DataBits)
            {
                StopBits = StopBits,
                Handshake = Handshake,
                ReadTimeout = 600,      // short to keep UI responsive
                WriteTimeout = 600,
                NewLine = newLine ?? "\n",
                DtrEnable = true,
                RtsEnable = false,
                Encoding = Encoding.ASCII
            };
            return sp;
        }

        private bool TryOpenWithBaud(int baud, string ending)
        {
            try
            {
                if (_port != null)
                {
                    try { if (_port.IsOpen) _port.Close(); } catch { }
                    try { _port.Dispose(); } catch { }
                }
                _port = BuildPort(PortName, baud, ending);
                _port.Open();
                return true;
            }
            catch { return false; }
        }

        private bool HandshakeTry()
        {
            try
            {
                var id = ReadDeviceID();
                return !string.IsNullOrWhiteSpace(id);
            }
            catch { return false; }
        }

        // ----------------- Low-level I/O (can throw) -----------------
        public virtual void Write(string command)
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

        // ----------------- Safe wrappers (never throw) -----------------
        private string QuerySafe(string command)
        {
            try
            {
                return Query(command);
            }
            catch (TimeoutException)
            {
                // Try a different line ending once, then revert
                string original = LineEnding;
                foreach (var ending in new[] { "\r\n", "\r", "\n" })
                {
                    try
                    {
                        if (ending == original) continue;
                        SetLineEnding(ending);
                        return Query(command);
                    }
                    catch { /* keep trying */ }
                    finally { SetLineEnding(original); }
                }
                SetState(ConnectionState.Faulted);
                return null;
            }
            catch
            {
                SetState(ConnectionState.Faulted);
                return null;
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
            return null;
        }

        private bool TryWrite(string cmd)
        {
            try { Write(cmd); return true; }
            catch { return false; }
        }

        private bool TryWriteAny(params string[] cmds)
        {
            foreach (var c in cmds)
                if (TryWrite(c)) return true;
            return false;
        }

        // ----------------- High-level ops (safe) -----------------
        public virtual string ReadMeasurement()
            => QueryFirstAvailable("MEAS?", "READ?", "FETC?");

        public virtual Task<string> ReadMeasurementAsync()
            => Task.Run(() => ReadMeasurement());

        public virtual string ReadDeviceID()
            => QueryFirstAvailable("*IDN?", "IDN?", "ID?", "SYST:VERS?") ?? string.Empty;

        public virtual Task<string> ReadDeviceIDAsync()
            => Task.Run(() => ReadDeviceID());

        // --------- Function setting (tolerant, safe) ---------
        public virtual void SetFunction(string function)
        {
            if (string.IsNullOrWhiteSpace(function))
                return;

            string token = NormalizeTokenNoSense(function); // e.g., "VOLT:AC"

            string[] attempts =
            {
                $"FUNC \"{token}\"",
                $"FUNC {token}",
                $"FUNC:MODE {token}",
                $"CONF:{TokenToConfNoSense(token)}"
            };

            lock (_ioLock)
            {
                try { Write("SYST:REM"); } catch { }

                bool anyWrite = false;
                foreach (var cmd in attempts)
                {
                    try
                    {
                        Write(cmd);
                        anyWrite = true;

                        // temperature convenience
                        if (token.StartsWith("TEMP", StringComparison.OrdinalIgnoreCase))
                        {
                            TryWriteAny("CONF:TEMP:RTD PT100",
                                        "TEMP:RTD:TYPE PT100",
                                        "TEMP:UNIT C");
                        }

                        if (ConfirmFunctionWithRetry(token, attemptsCount: 2, perAttemptTimeoutMs: 250))
                            return;
                    }
                    catch { /* try next */ }
                }

                if (anyWrite)
                {
                    // Could not confirm, but wrote something; keep going
                    return;
                }

                // Nothing succeeded
            }
        }

        public Task SetFunctionAsync(string function) => Task.Run(() => SetFunction(function));
        public string GetFunction() => QueryFirstAvailable("FUNC?", "CONF?") ?? string.Empty;

        private bool ConfirmFunctionWithRetry(string token, int attemptsCount = 2, int perAttemptTimeoutMs = 250)
        {
            for (int i = 0; i < attemptsCount; i++)
            {
                var r1 = QueryFast("FUNC?", perAttemptTimeoutMs);
                if (ReadbackMatches(r1, token)) return true;

                var r2 = QueryFast("CONF?", perAttemptTimeoutMs);
                if (ReadbackMatches(r2, token)) return true;

                Thread.Sleep(60 + i * 40);
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
                case "FRES": return new[] { "4WRES", "FOURWIRE", "4WIRE" };
                default: return Array.Empty<string>();
            }
        }

        // ----------------- Math / Averaging (safe) -----------------
        public void SetAveraging(bool on)
        {
            try
            {
                if (on)
                {
                    TryWriteAny("CALC:FUNC AVER");
                    TryWriteAny("CALC:STAT ON");
                }
                else
                {
                    TryWriteAny("CALC:STAT OFF");
                    TryWriteAny("CALC:FUNC OFF");
                }
            }
            catch { }
        }

        public AveragingStats TryQueryAveragingAll()
        {
            // Preferred vendor aggregate
            var s = QuerySafe("CALC:AVER:ALL?");
            if (string.IsNullOrWhiteSpace(s))
            {
                // Fallback: query individually
                var avg = QuerySafe("CALC:AVER:AVER?");
                var min = QuerySafe("CALC:AVER:MIN?");
                var max = QuerySafe("CALC:AVER:MAX?");
                var cnt = QuerySafe("CALC:AVER:COUN?");
                if (avg == null && min == null && max == null && cnt == null) return null;

                var stats = new AveragingStats();
                stats.Avg = ParseDoubleSafe(avg);
                stats.Min = ParseDoubleSafe(min);
                stats.Max = ParseDoubleSafe(max);
                stats.Count = (int)Math.Round(ParseDoubleSafe(cnt));
                return stats;
            }

            try
            {
                var parts = s.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 4) return null;

                // Try to detect which token is count
                int countIdx = -1; int best = -1;
                for (int i = 0; i < parts.Length && i < 4; i++)
                {
                    if (TryParseInt(parts[i], out int cnt) && cnt > best)
                    { best = cnt; countIdx = i; }
                }

                var stats = new AveragingStats();
                if (countIdx >= 0)
                {
                    stats.Count = best;
                    // other three become min/max/avg (order unknown) – sort
                    double[] vals = new double[3];
                    int k = 0;
                    for (int i = 0; i < 4; i++)
                        if (i != countIdx) vals[k++] = ParseDouble(parts[i]);

                    Array.Sort(vals);
                    stats.Min = vals[0];
                    stats.Avg = vals[1];
                    stats.Max = vals[2];
                }
                else
                {
                    // Assume avg,min,max,count
                    stats.Avg = ParseDouble(parts[0]);
                    stats.Min = ParseDouble(parts[1]);
                    stats.Max = ParseDouble(parts[2]);
                    stats.Count = (int)Math.Round(ParseDouble(parts[3]));
                }
                return stats;
            }
            catch { return null; }
        }

        public void MathRelEnable()
        {
            TryWriteAny("CALC:FUNC NULL");
            TryWriteAny("CALC:STAT ON");
        }

        public void MathRelZero()
        {
            TryWriteAny("CALC:NULL:OFFS");
        }

        public void MathOff()
        {
            TryWriteAny("CALC:STAT OFF");
            TryWriteAny("CALC:FUNC OFF");
        }

        public void MathDb(int referenceOhms)
        {
            TryWriteAny("CALC:FUNC DB");
            TryWriteAny($"CALC:DB:REF {referenceOhms}");
            TryWriteAny($"CALC:DBM:REF {referenceOhms}"); // harmless if unsupported
            TryWriteAny("CALC:STAT ON");
        }

        public void MathDbm(int referenceOhms)
        {
            TryWriteAny("CALC:FUNC DBM");
            TryWriteAny($"CALC:DBM:REF {referenceOhms}");
            TryWriteAny($"CALC:DB:REF {referenceOhms}");
            TryWriteAny("CALC:STAT ON");
        }

        // ----------------- Speed / Rate (safe) -----------------
        public void SetRate(char speedF_M_L)
        {
            try
            {
                char v = char.ToUpperInvariant(speedF_M_L);
                if (v == 'L') v = 'S';
                if (v != 'F' && v != 'M' && v != 'S') return;

                if (!TryWriteAny("RATE " + v))
                {
                    TryWriteAny("SAMP:RATE " + v);
                    TryWriteAny("TRIG:COUN INF"); // keep device streaming
                }
                Thread.Sleep(50);
            }
            catch { }
        }

        // ----------------- Range / Auto (safe) -----------------
        public void SetAutoRange(bool on)
        {
            try
            {
                if (on)
                {
                    if (!TryWriteAny("AUTO 1", "RANG:AUTO ON"))
                    {
                        TryWriteAny("SENS:VOLT:DC:RANG:AUTO ON",
                                    "SENS:VOLT:AC:RANG:AUTO ON",
                                    "SENS:RES:RANG:AUTO ON",
                                    "SENS:CURR:DC:RANG:AUTO ON",
                                    "SENS:CURR:AC:RANG:AUTO ON");
                    }
                }
                else
                {
                    if (!TryWriteAny("AUTO 0", "RANG:AUTO OFF"))
                    {
                        TryWriteAny("SENS:VOLT:DC:RANG:AUTO OFF",
                                    "SENS:VOLT:AC:RANG:AUTO OFF",
                                    "SENS:RES:RANG:AUTO OFF",
                                    "SENS:CURR:DC:RANG:AUTO OFF",
                                    "SENS:CURR:AC:RANG:AUTO OFF");
                    }
                }
            }
            catch { }
        }

        public void SetRange(string rangeToken)
        {
            if (string.IsNullOrWhiteSpace(rangeToken)) return;
            try
            {
                if (!TryWriteAny("RANGE " + rangeToken))
                {
                    // Fallback attempts across common functions
                    TryWriteAny("SENS:VOLT:DC:RANG " + rangeToken,
                                "SENS:VOLT:AC:RANG " + rangeToken,
                                "SENS:RES:RANG " + rangeToken,
                                "SENS:CURR:DC:RANG " + rangeToken,
                                "SENS:CURR:AC:RANG " + rangeToken);
                }
            }
            catch { }
        }

        // ----------------- Utilities -----------------
        public void SetLineEnding(string newEnding)
        {
            LineEnding = string.IsNullOrEmpty(newEnding) ? "\n" : newEnding;
            if (_port != null) _port.NewLine = LineEnding;
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

        // ----------------- Temperature -----------------
        public string QueryTempType() => QueryFirstAvailable("TEMP:RTD:TYPE?");
        public void ConfigureTempTherKITS90() { TryWriteAny("CONF:TEMP:THER KITS90"); }
        public string ReadTempOnce() => QueryFirstAvailable("MEAS:TEMP?");
        public string QueryTempUnit() => QueryFirstAvailable("TEMP:RTD:UNIT?");
        public void SetTempUnit(char unitK_F_C)
        {
            try
            {
                char u = char.ToUpperInvariant(unitK_F_C);
                if (u == 'K' || u == 'F' || u == 'C') Write($"TEMP:RTD:UNIT {u}");
            }
            catch { }
        }

        // ----------------- Queries & Remote/Local -----------------
        public string QueryRate() => QueryFirstAvailable("RATE?", "SAMP:RATE?");
        public string QueryAverMax() => QueryFirstAvailable("CALC:AVER:MAX?");
        public string QueryAverMin() => QueryFirstAvailable("CALC:AVER:MIN?");
        public string QueryAverAvg() => QueryFirstAvailable("CALC:AVER:AVER?");

        public void SetRemote() { TryWriteAny("SYST:REM"); }
        public void SetLocal() { TryWriteAny("SYST:LOC"); }

        public string QueryRange()
            => QueryFirstAvailable("RANGE?", "SENS:VOLT:DC:RANG?", "SENS:RES:RANG?", "SENS:CURR:DC:RANG?");

        public string QueryFunction() => QueryFirstAvailable("FUNC?") ?? string.Empty;

        // ----------------- Configure convenience -----------------
        public void ConfVoltDC(string range) { if (!string.IsNullOrWhiteSpace(range)) TryWriteAny($"CONF:VOLT:DC {range}"); else TryWriteAny("CONF:VOLT:DC"); }
        public void ConfVoltAC(string range) { if (!string.IsNullOrWhiteSpace(range)) TryWriteAny($"CONF:VOLT:AC {range}"); else TryWriteAny("CONF:VOLT:AC"); }

        public void ConfMilliVoltDC(string range) { if (!string.IsNullOrWhiteSpace(range)) TryWriteAny($"CONF:VOLT:DC {range}"); else TryWriteAny("CONF:VOLT:DC"); }
        public void ConfMilliVoltAC(string range) { if (!string.IsNullOrWhiteSpace(range)) TryWriteAny($"CONF:VOLT:AC {range}"); else TryWriteAny("CONF:VOLT:AC"); }

        public void ConfCurrDC_Amps(string range) { if (!string.IsNullOrWhiteSpace(range)) TryWriteAny($"CONF:CURR:DC {range}"); else TryWriteAny("CONF:CURR:DC"); }
        public void ConfCurrAC_Amps(string range) { if (!string.IsNullOrWhiteSpace(range)) TryWriteAny($"CONF:CURR:AC {range}"); else TryWriteAny("CONF:CURR:AC"); }

        public void ConfCurrDC_mA(string range) { if (!string.IsNullOrWhiteSpace(range)) TryWriteAny($"CONF:CURR:DC {range}"); else TryWriteAny("CONF:CURR:DC"); }
        public void ConfCurrAC_mA(string range) { if (!string.IsNullOrWhiteSpace(range)) TryWriteAny($"CONF:CURR:AC {range}"); else TryWriteAny("CONF:CURR:AC"); }

        public void ConfRes() { TryWriteAny("CONF:RES"); }

        public void ConfFres(string range)
        {
            if (!string.IsNullOrWhiteSpace(range)) TryWriteAny($"CONF:FRES {range}");
            else TryWriteAny("CONF:FRES");
            // Enable 4-wire sense on devices that need it
            TryWriteAny("SYST:RSEN ON", "SYST:FOUR:WIRE ON");
        }

        public void ConfCap(string range) { if (!string.IsNullOrWhiteSpace(range)) TryWriteAny($"CONF:CAP {range}"); else TryWriteAny("CONF:CAP"); }
        public void ConfPer() { TryWriteAny("CONF:PER"); }

        // ----------------- Continuity & Beeper -----------------
        public void SetContinuityThreshold(double ohms)
        {
            TryWriteAny($"CONT:THRE {ohms.ToString("G17", CultureInfo.InvariantCulture)}",
                        $"SENS:CONT:THR {ohms.ToString("G17", CultureInfo.InvariantCulture)}");
        }

        public void SetBeeper(bool on)
        {
            if (!TryWriteAny(on ? "SYST:BEEP:STAT ON" : "SYST:BEEP:STAT OFF"))
                TryWriteAny(on ? "SYST:BEEPER:STATE ON" : "SYST:BEEPER:STATE OFF");
        }

        // ----------------- Watchdog -----------------
        public void WatchdogBump(Exception ex)
        {
            bool isTimeoutLike = ex is TimeoutException || ex is IOException;
            if (!isTimeoutLike)
            {
                _watchdogConsecutiveTimeouts = 0;
                return;
            }

            int c = Interlocked.Increment(ref _watchdogConsecutiveTimeouts);
            if (c < 3) return; // tolerate a couple of hiccups

            var now = DateTime.UtcNow;
            if ((now - _lastRestartUtc).TotalSeconds < 2) return; // don't flap

            _lastRestartUtc = now;
            Interlocked.Exchange(ref _watchdogConsecutiveTimeouts, 0);

            // Attempt a quick restart with the same settings.
            try
            {
                lock (_ioLock)
                {
                    try { if (_port != null && _port.IsOpen) _port.Close(); } catch { }
                    try { _port?.Dispose(); } catch { }
                    _port = BuildPort(PortName, BaudRate, LineEnding);
                    _port.Open();

                    // Soft handshake; don't throw
                    _ = ReadDeviceID();
                    SetState(ConnectionState.Connected);
                }
            }
            catch
            {
                SetState(ConnectionState.Faulted);
            }
        }

        // ----------------- Helpers -----------------
        private static double ParseDouble(string s) => double.Parse(s, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture);
        private static double ParseDoubleSafe(string s)
        {
            if (double.TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var v))
                return v;
            return double.NaN;
        }

        private static bool TryParseInt(string s, out int value)
        {
            if (string.IsNullOrEmpty(s)) { value = 0; return false; }
            if (s.IndexOfAny(new[] { 'e', 'E', '.', ',' }) >= 0) { value = 0; return false; }
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
            return "VOLT:DC";
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
