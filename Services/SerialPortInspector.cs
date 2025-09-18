using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HouseholdMS.Helpers; // ModbusRtuRaw

namespace HouseholdMS.Services
{
    public sealed class PortDescriptor
    {
        public string Port { get; }
        public string Label { get; }   // raw identity string (not made friendly)
        public string Kind { get; }    // "SCPI", "MODBUS", "AT", "Unknown", "Busy", "Error", "Canceled"

        public PortDescriptor(string port, string label, string kind)
        {
            Port = port;
            Label = label;
            Kind = kind;
        }

        public override string ToString() => $"{Port} — {Label}";
    }

    /// <summary>
    /// Fast, cached scanner that identifies devices by asking them who they are.
    /// Order: SCPI (*IDN?), Modbus 0x2B/0x0E (vendor/product/revision), generic "ATI".
    /// Multi-baud probing with short timeouts. Results cached to avoid rescans on navigation.
    /// </summary>
    public static class SerialPortInspector
    {
        private static readonly object _cacheLock = new object();
        private static List<PortDescriptor> _lastScan;
        private static DateTime _lastScanAtUtc = DateTime.MinValue;

        public static async Task<List<PortDescriptor>> GetOrProbeAsync(
            int attemptTimeoutMs = 200,
            int cacheTtlMs = 15000,
            CancellationToken ct = default)
        {
            lock (_cacheLock)
            {
                if (_lastScan != null && (DateTime.UtcNow - _lastScanAtUtc).TotalMilliseconds < cacheTtlMs)
                    return new List<PortDescriptor>(_lastScan);
            }

            var res = await ProbeAllAsync(attemptTimeoutMs, ct).ConfigureAwait(false);
            lock (_cacheLock)
            {
                _lastScan = res;
                _lastScanAtUtc = DateTime.UtcNow;
            }
            return new List<PortDescriptor>(res);
        }

        public static void InvalidateCache()
        {
            lock (_cacheLock)
            {
                _lastScan = null;
                _lastScanAtUtc = DateTime.MinValue;
            }
        }

        public static async Task<List<PortDescriptor>> ProbeAllAsync(int attemptTimeoutMs = 200, CancellationToken ct = default)
        {
            var ports = SerialPort.GetPortNames()
                                  .OrderBy(s => ExtractComNumber(s))
                                  .ThenBy(s => s)
                                  .ToList();

            var tasks = ports.Select(p => Task.Run(() => ProbeOnePort(p, attemptTimeoutMs, ct), ct)).ToArray();
            var results = await Task.WhenAll(tasks).ConfigureAwait(false);
            return results.ToList();
        }

        private static PortDescriptor ProbeOnePort(string port, int attemptTimeoutMs, CancellationToken ct)
        {
            // Keep it snappy per port (~1.0–1.6s max)
            var perPortBudgetMs = Math.Max(1000, attemptTimeoutMs * 8);
            var started = Environment.TickCount;

            try
            {
                ct.ThrowIfCancellationRequested();

                // (A) SCPI *IDN? across common bauds + line endings
                var scpi = TryProbeScpiMulti(port, attemptTimeoutMs, perPortBudgetMs, ref started);
                if (!string.IsNullOrWhiteSpace(scpi))
                    return new PortDescriptor(port, Trim(scpi.Trim(), 128), "SCPI");

                // (B) Modbus "Read Device Identification" (0x2B/0x0E) across bauds/IDs
                var modbus = TryProbeModbusIdMulti(port, attemptTimeoutMs, perPortBudgetMs, ref started);
                if (!string.IsNullOrWhiteSpace(modbus))
                    return new PortDescriptor(port, Trim(modbus.Trim(), 128), "MODBUS");

                // (C) Generic "ATI" (many serial devices/modems reply with model/version)
                var ati = TryProbeGenericATI(port, attemptTimeoutMs, perPortBudgetMs, ref started);
                if (!string.IsNullOrWhiteSpace(ati))
                    return new PortDescriptor(port, Trim(ati.Trim(), 128), "AT");

                return new PortDescriptor(port, "Unknown / No quick response", "Unknown");
            }
            catch (UnauthorizedAccessException)
            {
                return new PortDescriptor(port, "In use by another app", "Busy");
            }
            catch (OperationCanceledException)
            {
                return new PortDescriptor(port, "Scan canceled", "Canceled");
            }
            catch (Exception ex)
            {
                return new PortDescriptor(port, "Error: " + Trim(ex.Message, 80), "Error");
            }
        }

        // ---------- SCPI ----------
        private static string TryProbeScpiMulti(string port, int attemptTimeoutMs, int budgetMs, ref int startedTick)
        {
            var baudsFast = new[] { 9600, 19200, 115200 };
            var baudsSlow = new[] { 57600, 38400, 4800, 2400 };
            var endings = new[] { "\n", "\r\n" };

            foreach (var baud in baudsFast)
            {
                foreach (var nl in endings)
                {
                    if (ElapsedMs(startedTick) > budgetMs) return null;
                    var idn = TryProbeScpiOnce(port, baud, nl, attemptTimeoutMs);
                    if (!string.IsNullOrWhiteSpace(idn)) return idn;
                }
            }

            if (ElapsedMs(startedTick) > budgetMs * 0.75) return null;

            foreach (var baud in baudsSlow)
            {
                foreach (var nl in endings)
                {
                    if (ElapsedMs(startedTick) > budgetMs) return null;
                    var idn = TryProbeScpiOnce(port, baud, nl, attemptTimeoutMs);
                    if (!string.IsNullOrWhiteSpace(idn)) return idn;
                }
            }
            return null;
        }

        private static string TryProbeScpiOnce(string port, int baud, string newline, int timeoutMs)
        {
            try
            {
                using (var sp = new SerialPort(port, baud, Parity.None, 8, StopBits.One))
                {
                    sp.ReadTimeout = timeoutMs;
                    sp.WriteTimeout = timeoutMs;
                    sp.Handshake = Handshake.None;
                    sp.DtrEnable = true;
                    sp.RtsEnable = false;
                    sp.Encoding = Encoding.ASCII;
                    sp.NewLine = newline;

                    sp.Open();
                    sp.DiscardInBuffer();
                    sp.DiscardOutBuffer();

                    Thread.Sleep(10); // settle
                    sp.Write("*IDN?" + newline);
                    try { return sp.ReadLine(); }
                    catch { return null; }
                }
            }
            catch { return null; }
        }

        // ---------- Modbus 0x2B/0x0E ----------
        private static string TryProbeModbusIdMulti(string port, int attemptTimeoutMs, int budgetMs, ref int startedTick)
        {
            // reasonable defaults — most USB–RS485 bridges & MPPTs will be here
            var bauds = new[] { 115200, 9600, 19200 };
            var unitIds = new byte[] { 1, 2, 3, 4 }; // small set to keep scan quick

            foreach (var b in bauds)
            {
                foreach (var uid in unitIds)
                {
                    if (ElapsedMs(startedTick) > budgetMs) return null;

                    try
                    {
                        var info = ModbusRtuRaw.TryReadDeviceIdentification(port, b, uid,
                            category: ModbusRtuRaw.DeviceIdCategory.Basic,
                            timeoutMs: attemptTimeoutMs,
                            error: out string err);

                        if (info != null && info.Count > 0)
                        {
                            // Compose raw label from returned objects (0x00.. vendor, 0x01.. product, 0x02.. revision)
                            info.TryGetValue(0x00, out string vendor);
                            info.TryGetValue(0x01, out string product);
                            info.TryGetValue(0x02, out string rev);

                            var pieces = new List<string>();
                            if (!string.IsNullOrWhiteSpace(vendor)) pieces.Add(vendor.Trim());
                            if (!string.IsNullOrWhiteSpace(product)) pieces.Add(product.Trim());
                            if (!string.IsNullOrWhiteSpace(rev)) pieces.Add(rev.Trim());

                            var label = pieces.Count > 0 ? string.Join(" ", pieces) : "[Device Identification present]";
                            return label;
                        }

                        // If Basic had nothing, try Regular quickly if budget allows
                        if (ElapsedMs(startedTick) > budgetMs) return null;
                        var info2 = ModbusRtuRaw.TryReadDeviceIdentification(port, b, uid,
                            category: ModbusRtuRaw.DeviceIdCategory.Regular,
                            timeoutMs: attemptTimeoutMs,
                            error: out string err2);
                        if (info2 != null && info2.Count > 0)
                        {
                            // Dump first couple objects into a raw string
                            var vals = info2.OrderBy(kv => kv.Key)
                                            .Take(4)
                                            .Select(kv => $"{kv.Key:X2}={kv.Value}")
                                            .ToArray();
                            return string.Join("; ", vals);
                        }
                    }
                    catch { /* next */ }
                }
            }
            return null;
        }

        // ---------- Generic "ATI" ----------
        private static string TryProbeGenericATI(string port, int attemptTimeoutMs, int budgetMs, ref int startedTick)
        {
            var bauds = new[] { 115200, 9600, 19200, 57600 };
            var endings = new[] { "\r\n", "\n" };
            foreach (var b in bauds)
            {
                foreach (var nl in endings)
                {
                    if (ElapsedMs(startedTick) > budgetMs) return null;
                    var s = TryProbeAtiOnce(port, b, nl, attemptTimeoutMs);
                    if (!string.IsNullOrWhiteSpace(s)) return s;
                }
            }
            return null;
        }

        private static string TryProbeAtiOnce(string port, int baud, string newline, int timeoutMs)
        {
            try
            {
                using (var sp = new SerialPort(port, baud, Parity.None, 8, StopBits.One))
                {
                    sp.ReadTimeout = timeoutMs;
                    sp.WriteTimeout = timeoutMs;
                    sp.Handshake = Handshake.None;
                    sp.DtrEnable = true;
                    sp.RtsEnable = false;
                    sp.Encoding = Encoding.ASCII;
                    sp.NewLine = newline;

                    sp.Open();
                    sp.DiscardInBuffer();
                    sp.DiscardOutBuffer();

                    Thread.Sleep(10);
                    sp.Write("ATI" + newline);
                    var line = SafeReadLineTrim(sp);
                    if (!string.IsNullOrWhiteSpace(line) && !IsJustOkError(line))
                        return line;

                    // Some devices respond multiple lines
                    var line2 = SafeReadLineTrim(sp);
                    if (!string.IsNullOrWhiteSpace(line2) && !IsJustOkError(line2))
                        return line2;

                    return null;
                }
            }
            catch { return null; }
        }

        private static string SafeReadLineTrim(SerialPort sp)
        {
            try { return (sp.ReadLine() ?? "").Trim(); }
            catch { return null; }
        }

        private static bool IsJustOkError(string s)
        {
            var t = (s ?? "").Trim().ToUpperInvariant();
            return t == "OK" || t == "ERROR" || t == "AT";
        }

        // ---------- Utils ----------
        private static int ExtractComNumber(string s)
        {
            if (s.StartsWith("COM", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(s.Substring(3), out int n)) return n;
            return int.MaxValue;
        }

        private static int ElapsedMs(int startTick) => unchecked(Environment.TickCount - startTick);
        private static string Trim(string s, int max) => string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max - 1) + "…";
    }
}
