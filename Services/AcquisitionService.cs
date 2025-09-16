using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HouseholdMS.Drivers;
using HouseholdMS.Models;

namespace HouseholdMS.Services
{
    public class AcquisitionService
    {
        private readonly IInstrument _inst;
        private readonly CommandLogger _log;
        private readonly RingBuffer<InstrumentReading> _buffer = new RingBuffer<InstrumentReading>(60000);
        private CancellationTokenSource _cts;
        public event Action<InstrumentReading> OnReading;

        public AcquisitionService(IInstrument inst, CommandLogger log) { _inst = inst; _log = log; }

        public void Start(int hz = 10)
        {
            if (_cts != null) _cts.Cancel();
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;

            Task.Run(async () =>
            {
                int ms = 1000 / (hz < 1 ? 1 : hz);
                if (ms < 20) ms = 20;
                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        InstrumentReading r = await _inst.ReadAsync();
                        _buffer.Add(r);
                        if (OnReading != null) OnReading(r);
                    }
                    catch (Exception ex) { _log.Log("ACQ: " + ex.Message); }
                    await Task.Delay(ms, ct);
                }
            }, ct);
        }

        public void Stop() { if (_cts != null) _cts.Cancel(); }

        public void ExportCsv(string path)
        {
            using (var w = new StreamWriter(path))
            {
                w.WriteLine("timestamp,vrms,irms,power,pf,cf,freq");
                var arr = _buffer.Snapshot();
                for (int i = 0; i < arr.Length; i++)
                {
                    var r = arr[i];
                    w.WriteLine(r.Timestamp.ToString("o") + "," + r.Vrms + "," + r.Irms + "," + r.Power + "," + r.Pf + "," + r.CrestFactor + "," + r.Freq);
                }
            }
        }
    }
}
