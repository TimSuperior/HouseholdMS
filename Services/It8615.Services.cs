using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HouseholdMS.Drivers;
using HouseholdMS.Models;
using Syncfusion.Drawing;
using Syncfusion.Pdf;
using Syncfusion.Pdf.Graphics;

namespace HouseholdMS.Services
{
    public class AcquisitionService
    {
        private readonly IInstrument _inst;
        private readonly CommandLogger _log; // optional
        private readonly RingBuffer<InstrumentReading> _buffer = new RingBuffer<InstrumentReading>(60000);
        private CancellationTokenSource _cts;
        private Task _runner;
        public event Action<InstrumentReading> OnReading;

        // No-logger convenience ctor
        public AcquisitionService(IInstrument inst) : this(inst, null) { }

        public AcquisitionService(IInstrument inst, CommandLogger log)
        {
            _inst = inst;
            _log = log; // may be null
        }

        public void Start(int hz = 10)
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;

            _runner = Task.Run(async () =>
            {
                int ms = 1000 / (hz < 1 ? 1 : hz);
                if (ms < 20) ms = 20;

                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        InstrumentReading r = await _inst.ReadAsync();
                        _buffer.Add(r);
                        var handler = OnReading; // avoid race on multicast invocation
                        if (handler != null) handler(r);
                    }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex) { _log?.Log("ACQ: " + ex.Message); }
                    try { await Task.Delay(ms, ct); } catch (OperationCanceledException) { break; }
                }
            }, ct);
        }

        public void Stop()
        {
            try
            {
                _cts?.Cancel();
                var t = _runner;
                if (t != null) Task.WaitAny(new[] { t }, 500);
            }
            catch { }
        }

        public void ExportCsv(string path)
        {
            using (var w = new StreamWriter(path))
            {
                w.WriteLine("timestamp,vrms,irms,power,pf,cf,freq");
                var arr = _buffer.Snapshot();
                for (int i = 0; i < arr.Length; i++)
                {
                    var r = arr[i];
                    w.WriteLine($"{r.Timestamp:o},{r.Vrms},{r.Irms},{r.Power},{r.Pf},{r.CrestFactor},{r.Freq}");
                }
            }
        }
    }

    public class CommandLogger
    {
        public string LogDirectory { get; private set; }
        private readonly string _path;
        public event Action<string> OnLog;

        public CommandLogger()
        {
            LogDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "IT8615Logs");
            Directory.CreateDirectory(LogDirectory);
            _path = Path.Combine(LogDirectory, "log_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
        }

        public void Log(string line)
        {
            string s = DateTime.Now.ToString("HH:mm:ss.fff") + " " + line;
            File.AppendAllText(_path, s + Environment.NewLine);
            var handler = OnLog; if (handler != null) handler(s);
        }
    }

    public static class ReportBuilderPdf
    {
        public static void WriteSimplePdf(string path, string title, Tuple<string, string>[] kv)
        {
            using (var doc = new PdfDocument())
            {
                PdfPage page = doc.Pages.Add();
                PdfGraphics g = page.Graphics;

                PdfFont titleFont = new PdfStandardFont(PdfFontFamily.Helvetica, 16, PdfFontStyle.Bold);
                PdfFont textFont = new PdfStandardFont(PdfFontFamily.Helvetica, 11);

                g.DrawString(title, titleFont, PdfBrushes.Black, new PointF(0, 0));

                float y = 30f;
                for (int i = 0; i < kv.Length; i++)
                {
                    g.DrawString(kv[i].Item1 + ":", textFont, PdfBrushes.DarkBlue, new PointF(0, y));
                    g.DrawString(kv[i].Item2, textFont, PdfBrushes.Black, new PointF(150, y));
                    y += 18f;
                }

                using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                    doc.Save(fs);
            }
        }
    }

    public class RingBuffer<T>
    {
        private readonly T[] _buf;
        private int _head, _count;
        public RingBuffer(int capacity) { _buf = new T[capacity]; }
        public int Count => _count;
        public void Add(T item)
        {
            _buf[_head] = item;
            _head = (_head + 1) % _buf.Length;
            if (_count < _buf.Length) _count++;
        }
        public T[] Snapshot()
        {
            T[] arr = new T[_count];
            int idx = (_head - _count + _buf.Length) % _buf.Length;
            for (int i = 0; i < _count; i++) arr[i] = _buf[(idx + i) % _buf.Length];
            return arr;
        }
        public void Clear() { _head = 0; _count = 0; }
    }
}
