// It8615.Driver.cs
using System;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ivi.Visa.Interop;
using HouseholdMS.Models;
using HouseholdMS.Services;

namespace HouseholdMS.Drivers
{
    public interface IInstrument
    {
        Task<string> IdentifyAsync();
        Task ToRemoteAsync();
        Task ToLocalAsync();
        Task DrainErrorQueueAsync();

        Task SetAcDcAsync(bool ac);
        Task SetFunctionAsync(string func);      // CURR/RES/VOLT/POW/SHOR
        Task SetSetpointAsync(double value);     // units depend on func
        string CurrentUnits { get; }

        Task SetPfCfAsync(double pf, double cf); // AC only
        Task EnableInputAsync(bool on);
        Task EStopAsync();

        // Ranges
        Task CacheRangesAsync();
        string DescribeRanges();

        // Meter
        Task<InstrumentReading> ReadAsync();

        // Scope
        Task ScopeConfigureAsync(string trigSource, string trigSlope, double trigLevel);
        Task ScopeRunAsync();
        Task ScopeSingleAsync();
        Task ScopeStopAsync();
        Task<(double[] v, double[] i)> FetchWaveformsAsync();

        // Harmonics (voltage)
        Task<double[]> MeasureVoltageHarmonicsAsync(int n);
    }

    public class ItechIt8615 : IInstrument
    {
        private readonly VisaSession _io;
        private readonly CommandLogger _log;
        private readonly CultureInfo _ci = CultureInfo.InvariantCulture;

        private double _iMin, _iMax, _rMin, _rMax, _vMin, _vMax, _pMin, _pMax, _pfMin, _pfMax, _cfMin, _cfMax;
        public string CurrentUnits { get; private set; } = "A";

        public ItechIt8615(VisaSession io, CommandLogger log) { _io = io; _log = log; }

        public Task<string> IdentifyAsync() => _io.QueryAsync("*IDN?");
        public Task ToRemoteAsync() => _io.WriteAsync("SYST:REM");
        public Task ToLocalAsync() => _io.WriteAsync("SYST:LOC");

        public async Task DrainErrorQueueAsync()
        {
            for (int i = 0; i < 10; i++)
            {
                string e = await _io.QueryAsync("SYST:ERR?");
                _log.Log("ERRQ: " + e);
                if (!string.IsNullOrWhiteSpace(e) && e.StartsWith("0")) break;
            }
        }

        public Task SetAcDcAsync(bool ac) => _io.WriteAsync("SYST:MODE " + (ac ? "AC" : "DC"));

        public async Task SetFunctionAsync(string func)
        {
            await _io.WriteAsync("SOUR:FUNC " + func);
            if (func == "CURR") CurrentUnits = "A";
            else if (func == "RES") CurrentUnits = "Ω";
            else if (func == "VOLT") CurrentUnits = "V";
            else if (func == "POW") CurrentUnits = "W";
            else CurrentUnits = "";
        }

        public async Task SetSetpointAsync(double value)
        {
            string cmd = "SOUR:CURR"; double lo = _iMin, hi = _iMax;
            if (CurrentUnits == "Ω") { cmd = "SOUR:RES"; lo = _rMin; hi = _rMax; }
            else if (CurrentUnits == "V") { cmd = "SOUR:VOLT"; lo = _vMin; hi = _vMax; }
            else if (CurrentUnits == "W") { cmd = "SOUR:POW"; lo = _pMin; hi = _pMax; }
            if (value < lo) value = lo;
            if (value > hi) value = hi;
            await _io.WriteAsync(cmd + " " + value.ToString(_ci));
        }

        public Task SetPfCfAsync(double pf, double cf)
        {
            if (pf < _pfMin) pf = _pfMin; if (pf > _pfMax) pf = _pfMax;
            if (cf < _cfMin) cf = _cfMin; if (cf > _cfMax) cf = _cfMax;
            return _io.WriteAsync("SOUR:PFAC " + pf.ToString(_ci) + ";:SOUR:CFAC " + cf.ToString(_ci));
        }

        public Task EnableInputAsync(bool on) => _io.WriteAsync("INP:STAT " + (on ? "ON" : "OFF"));

        public async Task EStopAsync()
        {
            await _io.WriteAsync("INP:STAT OFF;:SOUR:PFAC 1;:SOUR:CFAC 1.41");
            await ScopeStopAsync();
        }

        public async Task CacheRangesAsync()
        {
            _iMin = await _io.QueryNumberAsync("SOUR:CURR:LEV:IMM:AMPL? MIN");
            _iMax = await _io.QueryNumberAsync("SOUR:CURR:LEV:IMM:AMPL? MAX");
            _rMin = await _io.QueryNumberAsync("SOUR:RES:LEV:IMM:AMPL? MIN");
            _rMax = await _io.QueryNumberAsync("SOUR:RES:LEV:IMM:AMPL? MAX");
            _vMin = await _io.QueryNumberAsync("SOUR:VOLT:LEV:IMM:AMPL? MIN");
            _vMax = await _io.QueryNumberAsync("SOUR:VOLT:LEV:IMM:AMPL? MAX");
            _pMin = await _io.QueryNumberAsync("SOUR:POW:LEV:IMM:AMPL? MIN");
            _pMax = await _io.QueryNumberAsync("SOUR:POW:LEV:IMM:AMPL? MAX");

            _pfMin = await _io.QueryNumberAsync("SOUR:PFAC:LEV:IMM:AMPL? MIN");
            _pfMax = await _io.QueryNumberAsync("SOUR:PFAC:LEV:IMM:AMPL? MAX");
            _cfMin = await _io.QueryNumberAsync("SOUR:CFAC:LEV:IMM:AMPL? MIN");
            _cfMax = await _io.QueryNumberAsync("SOUR:CFAC:LEV:IMM:AMPL? MAX");
        }

        public string DescribeRanges()
        {
            Func<double, string> f = v => v.ToString("G4", _ci);
            var sb = new StringBuilder();
            sb.AppendLine("I: [" + f(_iMin) + "," + f(_iMax) + "] A");
            sb.AppendLine("R: [" + f(_rMin) + "," + f(_rMax) + "] Ω");
            sb.AppendLine("V: [" + f(_vMin) + "," + f(_vMax) + "] V");
            sb.AppendLine("P: [" + f(_pMin) + "," + f(_pMax) + "] W");
            sb.AppendLine("PF: [" + f(_pfMin) + "," + f(_pfMax) + "]  CF: [" + f(_cfMin) + "," + f(_cfMax) + "]");
            return sb.ToString();
        }

        private static bool Bad(double v) => double.IsNaN(v) || double.IsInfinity(v);

        public async Task<InstrumentReading> ReadAsync()
        {
            // Robust reads with fallbacks so labels don't stay at 0 when data exists
            double vrms = await _io.QueryNumberAsync("MEAS:VOLT:RMS?");
            if (Bad(vrms) || vrms == 0) vrms = await _io.QueryNumberAsync("MEAS:VOLT?");

            double irms = await _io.QueryNumberAsync("MEAS:CURR:RMS?");
            if (Bad(irms) || irms == 0) irms = await _io.QueryNumberAsync("MEAS:CURR?");

            double pow = await _io.QueryNumberAsync("MEAS:POW?");
            double pf = await _io.QueryNumberAsync("MEAS:POW:PFAC?");
            double cf = await _io.QueryNumberAsync("MEAS:CURR:CFAC?");
            double f = await _io.QueryNumberAsync("MEAS:FREQ?");

            // Derive PF if missing
            if ((Bad(pf) || pf == 0) && vrms > 1e-9 && irms > 1e-9 && !Bad(pow) && pow != 0)
            {
                var denom = vrms * irms;
                if (denom > 1e-12) pf = pow / denom;
            }

            // Derive Power if missing
            if (Bad(pow) || pow == 0)
            {
                if (vrms > 1e-9 && irms > 1e-9)
                {
                    var pfUse = (!Bad(pf) && pf > 0) ? pf : 1.0;
                    pow = vrms * irms * pfUse;
                }
            }

            return new InstrumentReading
            {
                Timestamp = DateTime.UtcNow,
                Vrms = vrms,
                Irms = irms,
                Power = pow,
                Pf = pf,
                CrestFactor = cf,
                Freq = f
            };
        }

        // ----- Scope -----
        public async Task ScopeConfigureAsync(string source, string slope, double level)
        {
            await _io.WriteAsync("WAVE:TRIG:SOUR " + source);   // VOLTage|CURRent
            await _io.WriteAsync("WAVE:TRIG:SLOP " + slope);    // POSitive|NEGative|ANY
            if (source.StartsWith("V", StringComparison.OrdinalIgnoreCase))
                await _io.WriteAsync("WAVE:TRIG:VOLT:LEV " + level.ToString(_ci));
            else
                await _io.WriteAsync("WAVE:TRIG:CURR:LEV " + level.ToString(_ci));
        }
        public Task ScopeRunAsync() => _io.WriteAsync("WAVE:RUN");
        public Task ScopeSingleAsync() => _io.WriteAsync("WAVE:SING");
        public Task ScopeStopAsync() => _io.WriteAsync("WAVE:STOP");

        public async Task<(double[] v, double[] i)> FetchWaveformsAsync()
        {
            string vs = await _io.QueryAsync("WAVE:VOLT:DATA?");
            string is_ = await _io.QueryAsync("WAVE:CURR:DATA?");
            return (ParseCsv(vs), ParseCsv(is_));
        }

        public async Task<double[]> MeasureVoltageHarmonicsAsync(int n)
        {
            string resp = await _io.QueryAsync("MEAS:VOLT:HARM:AMPL?");
            double[] arr = ParseCsv(resp);
            if (n < arr.Length) return arr.Take(n).ToArray();
            return arr;
        }

        private static double[] ParseCsv(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return Array.Empty<double>();
            string[] ss = s.Split(new char[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            double[] vals = new double[ss.Length];
            int k = 0;
            for (int i = 0; i < ss.Length; i++)
            {
                if (double.TryParse(ss[i], NumberStyles.Any, CultureInfo.InvariantCulture, out double v))
                    vals[k++] = v;
            }
            if (k == vals.Length) return vals;
            double[] trimmed = new double[k];
            Array.Copy(vals, trimmed, k);
            return trimmed;
        }
    }

    /// <summary>
    /// Tiny helper to enumerate VISA resources via Ivi.Visa.Interop.
    /// </summary>
    public static class VisaDiscovery
    {
        public static System.Collections.Generic.List<string> Find(string expression)
        {
            var results = new System.Collections.Generic.List<string>();
            ResourceManager rm = null;
            try
            {
                rm = new ResourceManager();
                object raw = rm.FindRsrc(expression);
                var arr = raw as Array;
                if (arr != null)
                {
                    foreach (var o in arr)
                    {
                        var s = Convert.ToString(o);
                        if (!string.IsNullOrWhiteSpace(s)) results.Add(s.Trim());
                    }
                }
                else
                {
                    string one = raw as string;
                    if (!string.IsNullOrWhiteSpace(one)) results.Add(one.Trim());
                }
            }
            catch { }
            finally
            {
                try { (rm as IDisposable)?.Dispose(); } catch { }
            }
            return results;
        }
    }

    public sealed class VisaSession : IDisposable
    {
        private readonly object _gate = new object();
        private ResourceManager _rm;
        private FormattedIO488 _io;
        private IMessage _session;
        private CommandLogger _log;
        private bool _isOpen;

        public int TimeoutMs { get; set; } = 2000;
        public int Retries { get; set; } = 2;

        public void Open(string resource, CommandLogger logger)
        {
            lock (_gate)
            {
                _log = logger;
                _rm = new ResourceManager();
                _session = (IMessage)_rm.Open(resource, AccessMode.NO_LOCK, TimeoutMs, "");
                _session.Timeout = TimeoutMs;
                _session.TerminationCharacterEnabled = true;
                _session.TerminationCharacter = 10; // '\n'
                try { _session.Clear(); } catch { }

                _io = new FormattedIO488 { IO = _session };
                _isOpen = true;
                _log?.Log("OPEN: " + resource);
            }
        }

        public System.Collections.Generic.List<string> DiscoverResources(string[] patterns)
        {
            var list = new System.Collections.Generic.List<string>();
            lock (_gate)
            {
                if (_rm == null) _rm = new ResourceManager();
            }
            foreach (var p in patterns)
            {
                try
                {
                    object result = _rm.FindRsrc(p);
                    if (result is object[] arr)
                        list.AddRange(arr.OfType<string>());
                    else if (result is Array a)
                        foreach (var o in a) if (o is string s) list.Add(s);
                            else if (result is string one) list.Add(one);
                }
                catch { /* ignore */ }
            }
            return list.Distinct().ToList();
        }

        public Task WriteAsync(string scpi)
        {
            return WithRetry(() =>
            {
                if (!_isOpen || _io == null) return (string)null;
                _log?.Log(">> " + scpi);
                _io.WriteString(scpi, true);
                return (string)null;
            });
        }

        public Task<string> QueryAsync(string scpi)
        {
            return WithRetry(() =>
            {
                if (!_isOpen || _io == null) return string.Empty;
                _log?.Log(">> " + scpi);
                _io.WriteString(scpi, true);
                string s = _io.ReadString();
                if (s != null) s = s.Trim();
                _log?.Log("<< " + s);
                return s ?? string.Empty;
            });
        }

        public async Task<double> QueryNumberAsync(string scpi)
        {
            string s = await QueryAsync(scpi);
            if (string.IsNullOrWhiteSpace(s)) return double.NaN;

            if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out double v)) return v;

            var tok = s.Split(',', ';');
            if (tok.Length > 0 && double.TryParse(tok[0], NumberStyles.Any, CultureInfo.InvariantCulture, out v)) return v;

            return double.NaN;
        }

        private Task<T> WithRetry<T>(Func<T> fn)
        {
            return Task.Run(() =>
            {
                int tries = 0;
                while (true)
                {
                    try
                    {
                        lock (_gate)
                        {
                            if (!_isOpen || _io == null) return default(T);
                            return fn();
                        }
                    }
                    catch (Exception ex)
                    {
                        tries++;
                        if (tries > Retries) return default(T);
                        if (_log != null) _log.Log("I/O retry " + tries + " after: " + ex.Message);
                        Thread.Sleep(50 * tries);
                    }
                }
            });
        }

        public void Close()
        {
            lock (_gate)
            {
                _isOpen = false;
                try { _log?.Log("CLOSE"); } catch { }
                try { _session?.Close(); } catch { }

                try
                {
                    if (_io != null && _io.IO != null)
                        Marshal.FinalReleaseComObject(_io.IO);
                }
                catch { }
                try { if (_session != null) Marshal.FinalReleaseComObject(_session); } catch { }
                try { if (_rm != null) Marshal.FinalReleaseComObject(_rm); } catch { }

                _io = null; _session = null; _rm = null;
            }
        }

        public void Dispose() { Close(); }
    }
}
