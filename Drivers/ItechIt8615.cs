using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HouseholdMS.Models;
using HouseholdMS.Services;

namespace HouseholdMS.Drivers
{
    public class ItechIt8615 : IInstrument
    {
        private readonly VisaSession _io;
        private readonly CommandLogger _log;
        private readonly CultureInfo _ci = CultureInfo.InvariantCulture;

        private double _iMin, _iMax, _rMin, _rMax, _vMin, _vMax, _pMin, _pMax, _pfMin, _pfMax, _cfMin, _cfMax;
        public string CurrentUnits { get; private set; } = "A";

        public ItechIt8615(VisaSession io, CommandLogger log) { _io = io; _log = log; }

        public Task<string> IdentifyAsync() { return _io.QueryAsync("*IDN?"); }

        public Task ToRemoteAsync() { return _io.WriteAsync("SYST:REM"); }
        public Task ToLocalAsync() { return _io.WriteAsync("SYST:LOC"); }

        public async Task DrainErrorQueueAsync()
        {
            for (int i = 0; i < 10; i++)
            {
                string e = await _io.QueryAsync("SYST:ERR?");
                _log.Log("ERRQ: " + e);
                if (e.StartsWith("0")) break;
            }
        }

        public Task SetAcDcAsync(bool ac) { return _io.WriteAsync("SYST:MODE " + (ac ? "AC" : "DC")); }

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

        public Task EnableInputAsync(bool on) { return _io.WriteAsync("INP:STAT " + (on ? "ON" : "OFF")); }

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

        public async Task<InstrumentReading> ReadAsync()
        {
            double vrms = await _io.QueryNumberAsync("MEAS:VOLT:RMS?");
            double irms = await _io.QueryNumberAsync("MEAS:CURR:RMS?");
            double pow = await _io.QueryNumberAsync("MEAS:POW?");
            double pf = await _io.QueryNumberAsync("MEAS:POW:PFAC?");
            double cf = await _io.QueryNumberAsync("MEAS:CURR:CFAC?");
            double f = await _io.QueryNumberAsync("MEAS:FREQ?");
            var r = new InstrumentReading();
            r.Timestamp = DateTime.UtcNow; r.Vrms = vrms; r.Irms = irms; r.Power = pow; r.Pf = pf; r.CrestFactor = cf; r.Freq = f;
            return r;
        }

        public async Task ScopeConfigureAsync(string source, string slope, double level)
        {
            await _io.WriteAsync("WAVE:TRIG:SOUR " + source);   // VOLTage|CURRent
            await _io.WriteAsync("WAVE:TRIG:SLOP " + slope);    // POSitive|NEGative|ANY
            if (source.StartsWith("V")) await _io.WriteAsync("WAVE:TRIG:VOLT:LEV " + level.ToString(_ci));
            else await _io.WriteAsync("WAVE:TRIG:CURR:LEV " + level.ToString(_ci));
        }
        public Task ScopeRunAsync() { return _io.WriteAsync("WAVE:RUN"); }
        public Task ScopeSingleAsync() { return _io.WriteAsync("WAVE:SING"); }
        public Task ScopeStopAsync() { return _io.WriteAsync("WAVE:STOP"); }

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
            string[] ss = s.Split(new char[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            double[] vals = new double[ss.Length];
            int k = 0;
            for (int i = 0; i < ss.Length; i++)
            {
                double v;
                if (double.TryParse(ss[i], NumberStyles.Any, CultureInfo.InvariantCulture, out v))
                {
                    vals[k++] = v;
                }
            }
            if (k == vals.Length) return vals;
            double[] trimmed = new double[k];
            Array.Copy(vals, trimmed, k);
            return trimmed;
        }
    }
}
