using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace HouseholdMS.View.UserControls
{
    // ---------- Embedded lightweight HTTP helper (no separate file) ----------
    internal static class HttpHelper
    {
        public static readonly HttpClient Http = new HttpClient(
            new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            })
        {
            Timeout = Timeout.InfiniteTimeSpan
        };

        static HttpHelper()
        {
            Http.DefaultRequestHeaders.UserAgent.ParseAdd("HouseholdMS/1.0 (+yr-meteogram)");
        }

        public static async Task<string> GetStringWithTimeoutAsync(
            string url, TimeSpan timeout, CancellationToken ct, string userAgentOverride = null)
        {
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                linked.CancelAfter(timeout);
                using (var req = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    if (!string.IsNullOrEmpty(userAgentOverride))
                    {
                        req.Headers.UserAgent.Clear();
                        req.Headers.TryAddWithoutValidation("User-Agent", userAgentOverride);
                    }

                    using (var resp = await Http.SendAsync(
                        req, HttpCompletionOption.ResponseHeadersRead, linked.Token).ConfigureAwait(false))
                    {
                        resp.EnsureSuccessStatusCode();
                        return await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    }
                }
            }
        }

        public static async Task<T> RetryAsync<T>(
            Func<CancellationToken, Task<T>> op,
            int retries, TimeSpan initialDelay, CancellationToken ct)
        {
            var delay = initialDelay;
            for (int attempt = 0; ; attempt++)
            {
                try { return await op(ct).ConfigureAwait(false); }
                catch (TaskCanceledException) when (!ct.IsCancellationRequested && attempt < retries)
                {
                    await Task.Delay(delay, ct).ConfigureAwait(false);
                    delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, 4000));
                }
                catch (HttpRequestException) when (attempt < retries)
                {
                    await Task.Delay(delay, ct).ConfigureAwait(false);
                    delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, 4000));
                }
            }
        }
    }

    public partial class YrMeteogramWindow : Window
    {
        private readonly string _locationId;
        private readonly string _lang;
        private Microsoft.Web.WebView2.Wpf.WebView2 _web;

        private List<HourlyPoint> _hourly = new List<HourlyPoint>();
        private List<DailySummary> _daily = new List<DailySummary>();

        private CancellationTokenSource _loadCts;
        private bool _retryAfterReset = false;

        // make SVG fill window better
        private const double DefaultZoom = 1.35;

        public YrMeteogramWindow(string locationId, string lang)
        {
            InitializeComponent();
            _locationId = locationId;
            _lang = string.IsNullOrWhiteSpace(lang) ? "en" : lang.Trim().ToLowerInvariant();
            Loaded += YrMeteogramWindow_Loaded;
            SizeChanged += (_, __) => ApplyZoom();
            Closed += (_, __) => _loadCts?.Cancel();
        }

        // ---------------------- WebView2 init ----------------------
        private async void YrMeteogramWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                LblStatus.Text = "Initializing WebView2…";

                _web = new Microsoft.Web.WebView2.Wpf.WebView2
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch
                };
                _web.CoreWebView2InitializationCompleted += Web_CoreWebView2InitializationCompleted;
                _web.NavigationCompleted += Web_NavigationCompleted;

                WebHost.Children.Clear();   // <-- FIXED: method call, not a property
                WebHost.Children.Add(_web);

                await InitializeWebView2Async();
                ApplyZoom();
                Navigate();

                _ = LoadForecastTablesAsync();
            }
            catch (Exception ex)
            {
                Fallback(true, "WebView2 initialization failed: " + ex.Message);
            }
        }

        private void ApplyZoom()
        {
            try
            {
                if (_web != null) _web.ZoomFactor = DefaultZoom;
            }
            catch { }
        }

        private async void InjectScaleScriptFallback()
        {
            try
            {
                if (_web?.CoreWebView2 == null) return;
                string scale = (DefaultZoom * 100).ToString("0", CultureInfo.InvariantCulture);
                string script =
                    @"(function(){try{var s=document.querySelector('svg');" +
                    $"if(s){{s.style.transformOrigin='0 0';s.style.transform='scale({DefaultZoom.ToString(CultureInfo.InvariantCulture)})';}}" +
                    $"else{{document.body.style.zoom='{scale}%';}}}})();";
                await _web.CoreWebView2.ExecuteScriptAsync(script);
            }
            catch { }
        }

        private static string GetUserDataFolder()
        {
            string baseDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "HouseholdMS", "WebView2");
            Directory.CreateDirectory(baseDir);
            return baseDir;
        }

        private async Task InitializeWebView2Async()
        {
            string ver = null;
            try { ver = CoreWebView2Environment.GetAvailableBrowserVersionString(); } catch { }
            if (string.IsNullOrEmpty(ver))
            {
                Fallback(true, "WebView2 Runtime is not installed.");
                return;
            }

            try
            {
                var options = new CoreWebView2EnvironmentOptions("--disable-gpu");
                var env = await CoreWebView2Environment.CreateAsync(null, GetUserDataFolder(), options);
                await _web.EnsureCoreWebView2Async(env);
                _web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                _web.CoreWebView2.Settings.AreDevToolsEnabled = false;
                _web.CoreWebView2.Settings.IsStatusBarEnabled = false;
                ApplyZoom();
            }
            catch (Exception ex)
            {
                if (!_retryAfterReset)
                {
                    _retryAfterReset = true;
                    try { Directory.Delete(GetUserDataFolder(), true); } catch { }
                    await InitializeWebView2Async();
                }
                else
                {
                    Fallback(true, "WebView2 could not initialize. " + ex.Message);
                }
            }
        }

        private string BuildUrl() => $"https://www.yr.no/{_lang}/content/{_locationId}/meteogram.svg";

        private void Navigate()
        {
            try
            {
                string url = BuildUrl();
                if (_web?.CoreWebView2 != null) _web.CoreWebView2.Navigate(url);
                else _web.Source = new Uri(url);
                Fallback(false, null);
                LblStatus.Text = "Loading…";
            }
            catch (Exception ex)
            {
                Fallback(true, "Navigation error: " + ex.Message);
            }
        }

        private void Web_CoreWebView2InitializationCompleted(object sender, CoreWebView2InitializationCompletedEventArgs e)
        {
            if (!e.IsSuccess) { Fallback(true, "WebView2 could not initialize."); return; }
            ApplyZoom();
        }

        private void Web_NavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            LblStatus.Text = e.IsSuccess ? "Meteogram ready." : ("Failed to load. " + e.WebErrorStatus);
            if (!e.IsSuccess) Fallback(true, "Failed to load SVG. ErrorStatus=" + e.WebErrorStatus);
            ApplyZoom();
            InjectScaleScriptFallback();
        }

        private void Fallback(bool show, string error)
        {
            FallbackPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            TxtError.Text = show ? (error ?? "") : "";
        }

        private void BtnReload_Click(object sender, RoutedEventArgs e)
        {
            Navigate();
            _ = LoadForecastTablesAsync(true);
        }

        // ---------------------- Extended data ----------------------

        private sealed class HourlyPoint
        {
            public DateTime TimeUtc { get; set; }
            public DateTime TimeLocal => TimeUtc.ToLocalTime();
            public string Symbol { get; set; }
            public double Temp { get; set; }
            public double Wind { get; set; }
            public double Gust { get; set; }
            public double Humidity { get; set; }
            public double Pressure { get; set; }
            public double Cloud { get; set; }
            public double WindDir { get; set; }
            public double Precip { get; set; }
        }

        private sealed class DailySummary
        {
            public DateTime Date { get; set; }
            public double MinTemp { get; set; }
            public double MaxTemp { get; set; }
            public double TotalPrecip { get; set; }
            public double MaxWind { get; set; }
            public double MaxGust { get; set; }
        }

        private static DateTime ParseIsoUtc(string s) =>
            DateTime.Parse(s, null, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal).ToUniversalTime();

        private async Task LoadForecastTablesAsync(bool userInitiated = false)
        {
            _loadCts?.Cancel();
            _loadCts = new CancellationTokenSource();
            var ct = _loadCts.Token;

            try
            {
                LblStatus.Text = userInitiated ? "Refreshing forecast…" : "Loading detailed forecast…";

                // Resolve lat/lon with retries
                var latlon = await HttpHelper.RetryAsync(
                    c => TryResolveLatLonAsync(_locationId, c),
                    retries: 2, initialDelay: TimeSpan.FromMilliseconds(500), ct);

                if (latlon == null)
                {
                    LblStatus.Text = "Could not resolve coordinates for detailed data.";
                    return;
                }

                var hourly = new List<HourlyPoint>();
                var daily = new List<DailySummary>();

                await HttpHelper.RetryAsync<object>(async c =>
                {
                    await FetchLocationForecastAsync(latlon.Item1, latlon.Item2, hourly, daily, c);
                    return null;
                }, retries: 2, initialDelay: TimeSpan.FromMilliseconds(500), ct);

                _hourly = hourly;
                _daily = daily;
                HourlyGrid.ItemsSource = _hourly;
                DailyGrid.ItemsSource = _daily;

                HourlyEmpty.Visibility = _hourly.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                DailyEmpty.Visibility = _daily.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

                LblStatus.Text = string.Format(CultureInfo.InvariantCulture,
                    "Detailed forecast ready (lat {0:0.####}, lon {1:0.####}).", latlon.Item1, latlon.Item2);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                LblStatus.Text = "Canceled.";
            }
            catch (TaskCanceledException)
            {
                LblStatus.Text = "Network timeout. Showing last data.";
            }
            catch (Exception ex)
            {
                LblStatus.Text = "Summary load failed: " + ex.Message;
            }
        }

        // Resolve lat/lon robustly (per-request UA; extra SVG regex fallback)
        private static async Task<Tuple<double, double>> TryResolveLatLonAsync(string locationId, CancellationToken ct)
        {
            // 1) Official yr.no location endpoint
            try
            {
                string url = "https://www.yr.no/api/v0/locations/" + locationId;
                string json = await HttpHelper.GetStringWithTimeoutAsync(
                    url, TimeSpan.FromSeconds(25), ct, "HouseholdMS/1.0 (+yr-meteogram)");
                using (var doc = JsonDocument.Parse(json))
                {
                    if (doc.RootElement.TryGetProperty("geometry", out var geom))
                    {
                        if (geom.TryGetProperty("coordinates", out var coords) &&
                            coords.ValueKind == JsonValueKind.Array && coords.GetArrayLength() >= 2)
                        {
                            double lon = coords[0].GetDouble();
                            double lat = coords[1].GetDouble();
                            return Tuple.Create(lat, lon);
                        }
                        if (geom.TryGetProperty("lat", out var latEl) &&
                            geom.TryGetProperty("lon", out var lonEl))
                        {
                            return Tuple.Create(latEl.GetDouble(), lonEl.GetDouble());
                        }
                    }
                }
            }
            catch { }

            // 2) HTML content page (JSON-LD)
            try
            {
                string htmlUrl = "https://www.yr.no/en/content/" + locationId + "/";
                string html = await HttpHelper.GetStringWithTimeoutAsync(
                    htmlUrl, TimeSpan.FromSeconds(25), ct,
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124 Safari/537.36");

                var m = Regex.Match(html,
                    @"(?:""latitude""\s*:\s*(?<lat>-?\d+(?:\.\d+)?).*?""longitude""\s*:\s*(?<lon>-?\d+(?:\.\d+)?))|(?:""lat""\s*:\s*(?<lat>-?\d+(?:\.\d+)?).*?""lon""\s*:\s*(?<lon>-?\d+(?:\.\d+)?))",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline);

                if (m.Success)
                {
                    double lat = double.Parse(m.Groups["lat"].Value, CultureInfo.InvariantCulture);
                    double lon = double.Parse(m.Groups["lon"].Value, CultureInfo.InvariantCulture);
                    return Tuple.Create(lat, lon);
                }

                var m2 = Regex.Match(html,
                    @"data-lat\s*=\s*""(?<lat>-?\d+(?:\.\d+)?)"".*?data-lon\s*=\s*""(?<lon>-?\d+(?:\.\d+)?)""",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (m2.Success)
                {
                    double lat = double.Parse(m2.Groups["lat"].Value, CultureInfo.InvariantCulture);
                    double lon = double.Parse(m2.Groups["lon"].Value, CultureInfo.InvariantCulture);
                    return Tuple.Create(lat, lon);
                }
            }
            catch { }

            // 3) SVG -> name -> Open-Meteo geocoder
            try
            {
                string svg = await HttpHelper.GetStringWithTimeoutAsync(
                    "https://www.yr.no/en/content/" + locationId + "/meteogram.svg",
                    TimeSpan.FromSeconds(25), ct,
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124 Safari/537.36");

                // direct lat/lon in SVG (extra fallback)
                var mSvg = Regex.Match(svg,
                    @"(?:latitude|lat)""?\s*[:=]\s*""?(?<lat>-?\d+(?:\.\d+)?).*?(?:longitude|lon)""?\s*[:=]\s*""?(?<lon>-?\d+(?:\.\d+)?)",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (mSvg.Success)
                {
                    double lat = double.Parse(mSvg.Groups["lat"].Value, CultureInfo.InvariantCulture);
                    double lon = double.Parse(mSvg.Groups["lon"].Value, CultureInfo.InvariantCulture);
                    return Tuple.Create(lat, lon);
                }

                string name = null;
                var mm = Regex.Match(svg, @"Weather\s+forecast\s+for\s*(?<name>[^<\r\n]+)", RegexOptions.IgnoreCase);
                if (mm.Success) name = mm.Groups["name"].Value.Trim();

                if (!string.IsNullOrWhiteSpace(name))
                {
                    string cleaned = Regex.Replace(name,
                        @"\b(Department|Province|Region|County|State|Oblast|District|Prefecture)\b",
                        "", RegexOptions.IgnoreCase).Trim();

                    foreach (var q in new[] { cleaned, cleaned.Split(',')[0], cleaned.Split(' ')[0] })
                    {
                        if (string.IsNullOrWhiteSpace(q)) continue;

                        string url = "https://geocoding-api.open-meteo.com/v1/search?count=1&language=en&format=json&name=" +
                                     Uri.EscapeDataString(q);
                        string json = await HttpHelper.GetStringWithTimeoutAsync(
                            url, TimeSpan.FromSeconds(20), ct,
                            "HouseholdMS/1.0 (+yr-meteogram)");

                        using (var doc = JsonDocument.Parse(json))
                        {
                            if (doc.RootElement.TryGetProperty("results", out var results) &&
                                results.ValueKind == JsonValueKind.Array && results.GetArrayLength() > 0)
                            {
                                var first = results[0];
                                double lat = first.GetProperty("latitude").GetDouble();
                                double lon = first.GetProperty("longitude").GetDouble();
                                return Tuple.Create(lat, lon);
                            }
                        }
                    }
                }
            }
            catch { }

            return null;
        }

        private static async Task FetchLocationForecastAsync(
            double lat, double lon, List<HourlyPoint> hourly, List<DailySummary> daily, CancellationToken ct)
        {
            string url = "https://api.met.no/weatherapi/locationforecast/2.0/compact?lat=" +
                         lat.ToString(CultureInfo.InvariantCulture) + "&lon=" +
                         lon.ToString(CultureInfo.InvariantCulture);

            string json = await HttpHelper.GetStringWithTimeoutAsync(
                url, TimeSpan.FromSeconds(25), ct, "HouseholdMS Meteogram/1.0 (contact: app@example.local)");

            using (var doc = JsonDocument.Parse(json))
            {
                var props = doc.RootElement.GetProperty("properties");
                var ts = props.GetProperty("timeseries");
                if (ts.ValueKind != JsonValueKind.Array || ts.GetArrayLength() == 0) return;

                int count = Math.Min(72, ts.GetArrayLength());
                for (int i = 0; i < count; i++)
                {
                    var item = ts[i];
                    DateTime t = ParseIsoUtc(item.GetProperty("time").GetString());
                    var data = item.GetProperty("data");
                    var inst = data.GetProperty("instant").GetProperty("details");

                    double temp = GetDouble(inst, "air_temperature");
                    double wind = GetDouble(inst, "wind_speed");
                    double gust = GetDouble(inst, "wind_speed_of_gust");
                    double hum = GetDouble(inst, "relative_humidity");
                    double pres = GetDouble(inst, "air_pressure_at_sea_level");
                    double cloud = GetDouble(inst, "cloud_area_fraction");
                    double wdir = GetDouble(inst, "wind_from_direction");

                    double precip = 0;
                    string symbol = null;

                    if (data.TryGetProperty("next_1_hours", out var n1h))
                    {
                        if (n1h.TryGetProperty("details", out var det) && det.TryGetProperty("precipitation_amount", out var p1))
                            precip = TryGetDouble(p1);
                        if (n1h.TryGetProperty("summary", out var sum) && sum.TryGetProperty("symbol_code", out var sc))
                            symbol = sc.GetString();
                    }
                    else if (data.TryGetProperty("next_6_hours", out var n6h))
                    {
                        if (n6h.TryGetProperty("details", out var det6) && det6.TryGetProperty("precipitation_amount", out var p6))
                            precip = TryGetDouble(p6);
                        if (n6h.TryGetProperty("summary", out var sum6) && sum6.TryGetProperty("symbol_code", out var sc6))
                            symbol = sc6.GetString();
                    }

                    hourly.Add(new HourlyPoint
                    {
                        TimeUtc = t,
                        Symbol = symbol ?? "",
                        Temp = temp,
                        Wind = wind,
                        Gust = gust,
                        Humidity = hum,
                        Pressure = pres,
                        Cloud = cloud,
                        WindDir = wdir,
                        Precip = precip
                    });
                }

                var map = new Dictionary<DateTime, DailySummary>();
                foreach (var h in hourly)
                {
                    DateTime day = h.TimeLocal.Date;
                    if (!map.TryGetValue(day, out var ds))
                    {
                        ds = new DailySummary
                        {
                            Date = day,
                            MinTemp = double.PositiveInfinity,
                            MaxTemp = double.NegativeInfinity,
                            TotalPrecip = 0,
                            MaxWind = 0,
                            MaxGust = 0
                        };
                        map[day] = ds;
                    }
                    if (h.Temp < ds.MinTemp) ds.MinTemp = h.Temp;
                    if (h.Temp > ds.MaxTemp) ds.MaxTemp = h.Temp;
                    ds.TotalPrecip += h.Precip;
                    if (h.Wind > ds.MaxWind) ds.MaxWind = h.Wind;
                    if (h.Gust > ds.MaxGust) ds.MaxGust = h.Gust;
                }

                daily.AddRange(map.Values);
                daily.Sort((a, b) => a.Date.CompareTo(b.Date));
            }
        }

        private static double GetDouble(JsonElement obj, string property) =>
            obj.TryGetProperty(property, out var v) ? TryGetDouble(v) : 0.0;

        private static double TryGetDouble(JsonElement v)
        {
            try { return v.GetDouble(); } catch { return 0.0; }
        }
    }
}
