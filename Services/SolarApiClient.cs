using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;

namespace HouseholdMS
{
    public static class SolarApiClient
    {
        private static readonly HttpClient Http;

        // ---------- super light in-memory cache ----------
        private class CacheEntry { public DateTime Exp; public object Value; }
        private static readonly ConcurrentDictionary<string, CacheEntry> _cache = new ConcurrentDictionary<string, CacheEntry>();

        private static bool TryCacheGet<T>(string key, out T value)
        {
            value = default(T);
            CacheEntry ce;
            if (_cache.TryGetValue(key, out ce) && ce.Exp > DateTime.UtcNow)
            {
                value = (T)ce.Value;
                return true;
            }
            return false;
        }

        private static void CacheSet<T>(string key, T value, TimeSpan ttl)
        {
            _cache[key] = new CacheEntry { Exp = DateTime.UtcNow.Add(ttl), Value = value };
        }

        static SolarApiClient()
        {
            // NASA / PVGIS endpoints require TLS 1.2+
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            Http = new HttpClient(new HttpClientHandler());
            Http.Timeout = TimeSpan.FromSeconds(25);
            Http.DefaultRequestHeaders.Add("User-Agent", "HouseholdMS/1.0");
        }

        // =====================================================================
        //                             NASA POWER
        // =====================================================================

        /// <summary>Daily GHI (kWh/m²) for a single date.</summary>
        public static async Task<double?> GetNasaDailyGhiKwhm2Async(double lat, double lon, DateTime dayUtc)
        {
            string date = dayUtc.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            string url = string.Format(CultureInfo.InvariantCulture,
                "https://power.larc.nasa.gov/api/temporal/daily/point?parameters=ALLSKY_SFC_SW_DWN&community=RE&latitude={0}&longitude={1}&start={2}&end={2}&time-standard=UTC&format=JSON",
                lat, lon, date);

            string s = await Http.GetStringAsync(url);
            var root = Deserialize<NasaRoot>(s);
            if (root == null || root.properties == null || root.properties.parameter == null || root.properties.parameter.ALLSKY_SFC_SW_DWN == null)
                return null;

            double v;
            if (root.properties.parameter.ALLSKY_SFC_SW_DWN.TryGetValue(date, out v))
                return v;

            foreach (var kv in root.properties.parameter.ALLSKY_SFC_SW_DWN)
                return kv.Value;

            return null;
        }

        /// <summary>Sum hourly GHI values (kWh/m²) for the given day.</summary>
        public static async Task<double?> GetNasaHourlyGhiDailySumAsync(double lat, double lon, DateTime dayUtc)
        {
            string date = dayUtc.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            string url = string.Format(CultureInfo.InvariantCulture,
                "https://power.larc.nasa.gov/api/temporal/hourly/point?parameters=ALLSKY_SFC_SW_DWN&community=RE&latitude={0}&longitude={1}&start={2}&end={2}&time-standard=UTC&format=JSON",
                lat, lon, date);

            string s = await Http.GetStringAsync(url);
            var root = Deserialize<NasaRootHourly>(s);
            if (root == null || root.properties == null || root.properties.parameter == null || root.properties.parameter.ALLSKY_SFC_SW_DWN == null)
                return null;

            double sum = 0; int count = 0;
            foreach (var kv in root.properties.parameter.ALLSKY_SFC_SW_DWN)
            {
                if (!double.IsNaN(kv.Value) && kv.Value >= 0) { sum += kv.Value; count++; }
            }
            return count == 0 ? (double?)null : sum;
        }

        /// <summary>Daily GHI for a date range (single request).</summary>
        public static async Task<Dictionary<DateTime, double>> GetNasaDailyRangeAsync(double lat, double lon, DateTime startUtc, DateTime endUtc)
        {
            string start = startUtc.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            string end = endUtc.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            string url = string.Format(CultureInfo.InvariantCulture,
                "https://power.larc.nasa.gov/api/temporal/daily/point?parameters=ALLSKY_SFC_SW_DWN&community=RE&latitude={0}&longitude={1}&start={2}&end={3}&time-standard=UTC&format=JSON",
                lat, lon, start, end);

            string s = await Http.GetStringAsync(url);
            var root = Deserialize<NasaRoot>(s);

            var dict = new Dictionary<DateTime, double>();
            if (root?.properties?.parameter?.ALLSKY_SFC_SW_DWN != null)
            {
                foreach (var kv in root.properties.parameter.ALLSKY_SFC_SW_DWN)
                {
                    if (kv.Key.Length >= 8)
                    {
                        DateTime d;
                        if (DateTime.TryParseExact(kv.Key.Substring(0, 8), "yyyyMMdd",
                            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out d))
                        {
                            dict[d.Date] = kv.Value;
                        }
                    }
                }
            }
            return dict;
        }

        /// <summary>Hourly GHI -> daily sums for a date range (single request).</summary>
        public static async Task<Dictionary<DateTime, double>> GetNasaHourlyDailySumsRangeAsync(double lat, double lon, DateTime startUtc, DateTime endUtc)
        {
            string start = startUtc.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            string end = endUtc.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            string url = string.Format(CultureInfo.InvariantCulture,
                "https://power.larc.nasa.gov/api/temporal/hourly/point?parameters=ALLSKY_SFC_SW_DWN&community=RE&latitude={0}&longitude={1}&start={2}&end={3}&time-standard=UTC&format=JSON",
                lat, lon, start, end);

            string s = await Http.GetStringAsync(url);
            var root = Deserialize<NasaRootHourly>(s);

            var sums = new Dictionary<DateTime, double>();
            if (root?.properties?.parameter?.ALLSKY_SFC_SW_DWN != null)
            {
                foreach (var kv in root.properties.parameter.ALLSKY_SFC_SW_DWN)
                {
                    if (kv.Key.Length >= 8)
                    {
                        DateTime d;
                        if (DateTime.TryParseExact(kv.Key.Substring(0, 8), "yyyyMMdd",
                            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out d))
                        {
                            double cur;
                            sums.TryGetValue(d.Date, out cur);
                            sums[d.Date] = cur + kv.Value;
                        }
                    }
                }
            }
            return sums;
        }

        // =====================================================================
        //                             OPEN-METEO
        // =====================================================================

        public class DailyShortwave
        {
            public DateTime Date;
            public double Kwhm2;    // MJ/m² converted to kWh/m²
            public double? PeakWm2; // derived from hourly
        }

        /// <summary>Daily shortwave (converted to kWh/m²) + daily peak W/m² for a range.</summary>
        public static async Task<List<DailyShortwave>> GetOpenMeteoDailyShortwaveAsync(double lat, double lon, DateTime startUtc, DateTime endUtc)
        {
            string start = startUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string end = endUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            // daily MJ/m²
            string urlDaily = string.Format(CultureInfo.InvariantCulture,
                "https://api.open-meteo.com/v1/forecast?latitude={0}&longitude={1}&daily=shortwave_radiation_sum&timezone=UTC&start_date={2}&end_date={3}",
                lat, lon, start, end);

            string djson = await Http.GetStringAsync(urlDaily);
            var dailyRoot = Deserialize<OpenDailyRoot>(djson);

            var list = new List<DailyShortwave>();
            if (dailyRoot?.daily?.time != null && dailyRoot.daily.shortwave_radiation_sum != null)
            {
                int n = Math.Min(dailyRoot.daily.time.Length, dailyRoot.daily.shortwave_radiation_sum.Length);
                for (int i = 0; i < n; i++)
                {
                    DateTime dt;
                    if (!DateTime.TryParse(dailyRoot.daily.time[i], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out dt))
                        continue;

                    double kwh = dailyRoot.daily.shortwave_radiation_sum[i] * 0.27777778; // MJ/m² -> kWh/m²
                    list.Add(new DailyShortwave { Date = dt.Date, Kwhm2 = kwh, PeakWm2 = null });
                }
            }

            // hourly peaks W/m²
            string urlHourly = string.Format(CultureInfo.InvariantCulture,
                "https://api.open-meteo.com/v1/forecast?latitude={0}&longitude={1}&hourly=shortwave_radiation&timezone=UTC&start_date={2}&end_date={3}",
                lat, lon, start, end);
            string hjson = await Http.GetStringAsync(urlHourly);
            var hourlyRoot = Deserialize<OpenHourlyRoot>(hjson);

            if (hourlyRoot?.hourly?.time != null && hourlyRoot.hourly.shortwave_radiation != null)
            {
                var peaks = new Dictionary<DateTime, double>();
                int m = Math.Min(hourlyRoot.hourly.time.Length, hourlyRoot.hourly.shortwave_radiation.Length);
                for (int i = 0; i < m; i++)
                {
                    DateTime t;
                    if (!DateTime.TryParse(hourlyRoot.hourly.time[i], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out t))
                        continue;
                    double val = hourlyRoot.hourly.shortwave_radiation[i];
                    var key = t.Date;
                    double cur;
                    if (!peaks.TryGetValue(key, out cur) || val > cur) peaks[key] = val;
                }
                foreach (var d in list)
                {
                    double p;
                    if (peaks.TryGetValue(d.Date, out p)) d.PeakWm2 = p;
                }
            }

            return list;
        }

        /// <summary>Tomorrow peak shortwave (W/m²) using hourly.</summary>
        public static async Task<double?> GetOpenMeteoTomorrowPeakShortwaveAsync(double lat, double lon, DateTime tomorrowUtc)
        {
            string day = tomorrowUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string url = string.Format(CultureInfo.InvariantCulture,
                "https://api.open-meteo.com/v1/forecast?latitude={0}&longitude={1}&hourly=shortwave_radiation&timezone=UTC&start_date={2}&end_date={2}",
                lat, lon, day);

            string s = await Http.GetStringAsync(url);
            var root = Deserialize<OpenHourlyRoot>(s);
            if (root?.hourly?.shortwave_radiation == null || root.hourly.shortwave_radiation.Length == 0)
                return null;
            return root.hourly.shortwave_radiation.Max();
        }

        // =====================================================================
        //                       Aggregators & Convenience
        // =====================================================================

        public class IrradianceDay
        {
            public DateTime Date;
            public double? Kwhm2;
            public string Source;    // "NASA (daily)", "NASA (hourly)", "Open-Meteo", "—"
            public double? PeakWm2;  // used for both past and forecast (hourly max)
        }

        /// <summary>Cached today's GHI to avoid repeated calls on dashboard.</summary>
        public static async Task<double?> GetTodayGhiCachedAsync(double lat, double lon, DateTime todayUtc, TimeSpan ttl)
        {
            string key = $"ghi:{lat:F3}:{lon:F3}:{todayUtc:yyyyMMdd}";
            double? v;
            if (TryCacheGet<double?>(key, out v)) return v;

            v = await GetDailyGhiKwhm2WithFallbackAsync(lat, lon, todayUtc);
            CacheSet(key, v, ttl);
            return v;
        }

        /// <summary>One-day value with fallbacks (NASA daily → NASA hourly sum → Open-Meteo daily).</summary>
        public static async Task<double?> GetDailyGhiKwhm2WithFallbackAsync(double lat, double lon, DateTime dayUtc)
        {
            var v = await GetNasaDailyGhiKwhm2Async(lat, lon, dayUtc);
            if (v.HasValue) return v;

            v = await GetNasaHourlyGhiDailySumAsync(lat, lon, dayUtc);
            if (v.HasValue) return v;

            var om = await GetOpenMeteoDailyShortwaveAsync(lat, lon, dayUtc, dayUtc);
            return (om != null && om.Count == 1) ? (double?)om[0].Kwhm2 : null;
        }

        /// <summary>
        /// FAST: single Open-Meteo request for past-7 + next-3; hourly peaks for **all** days.
        /// Falls back to cache for 10 minutes.
        /// </summary>
        public static async Task<(List<IrradianceDay> Past7, List<IrradianceDay> Next3)>
            GetOpenMeteoPast7Next3Async(double lat, double lon, DateTime todayUtc)
        {
            // cache 10 minutes
            string key = $"om-fast:{lat:F3}:{lon:F3}:{todayUtc:yyyyMMdd}:withpeaks-all";
            (List<IrradianceDay> Past7, List<IrradianceDay> Next3) cached;
            if (TryCacheGet(key, out cached)) return cached;

            // One request: daily (past 7 + next 3) + hourly for the same window
            string url = string.Format(CultureInfo.InvariantCulture,
                "https://api.open-meteo.com/v1/forecast?latitude={0}&longitude={1}" +
                "&daily=shortwave_radiation_sum&hourly=shortwave_radiation&timezone=UTC" +
                "&past_days=7&forecast_days=3",
                lat, lon);

            string json = await Http.GetStringAsync(url);

            var dailyRoot = Deserialize<OpenDailyRoot>(json);
            var hourlyRoot = Deserialize<OpenHourlyRoot>(json);

            // Daily MJ/m² -> kWh/m² mapped by date
            var daily = new SortedDictionary<DateTime, double>();
            if (dailyRoot?.daily?.time != null && dailyRoot.daily.shortwave_radiation_sum != null)
            {
                int n = Math.Min(dailyRoot.daily.time.Length, dailyRoot.daily.shortwave_radiation_sum.Length);
                for (int i = 0; i < n; i++)
                {
                    DateTime d;
                    if (DateTime.TryParse(dailyRoot.daily.time[i], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out d))
                    {
                        daily[d.Date] = dailyRoot.daily.shortwave_radiation_sum[i] * 0.27777778; // MJ/m² -> kWh/m²
                    }
                }
            }

            // Hourly peaks (W/m²) for every date in the window
            var peaks = new Dictionary<DateTime, double>();
            if (hourlyRoot?.hourly?.time != null && hourlyRoot.hourly.shortwave_radiation != null)
            {
                int m = Math.Min(hourlyRoot.hourly.time.Length, hourlyRoot.hourly.shortwave_radiation.Length);
                for (int i = 0; i < m; i++)
                {
                    DateTime t;
                    if (DateTime.TryParse(hourlyRoot.hourly.time[i], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out t))
                    {
                        var d = t.Date;
                        double v = hourlyRoot.hourly.shortwave_radiation[i];
                        double cur;
                        if (!peaks.TryGetValue(d, out cur) || v > cur) peaks[d] = v;
                    }
                }
            }

            var past7 = new List<IrradianceDay>();
            var next3 = new List<IrradianceDay>();

            foreach (var kv in daily)
            {
                var item = new IrradianceDay
                {
                    Date = kv.Key,
                    Kwhm2 = kv.Value,
                    Source = "Open-Meteo"
                };
                double p;
                if (peaks.TryGetValue(kv.Key, out p)) item.PeakWm2 = p;

                if (kv.Key <= todayUtc) past7.Add(item); else next3.Add(item);
            }

            var result = (past7, next3);
            CacheSet(key, result, TimeSpan.FromMinutes(10));
            return result;
        }

        /// <summary>
        /// Precise/batched loader: NASA daily + hourly fallback + Open-Meteo fill, then OM forecast.
        /// </summary>
        public static async Task<(List<IrradianceDay> Past7, List<IrradianceDay> Next3)> GetIrradianceSummaryAsync(double lat, double lon, DateTime todayUtc)
        {
            var past = new List<IrradianceDay>();
            var next = new List<IrradianceDay>();

            // ---- PAST 7 ----
            var start = todayUtc.AddDays(-6);
            var end = todayUtc;

            var nasaDaily = await GetNasaDailyRangeAsync(lat, lon, start, end);
            var nasaHourly = new Dictionary<DateTime, double>();
            var needHourly = new List<DateTime>();

            for (int i = 0; i < 7; i++)
            {
                var d = start.AddDays(i).Date;
                double val;
                if (nasaDaily.TryGetValue(d, out val))
                {
                    past.Add(new IrradianceDay { Date = d, Kwhm2 = val, Source = "NASA (daily)" });
                }
                else
                {
                    past.Add(new IrradianceDay { Date = d, Kwhm2 = null, Source = null });
                    needHourly.Add(d);
                }
            }

            if (needHourly.Count > 0)
            {
                nasaHourly = await GetNasaHourlyDailySumsRangeAsync(lat, lon, start, end);
                foreach (var p in past)
                {
                    if (p.Kwhm2 == null)
                    {
                        double v;
                        if (nasaHourly.TryGetValue(p.Date, out v))
                        {
                            p.Kwhm2 = v; p.Source = "NASA (hourly)";
                        }
                    }
                }
            }

            // Open-Meteo fallback for any remaining nulls
            if (past.Any(x => x.Kwhm2 == null))
            {
                var om = await GetOpenMeteoDailyShortwaveAsync(lat, lon, start, end);
                var omDict = om?.ToDictionary(x => x.Date, x => x.Kwhm2) ?? new Dictionary<DateTime, double>();
                foreach (var p in past)
                {
                    if (p.Kwhm2 == null)
                    {
                        double v;
                        if (omDict.TryGetValue(p.Date, out v))
                        {
                            p.Kwhm2 = v; p.Source = "Open-Meteo";
                        }
                        else
                        {
                            p.Kwhm2 = null; p.Source = "—";
                        }
                    }
                }
            }

            // ---- NEXT 3 (Open-Meteo) ----
            var fc = await GetOpenMeteoDailyShortwaveAsync(lat, lon, todayUtc.AddDays(1), todayUtc.AddDays(3));
            if (fc != null)
            {
                foreach (var d in fc)
                {
                    next.Add(new IrradianceDay
                    {
                        Date = d.Date,
                        Kwhm2 = d.Kwhm2,
                        PeakWm2 = d.PeakWm2,
                        Source = "Open-Meteo"
                    });
                }
            }

            return (past, next);
        }

        // =====================================================================
        //                                 PVGIS
        // =====================================================================

        public class PvGisResult
        {
            public double[] Monthly;         // kWh (12)
            public string[] MonthNames;      // Jan..Dec
            public double AnnualKWh;         // kWh
            public double MonthlyAverageKWh; // kWh
        }

        /// <summary>PV baseline via PVGIS PVcalc.</summary>
        public static async Task<PvGisResult> GetPvGisPvcalcAsync(double lat, double lon, double peakKw, int tilt, int azimuth, double lossesPct)
        {
            string cacheKey = string.Format(CultureInfo.InvariantCulture,
                "pvgis:{0:F3}:{1:F3}:{2:F2}:{3}:{4}:{5:F1}", lat, lon, peakKw, tilt, azimuth, lossesPct);

            PvGisResult cached;
            if (TryCacheGet(cacheKey, out cached))
                return cached;

            string s = await Http.GetStringAsync(string.Format(CultureInfo.InvariantCulture,
                "https://re.jrc.ec.europa.eu/api/PVcalc?lat={0}&lon={1}&peakpower={2}&loss={3}&angle={4}&aspect={5}&outputformat=json",
                lat, lon,
                peakKw.ToString(CultureInfo.InvariantCulture),
                lossesPct.ToString(CultureInfo.InvariantCulture),
                tilt, azimuth));

            var root = Deserialize<PvRoot>(s);
            if (root == null || root.outputs == null)
                return null;

            double annual = double.NaN;
            if (root.outputs.totals != null)
            {
                if (root.outputs.totals.fixedTotals != null && !double.IsNaN(root.outputs.totals.fixedTotals.E_y))
                    annual = root.outputs.totals.fixedTotals.E_y;
                else if (!double.IsNaN(root.outputs.totals.E_y))
                    annual = root.outputs.totals.E_y;
            }

            double[] monthly = null;
            if (root.outputs.monthly != null)
            {
                if (root.outputs.monthly.fixedArray != null && root.outputs.monthly.fixedArray.Length == 12)
                    monthly = root.outputs.monthly.fixedArray.Select(m => m.E_m).ToArray();
                else if (root.outputs.monthly.E_m != null && root.outputs.monthly.E_m.Length == 12)
                    monthly = root.outputs.monthly.E_m;
            }
            if (monthly == null) monthly = new double[12];

            var names = new[] { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };
            double avg = monthly.Length > 0 ? monthly.Average() : double.NaN;

            var result = new PvGisResult
            {
                Monthly = monthly,
                MonthNames = names,
                AnnualKWh = annual,
                MonthlyAverageKWh = avg
            };

            CacheSet(cacheKey, result, TimeSpan.FromHours(12));
            return result;
        }

        // =====================================================================
        //                               DTOs / JSON
        // =====================================================================

        // NASA DAILY
        [DataContract] private class NasaRoot { [DataMember] public NasaProperties properties; }
        [DataContract] private class NasaProperties { [DataMember] public NasaParameter parameter; }
        [DataContract]
        private class NasaParameter
        {
            [DataMember(Name = "ALLSKY_SFC_SW_DWN")]
            public Dictionary<string, double> ALLSKY_SFC_SW_DWN;
        }

        // NASA HOURLY
        [DataContract] private class NasaRootHourly { [DataMember] public NasaPropertiesHourly properties; }
        [DataContract] private class NasaPropertiesHourly { [DataMember] public NasaParameterHourly parameter; }
        [DataContract]
        private class NasaParameterHourly
        {
            [DataMember(Name = "ALLSKY_SFC_SW_DWN")]
            public Dictionary<string, double> ALLSKY_SFC_SW_DWN;
        }

        // Open-Meteo
        [DataContract] private class OpenDailyRoot { [DataMember] public OpenDaily daily; }
        [DataContract]
        private class OpenDaily
        {
            [DataMember] public string[] time;
            [DataMember] public double[] shortwave_radiation_sum; // MJ/m²
        }
        [DataContract] private class OpenHourlyRoot { [DataMember] public OpenHourly hourly; }
        [DataContract]
        private class OpenHourly
        {
            [DataMember] public string[] time;
            [DataMember] public double[] shortwave_radiation; // W/m²
        }

        // PVGIS
        [DataContract] private class PvRoot { [DataMember] public PvOutputs outputs; }
        [DataContract]
        private class PvOutputs
        {
            [DataMember] public PvTotals totals;
            [DataMember] public PvMonthly monthly;
        }
        [DataContract]
        private class PvTotals
        {
            [DataMember(Name = "fixed")] public PvTotalsFixed fixedTotals;
            [DataMember] public double E_y;
        }
        [DataContract] private class PvTotalsFixed { [DataMember] public double E_y; }

        [DataContract]
        private class PvMonthly
        {
            [DataMember(Name = "fixed")] public PvMonthEntry[] fixedArray; // modern: 12 records
            [DataMember] public double[] E_m;                               // legacy fallback
        }
        [DataContract]
        private class PvMonthEntry
        {
            [DataMember] public int month;   // 1..12
            [DataMember] public double E_m;  // kWh
        }

        // generic JSON helper
        private static T Deserialize<T>(string json)
        {
            var ser = new DataContractJsonSerializer(typeof(T));
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                return (T)ser.ReadObject(ms);
            }
        }
    }
}
