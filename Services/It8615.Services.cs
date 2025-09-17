using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HouseholdMS.Drivers;
using HouseholdMS.Models;
using Newtonsoft.Json;
using Syncfusion.Pdf;
using Syncfusion.Pdf.Graphics;
using Syncfusion.Drawing;

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
            _cts?.Cancel();
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
                        OnReading?.Invoke(r);
                    }
                    catch (Exception ex) { _log.Log("ACQ: " + ex.Message); }
                    await Task.Delay(ms, ct);
                }
            }, ct);
        }

        public void Stop() { _cts?.Cancel(); }

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
            OnLog?.Invoke(s);
        }
    }

    public static class JsonFileStore
    {
        public static void Save<T>(string path, T obj)
        {
            File.WriteAllText(path, JsonConvert.SerializeObject(obj, Formatting.Indented));
        }
        public static T Load<T>(string path)
        {
            return JsonConvert.DeserializeObject<T>(File.ReadAllText(path));
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
                {
                    doc.Save(fs);
                }
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

    public static class SequenceRunner
    {
        public static List<string> Preflight(List<SequenceStep> steps, IInstrument it)
        {
            var issues = new List<string>();
            if (steps == null || steps.Count == 0) issues.Add("No steps.");
            for (int i = 0; i < steps.Count; i++)
            {
                var st = steps[i];
                int idx = i + 1;
                if (st.DurationMs < 5) issues.Add($"Step {idx}: duration too small.");
                if (st.AcDc != "AC" && st.AcDc != "DC") issues.Add($"Step {idx}: AC/DC invalid.");
                if (st.Function != "CURR" && st.Function != "RES" && st.Function != "VOLT" && st.Function != "POW" && st.Function != "SHOR")
                    issues.Add($"Step {idx}: function invalid.");
                if (st.Repeat < 1 || st.Repeat > 1000) issues.Add($"Step {idx}: repeat out of range.");
            }
            return issues;
        }

        public static async Task RunAsync(List<SequenceStep> steps, IInstrument it, CommandLogger log, bool loop, CancellationToken ct)
        {
            if (it == null) throw new InvalidOperationException("Instrument not connected.");

            var issues = Preflight(steps, it);
            if (issues.Count > 0) throw new InvalidOperationException("Preflight failed: " + string.Join("; ", issues));

            log.Log("SEQ start: " + steps.Count + " steps, loop=" + loop);
            do
            {
                for (int s = 0; s < steps.Count; s++)
                {
                    var st = steps[s];
                    for (int rep = 0; rep < st.Repeat; rep++)
                    {
                        ct.ThrowIfCancellationRequested();
                        await it.SetAcDcAsync(st.AcDc == "AC");
                        await it.SetFunctionAsync(st.Function);
                        await it.SetPfCfAsync(st.Pf, st.Cf);
                        await it.SetSetpointAsync(st.Setpoint);
                        await it.EnableInputAsync(true);
                        log.Log($"STEP {st.Index} rep {rep + 1}: {st.AcDc}/{st.Function} set={st.Setpoint} PF={st.Pf} CF={st.Cf} note={st.Note}");
                        await Task.Delay(st.DurationMs, ct);
                    }
                }
            }
            while (loop && !ct.IsCancellationRequested);

            await it.EnableInputAsync(false);
            log.Log("SEQ done.");
        }
    }
}
