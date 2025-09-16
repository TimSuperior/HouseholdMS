using System;
using System.Collections.Generic;
using Ivi.Visa.Interop;

namespace HouseholdMS.Services
{
    /// <summary>
    /// Tiny helper to enumerate VISA resources via Ivi.Visa.Interop without touching ScpiDeviceVisa.
    /// </summary>
    public static class VisaDiscovery
    {
        /// <summary>
        /// Find VISA resources matching an expression like "USB?*::0x2EC7::*::INSTR" or "USB?*::INSTR".
        /// </summary>
        public static List<string> Find(string expression)
        {
            var results = new List<string>();
            ResourceManager rm = null;
            try
            {
                rm = new ResourceManager();
                object raw = rm.FindRsrc(expression);
                // The COM interop returns a SAFEARRAY of BSTR. Handle defensively.
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
                    // Some VISA versions return a single string when exactly one match is found
                    string one = raw as string;
                    if (!string.IsNullOrWhiteSpace(one)) results.Add(one.Trim());
                }
            }
            catch
            {
                // swallow – scanning is best-effort; caller will decide what to do
            }
            finally
            {
                try { (rm as IDisposable)?.Dispose(); } catch { }
            }
            return results;
        }
    }
}
