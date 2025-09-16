using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HouseholdMS.Drivers;
using HouseholdMS.Models;

namespace HouseholdMS.Services
{
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
                if (st.DurationMs < 5) issues.Add("Step " + idx + ": duration too small.");
                if (st.AcDc != "AC" && st.AcDc != "DC") issues.Add("Step " + idx + ": AC/DC invalid.");
                if (st.Function != "CURR" && st.Function != "RES" && st.Function != "VOLT" && st.Function != "POW" && st.Function != "SHOR")
                    issues.Add("Step " + idx + ": function invalid.");
                if (st.Repeat < 1 || st.Repeat > 1000) issues.Add("Step " + idx + ": repeat out of range.");
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
                        log.Log("STEP " + st.Index + " rep " + (rep + 1) + ": " + st.AcDc + "/" + st.Function + " set=" + st.Setpoint + " PF=" + st.Pf + " CF=" + st.Cf + " note=" + st.Note);
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
