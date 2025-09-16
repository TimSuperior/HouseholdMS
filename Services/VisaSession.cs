using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Ivi.Visa.Interop;

namespace HouseholdMS.Services
{
    public sealed class VisaSession : IDisposable
    {
        private ResourceManager _rm;
        private FormattedIO488 _io;
        private IMessage _session;
        private CommandLogger _log;

        public int TimeoutMs { get; set; } = 2000;
        public int Retries { get; set; } = 2;

        public void Open(string resource, CommandLogger logger)
        {
            _log = logger;
            _rm = new ResourceManager();
            _session = (IMessage)_rm.Open(resource, AccessMode.NO_LOCK, TimeoutMs, "");
            _session.Timeout = TimeoutMs;

            _io = new FormattedIO488();
            _io.IO = _session;

            _log.Log("OPEN: " + resource);
        }

        public List<string> DiscoverResources(string[] patterns)
        {
            var list = new List<string>();
            if (_rm == null) _rm = new ResourceManager();
            foreach (var p in patterns)
            {
                try
                {
                    object result = _rm.FindRsrc(p);
                    var arr = (object[])result;
                    foreach (var x in arr) list.Add((string)x);
                }
                catch { /* ignore */ }
            }
            return list.Distinct().ToList();
        }

        public Task WriteAsync(string scpi)
        {
            return WithRetry(new Func<string>(() =>
            {
                _log?.Log(">> " + scpi);
                _io.WriteString(scpi, true);
                return null;
            }));
        }

        public Task<string> QueryAsync(string scpi)
        {
            return WithRetry(new Func<string>(() =>
            {
                _log?.Log(">> " + scpi);
                _io.WriteString(scpi, true);
                string s = _io.ReadString();
                if (s != null) s = s.Trim();
                _log?.Log("<< " + s);
                return s;
            }));
        }

        public async Task<double> QueryNumberAsync(string scpi)
        {
            string s = await QueryAsync(scpi);
            double v;
            if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out v)) return v;
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
                        tries++;
                        return fn();
                    }
                    catch (Exception ex)
                    {
                        if (tries > Retries) throw;
                        if (_log != null) _log.Log("I/O retry " + tries + " after: " + ex.Message);
                        System.Threading.Thread.Sleep(50 * tries);
                    }
                }
            });
        }

        public void Close()
        {
            try { _log?.Log("CLOSE"); } catch { }
            try { if (_session != null) _session.Close(); } catch { }
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

        public void Dispose() { Close(); }
    }
}
