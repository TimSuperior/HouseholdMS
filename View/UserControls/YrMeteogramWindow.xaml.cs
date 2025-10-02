using System;
using System.Collections.Generic;
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
    // ---------- Lightweight HTTP helper ----------
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
            Http.DefaultRequestHeaders.Accept.ParseAdd("*/*");
            Http.DefaultRequestHeaders.ExpectContinue = false;
        }

        public static async Task<string> GetStringWithTimeoutAsync(
            string url, TimeSpan timeout, CancellationToken ct,
            string userAgentOverride = null, string acceptLang = "en;q=0.9,*;q=0.8")
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
                    if (!string.IsNullOrEmpty(acceptLang))
                        req.Headers.TryAddWithoutValidation("Accept-Language", acceptLang);

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
            Exception last = null;

            for (int attempt = 0; attempt <= retries; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                try { return await op(ct).ConfigureAwait(false); }
                catch (TaskCanceledException ex) when (!ct.IsCancellationRequested) { last = ex; }
                catch (HttpRequestException ex) { last = ex; }

                if (attempt == retries) break;
                await Task.Delay(delay, ct).ConfigureAwait(false);
                delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, 5000));
            }

            throw last ?? new Exception("Operation failed after retries.");
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

        private const double DefaultZoom = 1.35;

        public YrMeteogramWindow(string locationId, string lang)
        {
            InitializeComponent();
            _locationId = locationId ?? "";
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

                WebHost.Children.Clear();
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
            try { if (_web != null) _web.ZoomFactor = DefaultZoom; } catch { }
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

        // ---------- Language-specific path handling ----------
        private static string ContentSegmentFor(string lang)
        {
            switch ((lang ?? "en").ToLowerInvariant())
            {
                case "nb": return "innhold";    // Bokmål
                case "nn": return "innhald";    // Nynorsk
                case "sme": return "sisdoallu"; // Northern Sami
                default: return "content";      // English + default
            }
        }

        private static string ExtractPlaceId(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            var m = Regex.Match(s, @"\b([12]-\d{3,})\b");
            return m.Success ? m.Groups[1].Value : null;
        }

        // Build SVG URL for the WebView tab
        private string BuildUrl()
        {
            if (_locationId.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var uri = new Uri(_locationId);
                    string path = uri.AbsolutePath.EndsWith("/meteogram.svg", StringComparison.OrdinalIgnoreCase)
                        ? uri.AbsolutePath
                        : uri.AbsolutePath.TrimEnd('/') + "/meteogram.svg";
                    return new UriBuilder(uri) { Path = path }.Uri.ToString();
                }
                catch { return _locationId; }
            }

            string id = ExtractPlaceId(_locationId) ?? _locationId.Trim('/');
            var seg = ContentSegmentFor(_lang);
            return $"https://www.yr.no/{_lang}/{seg}/{id}/meteogram.svg";
        }

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
            public DateTime TimeLocal { get { return TimeUtc.ToLocalTime(); } }
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

        private static DateTime ParseIsoUtc(string s)
        {
            return DateTime.Parse(s, null,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal).ToUniversalTime();
        }

        private async Task LoadForecastTablesAsync(bool userInitiated = false)
        {
            _loadCts?.Cancel();
            _loadCts = new CancellationTokenSource();
            var ct = _loadCts.Token;

            try
            {
                LblStatus.Text = userInitiated ? "Refreshing forecast…" : "Loading detailed forecast…";

                var latlon = await HttpHelper.RetryAsync(
                    c => TryResolveLatLonAsync(_locationId, _lang, c),
                    retries: 3, initialDelay: TimeSpan.FromMilliseconds(600), ct);

                if (latlon == null)
                {
                    LblStatus.Text = "Could not resolve coordinates for detailed data.";
                    HourlyGrid.ItemsSource = null;
                    DailyGrid.ItemsSource = null;
                    HourlyEmpty.Visibility = Visibility.Visible;
                    DailyEmpty.Visibility = Visibility.Visible;
                    return;
                }

                var hourly = new List<HourlyPoint>();
                var daily = new List<DailySummary>();

                await HttpHelper.RetryAsync<object>(async c =>
                {
                    await FetchLocationForecastAsync(latlon.Item1, latlon.Item2, hourly, daily, c);
                    return null;
                }, retries: 2, initialDelay: TimeSpan.FromMilliseconds(600), ct);

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

        // ---------- Robust coordinate resolution ----------

        private static Tuple<double, double> TryParseLatLonFromString(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            s = WebUtility.UrlDecode(s);

            // lat=..&lon=..
            var m1 = Regex.Match(s, @"lat(?:itude)?\s*[=:]\s*(?<lat>-?\d{1,3}(?:\.\d+)?)[^-\d]{0,20}lon(?:gitude)?\s*[=:]\s*(?<lon>-?\d{1,3}(?:\.\d+)?)",
                                 RegexOptions.IgnoreCase);
            if (m1.Success)
            {
                double la1, lo1;
                if (double.TryParse(m1.Groups["lat"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out la1) &&
                    double.TryParse(m1.Groups["lon"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out lo1))
                    return Tuple.Create(la1, lo1);
            }

            // @lat,lon
            var m2 = Regex.Match(s, @"@(?<lat>-?\d{1,3}(?:\.\d+)?)[,\s]+(?<lon>-?\d{1,3}(?:\.\d+)?)");
            if (m2.Success)
            {
                double la2, lo2;
                if (double.TryParse(m2.Groups["lat"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out la2) &&
                    double.TryParse(m2.Groups["lon"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out lo2))
                    return Tuple.Create(la2, lo2);
            }

            // plain pair
            var m3 = Regex.Match(s, @"(?<!\d)(?<lat>-?\d{1,3}(?:\.\d+)?)[,\s;_]+(?<lon>-?\d{1,3}(?:\.\d+)?)(?!\d)");
            if (m3.Success)
            {
                double la3, lo3;
                if (double.TryParse(m3.Groups["lat"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out la3) &&
                    double.TryParse(m3.Groups["lon"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out lo3))
                    return Tuple.Create(la3, lo3);
            }

            return null;
        }

        private static IEnumerable<string> GenerateNameCandidates(string locationId)
        {
            var outs = new List<string>();
            if (string.IsNullOrWhiteSpace(locationId)) return outs;

            string s = WebUtility.UrlDecode(locationId.Trim('/'));

            // If full URL, take path after the language-specific content segment
            if (s.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var uri = new Uri(s);
                    var partsUrl = uri.AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                    int idx = Array.FindIndex(partsUrl, p =>
                        string.Equals(p, "content", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(p, "innhold", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(p, "innhald", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(p, "sisdoallu", StringComparison.OrdinalIgnoreCase));
                    if (idx >= 0 && idx + 1 < partsUrl.Length)
                        s = string.Join("/", partsUrl, idx + 1, partsUrl.Length - (idx + 1));
                    else
                        s = partsUrl.Length > 0 ? partsUrl[partsUrl.Length - 1] : s;
                }
                catch { /* use original s */ }
            }

            // remove language-specific segment
            s = Regex.Replace(s, @"(?i)(?:^|/)(content|innhold|innhald|sisdoallu)/", "");

            var parts = s.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

            Func<string, string> Clean = x =>
            {
                x = x.Replace('-', ' ');
                x = Regex.Replace(x, @"\b([12]-\d{3,})\b", ""); // numeric id
                x = Regex.Replace(x, @"\b(\d+-\d+|\d{5,})\b", "", RegexOptions.IgnoreCase);
                x = Regex.Replace(x, @"\s+", " ").Trim();
                return x;
            };

            if (parts.Length > 0) outs.Add(Clean(parts[parts.Length - 1]));
            if (parts.Length > 1) outs.Add(Clean(parts[parts.Length - 2] + ", " + parts[parts.Length - 1]));
            if (parts.Length > 2) outs.Add(Clean(parts[parts.Length - 3] + ", " + parts[parts.Length - 2] + ", " + parts[parts.Length - 1]));

            // de-duplicate
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var final = new List<string>();
            foreach (var cand in outs)
                if (!string.IsNullOrWhiteSpace(cand) && seen.Add(cand)) final.Add(cand);

            return final;
        }

        private static async Task<Tuple<double, double>> TryResolveLatLonAsync(string locationId, string lang, CancellationToken ct)
        {
            var direct = TryParseLatLonFromString(locationId);
            if (direct != null) return direct;

            string id = ExtractPlaceId(locationId);

            // 1) yr.no API v0 for canonical id (fastest)
            if (!string.IsNullOrEmpty(id))
            {
                try
                {
                    string url = "https://www.yr.no/api/v0/locations/" + id;
                    string json = await HttpHelper.GetStringWithTimeoutAsync(
                        url, TimeSpan.FromSeconds(35), ct, "HouseholdMS/1.0 (+yr-meteogram)");
                    using (var doc = JsonDocument.Parse(json))
                    {
                        JsonElement geom;
                        if (doc.RootElement.TryGetProperty("geometry", out geom))
                        {
                            JsonElement coords;
                            if (geom.TryGetProperty("coordinates", out coords) &&
                                coords.ValueKind == JsonValueKind.Array && coords.GetArrayLength() >= 2)
                                return Tuple.Create(coords[1].GetDouble(), coords[0].GetDouble());

                            JsonElement latEl, lonEl;
                            if (geom.TryGetProperty("lat", out latEl) && geom.TryGetProperty("lon", out lonEl))
                                return Tuple.Create(latEl.GetDouble(), lonEl.GetDouble());
                        }
                    }
                }
                catch { }
            }

            // prepare base according to language
            string seg = ContentSegmentFor(lang ?? "en");
            Func<string, string> baseUrl = suffix =>
                $"https://www.yr.no/{(lang ?? "en")}/{seg}/{(id ?? locationId.Trim('/'))}/{suffix}";

            // 2) table.html — often has data-lat/lon
            try
            {
                string html = await HttpHelper.GetStringWithTimeoutAsync(
                    baseUrl("table.html"), TimeSpan.FromSeconds(35), ct,
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124 Safari/537.36",
                    acceptLang: (lang ?? "en") + ";q=0.9,en;q=0.8,*;q=0.7");

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

            // 3) meteogram.svg — sometimes includes coords or human name
            try
            {
                string svg = await HttpHelper.GetStringWithTimeoutAsync(
                    baseUrl("meteogram.svg"), TimeSpan.FromSeconds(35), ct,
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124 Safari/537.36",
                    acceptLang: (lang ?? "en") + ";q=0.9,en;q=0.8,*;q=0.7");

                var mSvg = Regex.Match(svg,
                    @"(?:latitude|lat)""?\s*[:=]\s*""?(?<lat>-?\d+(?:\.\d+)?).*?(?:longitude|lon)""?\s*[:=]\s*""?(?<lon>-?\d+(?:\.\d+)?)",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (mSvg.Success)
                {
                    double lat = double.Parse(mSvg.Groups["lat"].Value, CultureInfo.InvariantCulture);
                    double lon = double.Parse(mSvg.Groups["lon"].Value, CultureInfo.InvariantCulture);
                    return Tuple.Create(lat, lon);
                }

                var mm = Regex.Match(svg, @"Weather\s+forecast\s+for\s*(?<name>[^<\r\n]+)", RegexOptions.IgnoreCase);
                if (mm.Success)
                {
                    var loc = await GeocodeByOpenMeteoAsync(new[] { mm.Groups["name"].Value.Trim() }, ct);
                    if (loc != null) return loc;
                }
            }
            catch { }

            // 4) Fall back to names derived from slug/URL segments
            var candidates = GenerateNameCandidates(locationId);
            var loc2 = await GeocodeByOpenMeteoAsync(candidates, ct);
            if (loc2 != null) return loc2;

            return null;
        }

        private static async Task<Tuple<double, double>> GeocodeByOpenMeteoAsync(IEnumerable<string> names, CancellationToken ct)
        {
            foreach (var q in names)
            {
                if (string.IsNullOrWhiteSpace(q)) continue;
                try
                {
                    string url = "https://geocoding-api.open-meteo.com/v1/search?count=1&language=en&format=json&name=" +
                                 Uri.EscapeDataString(q);
                    string json = await HttpHelper.GetStringWithTimeoutAsync(
                        url, TimeSpan.FromSeconds(20), ct, "HouseholdMS/1.0 (+yr-meteogram)");

                    using (var doc = JsonDocument.Parse(json))
                    {
                        JsonElement results;
                        if (doc.RootElement.TryGetProperty("results", out results) &&
                            results.ValueKind == JsonValueKind.Array && results.GetArrayLength() > 0)
                        {
                            var first = results[0];
                            double lat = first.GetProperty("latitude").GetDouble();
                            double lon = first.GetProperty("longitude").GetDouble();
                            return Tuple.Create(lat, lon);
                        }
                    }
                }
                catch { }
            }
            return null;
        }

        private static async Task FetchLocationForecastAsync(
            double lat, double lon, List<HourlyPoint> hourly, List<DailySummary> daily, CancellationToken ct)
        {
            // Locationforecast 2.0 (compact)
            string url = "https://api.met.no/weatherapi/locationforecast/2.0/compact?lat=" +
                         lat.ToString(CultureInfo.InvariantCulture) + "&lon=" +
                         lon.ToString(CultureInfo.InvariantCulture);

            string json = await HttpHelper.GetStringWithTimeoutAsync(
                url, TimeSpan.FromSeconds(35), ct, "HouseholdMS Meteogram/1.0 (contact: app@example.local)");

            using (var doc = JsonDocument.Parse(json))
            {
                JsonElement props, ts;
                if (!doc.RootElement.TryGetProperty("properties", out props)) return;
                if (!props.TryGetProperty("timeseries", out ts)) return;
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

                    JsonElement n1h, n6h;
                    if (data.TryGetProperty("next_1_hours", out n1h))
                    {
                        JsonElement det, sum, p1, sc;
                        if (n1h.TryGetProperty("details", out det) && det.TryGetProperty("precipitation_amount", out p1))
                            precip = TryGetDouble(p1);
                        if (n1h.TryGetProperty("summary", out sum) && sum.TryGetProperty("symbol_code", out sc))
                            symbol = sc.GetString();
                    }
                    else if (data.TryGetProperty("next_6_hours", out n6h))
                    {
                        JsonElement det6, sum6, p6, sc6;
                        if (n6h.TryGetProperty("details", out det6) && det6.TryGetProperty("precipitation_amount", out p6))
                            precip = TryGetDouble(p6);
                        if (n6h.TryGetProperty("summary", out sum6) && sum6.TryGetProperty("symbol_code", out sc6))
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
                    DailySummary ds;
                    if (!map.TryGetValue(day, out ds))
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

        private static double GetDouble(JsonElement obj, string property)
        {
            JsonElement v;
            return obj.TryGetProperty(property, out v) ? TryGetDouble(v) : 0.0;
        }

        private static double TryGetDouble(JsonElement v)
        {
            try { return v.GetDouble(); } catch { return 0.0; }
        }
    }
}
