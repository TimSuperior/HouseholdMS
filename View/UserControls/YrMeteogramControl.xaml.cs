using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace HouseholdMS.View.UserControls
{
    public partial class YrMeteogramControl : UserControl
    {
        private Microsoft.Web.WebView2.Wpf.WebView2 _web;
        private readonly DispatcherTimer _autoTimer = new DispatcherTimer();
        private CancellationTokenSource _cts;
        private bool _isLoadingSummary;

        public YrMeteogramControl()
        {
            InitializeComponent();
            Loaded += YrMeteogramControl_Loaded;
            Unloaded += YrMeteogramControl_Unloaded;

            _autoTimer.Interval = TimeSpan.FromMinutes(AutoRefreshMinutes);
            _autoTimer.Tick += async (_, __) => await RefreshSummaryAsync();
        }

        // ---------------- Dependency Properties ----------------
        public static readonly DependencyProperty LocationIdProperty =
            DependencyProperty.Register(nameof(LocationId), typeof(string), typeof(YrMeteogramControl),
                new PropertyMetadata("2-3667725", OnParamsChanged));

        public string LocationId { get { return (string)GetValue(LocationIdProperty); } set { SetValue(LocationIdProperty, value); } }

        public static readonly DependencyProperty YrLanguageProperty =
            DependencyProperty.Register(nameof(YrLanguage), typeof(string), typeof(YrMeteogramControl),
                new PropertyMetadata("en", OnParamsChanged));

        public string YrLanguage { get { return (string)GetValue(YrLanguageProperty); } set { SetValue(YrLanguageProperty, value); } }

        public static readonly DependencyProperty CompactModeProperty =
            DependencyProperty.Register(nameof(CompactMode), typeof(bool), typeof(YrMeteogramControl),
                new PropertyMetadata(true, OnParamsChanged));

        public bool CompactMode { get { return (bool)GetValue(CompactModeProperty); } set { SetValue(CompactModeProperty, value); } }

        // Optional: auto-geocode when Lat/Lon not given
        public static readonly DependencyProperty PlaceNameProperty =
            DependencyProperty.Register(nameof(PlaceName), typeof(string), typeof(YrMeteogramControl),
                new PropertyMetadata(null, OnParamsChanged));
        public string PlaceName { get { return (string)GetValue(PlaceNameProperty); } set { SetValue(PlaceNameProperty, value); } }

        public static readonly DependencyProperty CountryCodeProperty =
            DependencyProperty.Register(nameof(CountryCode), typeof(string), typeof(YrMeteogramControl),
                new PropertyMetadata(null, OnParamsChanged));
        public string CountryCode { get { return (string)GetValue(CountryCodeProperty); } set { SetValue(CountryCodeProperty, value); } }

        public static readonly DependencyProperty LatitudeProperty =
            DependencyProperty.Register(nameof(Latitude), typeof(double), typeof(YrMeteogramControl),
                new PropertyMetadata(double.NaN, OnParamsChanged));
        public double Latitude { get { return (double)GetValue(LatitudeProperty); } set { SetValue(LatitudeProperty, value); } }

        public static readonly DependencyProperty LongitudeProperty =
            DependencyProperty.Register(nameof(Longitude), typeof(double), typeof(YrMeteogramControl),
                new PropertyMetadata(double.NaN, OnParamsChanged));
        public double Longitude { get { return (double)GetValue(LongitudeProperty); } set { SetValue(LongitudeProperty, value); } }

        public static readonly DependencyProperty AutoRefreshMinutesProperty =
            DependencyProperty.Register(nameof(AutoRefreshMinutes), typeof(int), typeof(YrMeteogramControl),
                new PropertyMetadata(60, OnAutoRefreshChanged));
        public int AutoRefreshMinutes { get { return (int)GetValue(AutoRefreshMinutesProperty); } set { SetValue(AutoRefreshMinutesProperty, value); } }

        private static void OnParamsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var c = (YrMeteogramControl)d;
            if (!c.IsLoaded) return;

            c.ApplyModeToggles();
            if (c.CompactMode) c.RefreshSummaryAsync().ConfigureAwait(false);
            else c.NavigateToMeteogram();
        }

        private static void OnAutoRefreshChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var c = (YrMeteogramControl)d;
            c._autoTimer.Stop();
            var minutes = Math.Max(1, c.AutoRefreshMinutes);
            c._autoTimer.Interval = TimeSpan.FromMinutes(minutes);
            if (c.IsLoaded && c.CompactMode) c._autoTimer.Start();
        }

        private async void YrMeteogramControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (DesignerProperties.GetIsInDesignMode(this))
            {
                Fallback(true, "Design mode: WebView2 not loaded.");
                return;
            }

            ApplyModeToggles();

            if (CompactMode)
            {
                LblStatus.Text = "Loading forecast…";
                _autoTimer.Start();
                await RefreshSummaryAsync();
            }
            else
            {
                LblStatus.Text = "Initializing WebView2...";
                await EnsureWebViewAsync();
                NavigateToMeteogram();
            }
        }

        private void YrMeteogramControl_Unloaded(object sender, RoutedEventArgs e)
        {
            try { if (_web != null && _web.CoreWebView2 != null) _web.CoreWebView2.Stop(); } catch { }
            _autoTimer.Stop();
            if (_cts != null) _cts.Cancel();
        }

        // ---------------- UI helpers ----------------

        private void ApplyModeToggles()
        {
            if (SummaryPanel == null || WebHost == null) return;
            if (CompactMode)
            {
                SummaryPanel.Visibility = Visibility.Visible;
                WebHost.Visibility = Visibility.Collapsed;
                Fallback(false, null);
            }
            else
            {
                SummaryPanel.Visibility = Visibility.Collapsed;
                WebHost.Visibility = Visibility.Visible;
            }
        }

        private void Tile_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // Ignore clicks directly on buttons (e.g., Reload)
            var fe = e.OriginalSource as FrameworkElement;
            if (fe is Button || (fe != null && fe.Parent is Button)) return;

            try
            {
                var win = new YrMeteogramWindow(LocationId, YrLanguage);
                var owner = Window.GetWindow(this);
                win.Owner = owner;
                win.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                win.Show();
            }
            catch (Exception ex)
            {
                Fallback(true, "Open window failed: " + ex.Message);
            }
        }

        // ---------------- WebView2 full mode ----------------

        private async Task EnsureWebViewAsync()
        {
            try
            {
                if (_web == null)
                {
                    _web = new Microsoft.Web.WebView2.Wpf.WebView2
                    {
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        VerticalAlignment = VerticalAlignment.Stretch
                    };
                    _web.CoreWebView2InitializationCompleted += Web_CoreWebView2InitializationCompleted;
                    _web.NavigationCompleted += Web_NavigationCompleted;

                    WebHost.Children.Clear();
                    WebHost.Children.Add(_web);
                }

                if (_web.CoreWebView2 == null)
                    await _web.EnsureCoreWebView2Async();

                Fallback(false, null);
            }
            catch (Exception ex)
            {
                Fallback(true, "WebView2 initialization failed: " + ex.Message);
            }
        }

        private string BuildUrl()
        {
            var lang = string.IsNullOrWhiteSpace(YrLanguage) ? "en" : YrLanguage.Trim().ToLowerInvariant();
            return "https://www.yr.no/" + lang + "/content/" + LocationId + "/meteogram.svg";
        }

        private void NavigateToMeteogram()
        {
            LblStatus.Text = "Loading meteogram…";
            try
            {
                if (_web != null && _web.CoreWebView2 != null)
                {
                    _web.Source = new Uri(BuildUrl());
                    Fallback(false, null);
                }
            }
            catch (Exception ex)
            {
                Fallback(true, "Navigation error: " + ex.Message);
            }
        }

        private void Web_CoreWebView2InitializationCompleted(object sender, CoreWebView2InitializationCompletedEventArgs e)
        {
            if (!e.IsSuccess) { Fallback(true, "WebView2 could not initialize."); return; }
            try
            {
                _web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                _web.CoreWebView2.Settings.AreDevToolsEnabled = false;
                _web.CoreWebView2.Settings.IsStatusBarEnabled = false;
            }
            catch { }
        }

        private void Web_NavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (e.IsSuccess) LblStatus.Text = "Meteogram ready.";
            else Fallback(true, "Failed to load SVG. ErrorStatus=" + e.WebErrorStatus);
        }

        // ---------------- Compact Summary ----------------

        private async Task RefreshSummaryAsync()
        {
            if (!CompactMode) return;
            if (_isLoadingSummary) return;                 // prevent overlapping calls
            _isLoadingSummary = true;
            if (BtnReload != null) BtnReload.IsEnabled = false;

            if (_cts != null) _cts.Cancel();
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;

            try
            {
                double lat, lon;
                if (!TryGetLatLon(out lat, out lon))
                {
                    var resolved = await GeocodeIfNeededAsync(ct);
                    if (resolved == null)
                    {
                        LineNow.Text = "—";
                        LineNext.Text = "—";
                        LineMeta.Text = "Set Latitude/Longitude or PlaceName";
                        return;
                    }
                    lat = resolved.Item1;
                    lon = resolved.Item2;
                }

                var sum = await FetchSummaryAsync(lat, lon, ct);
                if (ct.IsCancellationRequested || sum == null) return;

                LineNow.Text = string.Format("Now: {0:0.#}°C", sum.TempNow);
                LineNext.Text = string.Format("24h: H {0:0.#}°  L {1:0.#}°   •   Rain {2:0.#} mm",
                                              sum.Max24h, sum.Min24h, sum.Precip24h);
                LineMeta.Text = string.Format("Wind {0:0.#} m/s   •   Updated {1}",
                                              sum.WindNow, sum.Updated.ToLocalTime().ToString("g"));
                LblStatus.Text = " ";
            }
            catch (Exception ex)
            {
                LblStatus.Text = "Summary load failed.";
                LineMeta.Text = ex.Message;
            }
            finally
            {
                _isLoadingSummary = false;
                if (BtnReload != null) BtnReload.IsEnabled = true;
            }
        }

        private bool TryGetLatLon(out double lat, out double lon)
        {
            lat = Latitude; lon = Longitude;
            return !(double.IsNaN(lat) || double.IsNaN(lon));
        }

        private async Task<Tuple<double, double>> GeocodeIfNeededAsync(CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(PlaceName)) return null;

            string url = "https://geocoding-api.open-meteo.com/v1/search?count=1&language=en&format=json&name=" +
                         Uri.EscapeDataString(PlaceName.Trim());
            if (!string.IsNullOrWhiteSpace(CountryCode))
                url += "&country=" + Uri.EscapeDataString(CountryCode.Trim());

            HttpClient http = new HttpClient();
            http.Timeout = TimeSpan.FromSeconds(10);
            try
            {
                using (HttpResponseMessage resp = await http.GetAsync(url, ct))
                {
                    resp.EnsureSuccessStatusCode();
                    string json = await resp.Content.ReadAsStringAsync();

                    using (JsonDocument doc = JsonDocument.Parse(json))
                    {
                        JsonElement results;
                        if (!doc.RootElement.TryGetProperty("results", out results) || results.GetArrayLength() == 0)
                            return null;

                        JsonElement first = results[0];
                        double lat = first.GetProperty("latitude").GetDouble();
                        double lon = first.GetProperty("longitude").GetDouble();
                        return Tuple.Create(lat, lon);
                    }
                }
            }
            catch { return null; }
            finally { http.Dispose(); }
        }

        private sealed class ForecastSummary
        {
            public double TempNow { get; set; }
            public double WindNow { get; set; }
            public double Min24h { get; set; }
            public double Max24h { get; set; }
            public double Precip24h { get; set; }
            public DateTime Updated { get; set; }
        }

        private static async Task<ForecastSummary> FetchSummaryAsync(double lat, double lon, CancellationToken ct)
        {
            HttpClient http = new HttpClient();
            http.Timeout = TimeSpan.FromSeconds(10);
            try
            {
                http.DefaultRequestHeaders.UserAgent.ParseAdd("HouseholdMS Meteogram/1.0 (contact: app@example.local)");
                string url = "https://api.met.no/weatherapi/locationforecast/2.0/compact?lat=" +
                             lat.ToString(CultureInfo.InvariantCulture) +
                             "&lon=" + lon.ToString(CultureInfo.InvariantCulture);

                using (HttpResponseMessage resp = await http.GetAsync(url, ct))
                {
                    resp.EnsureSuccessStatusCode();
                    string json = await resp.Content.ReadAsStringAsync();

                    using (JsonDocument doc = JsonDocument.Parse(json))
                    {
                        JsonElement props = doc.RootElement.GetProperty("properties");
                        JsonElement meta = props.GetProperty("meta");
                        DateTime updated = meta.TryGetProperty("updated_at", out JsonElement updVal)
                            ? ParseIsoUtc(updVal.GetString())
                            : DateTime.UtcNow;

                        JsonElement ts = props.GetProperty("timeseries");
                        if (ts.GetArrayLength() == 0) return null;

                        double tempNow = 0, windNow = 0;
                        JsonElement first = ts[0];
                        JsonElement inst = first.GetProperty("data").GetProperty("instant").GetProperty("details");
                        JsonElement tnow, wnow;
                        if (inst.TryGetProperty("air_temperature", out tnow)) tempNow = tnow.GetDouble();
                        if (inst.TryGetProperty("wind_speed", out wnow)) windNow = wnow.GetDouble();

                        DateTime t0 = ParseIsoUtc(first.GetProperty("time").GetString());
                        DateTime cutoff = t0.AddHours(24);

                        double min = double.MaxValue, max = double.MinValue, precip = 0;

                        foreach (JsonElement item in ts.EnumerateArray())
                        {
                            DateTime time = ParseIsoUtc(item.GetProperty("time").GetString());
                            if (time > cutoff) break;

                            JsonElement data = item.GetProperty("data");
                            JsonElement instDet = data.GetProperty("instant").GetProperty("details");

                            JsonElement tEl;
                            if (instDet.TryGetProperty("air_temperature", out tEl))
                            {
                                double t = tEl.GetDouble();
                                if (t < min) min = t;
                                if (t > max) max = t;
                            }

                            JsonElement n1h, det, p1, n6h, det6, p6;
                            if (data.TryGetProperty("next_1_hours", out n1h))
                            {
                                if (n1h.TryGetProperty("details", out det) && det.TryGetProperty("precipitation_amount", out p1))
                                    precip += p1.GetDouble();
                            }
                            else if (data.TryGetProperty("next_6_hours", out n6h))
                            {
                                if (n6h.TryGetProperty("details", out det6) && det6.TryGetProperty("precipitation_amount", out p6))
                                    precip += p6.GetDouble();
                            }
                        }

                        if (min == double.MaxValue) min = tempNow;
                        if (max == double.MinValue) max = tempNow;

                        ForecastSummary r = new ForecastSummary();
                        r.TempNow = tempNow; r.WindNow = windNow; r.Min24h = min; r.Max24h = max;
                        r.Precip24h = precip < 0 ? 0 : precip; r.Updated = updated;
                        return r;
                    }
                }
            }
            finally { http.Dispose(); }
        }

        private static DateTime ParseIsoUtc(string s)
        {
            return DateTime.Parse(s, null,
                System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal)
                .ToUniversalTime();
        }

        // ---------------- Shared actions ----------------

        private void Fallback(bool show, string error)
        {
            FallbackPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (TxtError != null) TxtError.Text = (show && !string.IsNullOrEmpty(error)) ? error : string.Empty;
            if (!show) LblStatus.Text = " ";
        }

        private async void BtnReload_Click(object sender, RoutedEventArgs e)
        {
            if (CompactMode) await RefreshSummaryAsync();
            else NavigateToMeteogram();
        }

        private void BtnCopy_Click(object sender, RoutedEventArgs e)
        {
            try { Clipboard.SetText(BuildUrl()); LblStatus.Text = "Link copied."; }
            catch (Exception ex) { Fallback(true, "Copy failed: " + ex.Message); }
        }
    }
}
