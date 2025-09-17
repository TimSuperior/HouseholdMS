using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HouseholdMS.Helpers; // for ModbusRtuRaw & EpeverRegisters

namespace HouseholdMS.Services
{
    /// <summary>Simple port descriptor for UI binding.</summary>
    public sealed class PortDescriptor
    {
        public string Port { get; }
        public string Label { get; }
        public string Kind { get; } // "SCPI", "EPEVER", "Unknown", "Busy", etc.

        public PortDescriptor(string port, string label, string kind)
        {
            Port = port;
            Label = label;
            Kind = kind;
        }

        public override string ToString() => $"{Port} — {Label}";
    }

    /// <summary>
    /// Probes each COM port quickly:
    /// 1) SCPI handshake (*IDN?) @ 9600 8N1
    /// 2) EPEVER Modbus RTU (Read Input Registers 0x3100) @ ID=1 on common bauds
    /// Falls back to "Unknown" or "Busy".
    /// </summary>
    public static class SerialPortInspector
    {
        public static async Task<List<PortDescriptor>> ProbeAllAsync(int perPortTimeoutMs = 600, CancellationToken ct = default)
        {
            var ports = SerialPort.GetPortNames()
                                  .OrderBy(s => ExtractComNumber(s))
                                  .ThenBy(s => s)
                                  .ToList();

            var tasks = ports.Select(p => Task.Run(() => ProbeOne(p, perPortTimeoutMs, ct), ct)).ToArray();
            var results = await Task.WhenAll(tasks);
            return results.ToList();
        }

        private static PortDescriptor ProbeOne(string port, int timeoutMs, CancellationToken ct)
        {
            try
            {
                ct.ThrowIfCancellationRequested();

                // --- 1) SCPI try (*IDN?)
                var scpi = TryProbeScpi(port, timeoutMs);
                if (scpi != null)
                {
                    string label = ShortenIdn(scpi, out string friendly);
                    return new PortDescriptor(port, string.IsNullOrWhiteSpace(friendly) ? $"SCPI [{label}]" : friendly, "SCPI");
                }

                // --- 2) EPEVER try (Modbus RTU, ID 1, common bauds)
                var mppt = TryProbeEpever(port, timeoutMs);
                if (mppt != null)
                {
                    return new PortDescriptor(port, mppt, "EPEVER");
                }

                // --- 3) Unknown but present
                return new PortDescriptor(port, "Unknown / No quick response", "Unknown");
            }
            catch (UnauthorizedAccessException)
            {
                return new PortDescriptor(port, "In use by another app", "Busy");
            }
            catch (Exception ex)
            {
                return new PortDescriptor(port, "Error: " + Trim(ex.Message, 60), "Error");
            }
        }

        private static string TryProbeScpi(string port, int timeoutMs)
        {
            // Many bench DMMs (incl. MP730889) default to 9600, 8N1, ASCII
            using (var sp = new SerialPort(port, 9600, Parity.None, 8, StopBits.One))
            {
                sp.ReadTimeout = timeoutMs;
                sp.WriteTimeout = timeoutMs;
                sp.Handshake = Handshake.None;
                sp.DtrEnable = true;
                sp.RtsEnable = false;
                sp.Encoding = Encoding.ASCII;
                try
                {
                    sp.Open();
                    // try \n
                    sp.DiscardInBuffer();
                    sp.DiscardOutBuffer();
                    sp.NewLine = "\n";
                    sp.Write("*IDN?\n");
                    try { return sp.ReadLine(); }
                    catch
                    {
                        // try \r\n
                        sp.DiscardInBuffer();
                        sp.DiscardOutBuffer();
                        sp.NewLine = "\r\n";
                        sp.Write("*IDN?\r\n");
                        return sp.ReadLine();
                    }
                }
                catch
                {
                    return null;
                }
            }
        }

        private static string TryProbeEpever(string port, int timeoutMs)
        {
            // EPEVER defaults are often 115200 8N1, ID=1.
            // We try a small set of common bauds quickly.
            var bauds = new[] { 115200, 57600, 9600 };
            foreach (var baud in bauds)
            {
                try
                {
                    string err;
                    var regs = ModbusRtuRaw.TryReadInputRegisters(port, baud, 1, EpeverRegisters.PV_START, 1, timeoutMs, out err);
                    if (regs != null && regs.Length >= 1)
                        return $"EPEVER MPPT (ID=1 @ {baud}bps)";
                }
                catch
                {
                    // keep trying next baud
                }
            }
            return null;
        }

        private static int ExtractComNumber(string s)
        {
            if (s.StartsWith("COM", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(s.Substring(3), out int n)) return n;
            return int.MaxValue;
        }

        private static string ShortenIdn(string idn, out string friendly)
        {
            friendly = null;
            if (string.IsNullOrWhiteSpace(idn)) return "(empty)";
            var trimmed = idn.Trim();
            var parts = trimmed.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            // MP730889 often includes model in IDN
            if (parts.Any(p => p.IndexOf("MP730889", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                friendly = "MP730889 DMM";
            }
            else if (parts.Length > 0)
            {
                var model = parts.Last().Trim();
                if (model.Length > 2 && model.Length <= 32) friendly = model;
            }
            return Trim(trimmed.Replace('\t', ' '), 64);
        }

        private static string Trim(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return (s.Length <= max) ? s : s.Substring(0, max - 1) + "…";
        }
    }
}
