using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using HouseholdMS.Resources; // Strings.*

namespace HouseholdMS.View.UserControls
{
    public partial class YrMeteogramWindow : Window
    {
        private readonly string _locationId;
        private readonly string _lang;              // app language ("en", "es", "es-ES", etc.)
        private Microsoft.Web.WebView2.Wpf.WebView2 _web;

        private bool _retryAfterReset = false;
        private const double DefaultZoom = 1.35;

        public YrMeteogramWindow(string locationId, string lang)
        {
            InitializeComponent();
            _locationId = locationId ?? "";
            _lang = string.IsNullOrWhiteSpace(lang) ? "en" : lang.Trim().ToLowerInvariant();

            Loaded += YrMeteogramWindow_Loaded;
            SizeChanged += delegate { ApplyZoom(); };
        }

        // ---------------------- WebView2 init ----------------------
        private async void YrMeteogramWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                LblStatus.Text = Strings.YR_Status_Initializing;

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
            }
            catch (Exception ex)
            {
                Fallback(true, Strings.YR_Error_InitFailed + ex.Message);
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
                if (_web == null || _web.CoreWebView2 == null) return;
                string scale = (DefaultZoom * 100).ToString("0", CultureInfo.InvariantCulture);
                string script =
                    @"(function(){try{var s=document.querySelector('svg');" +
                    string.Format(CultureInfo.InvariantCulture,
                        "if(s){{s.style.transformOrigin='0 0';s.style.transform='scale({0})';}}", DefaultZoom) +
                    "else{document.body.style.zoom='" + scale + "%';}})();";
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
                Fallback(true, Strings.YR_Error_RuntimeMissing);
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
                    Fallback(true, Strings.YR_Error_CouldNotInitialize + " " + ex.Message);
                }
            }
        }

        // ---------- Build yr.no meteogram URL (normalize language to 'en' or 'es') ----------
        private static string NormalizeYrLang(string lang)
        {
            if (string.IsNullOrWhiteSpace(lang)) return "en";
            lang = lang.ToLowerInvariant();
            if (lang.StartsWith("es")) return "es";
            if (lang.StartsWith("en")) return "en";
            return "en";
        }

        private string BuildUrl()
        {
            var langPath = NormalizeYrLang(_lang);

            if (_locationId.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                bool hasSvg = _locationId.IndexOf("meteogram.svg", StringComparison.OrdinalIgnoreCase) >= 0;
                return hasSvg ? _locationId : (_locationId.TrimEnd('/') + "/meteogram.svg");
            }
            // e.g. https://www.yr.no/es/content/<place-id>/meteogram.svg
            return "https://www.yr.no/" + langPath + "/content/" + _locationId.Trim('/') + "/meteogram.svg";
        }

        private void Navigate()
        {
            try
            {
                string url = BuildUrl();
                if (_web != null && _web.CoreWebView2 != null) _web.CoreWebView2.Navigate(url);
                else _web.Source = new Uri(url);
                Fallback(false, null);
                LblStatus.Text = Strings.YR_Status_Loading;
            }
            catch (Exception ex)
            {
                Fallback(true, Strings.YR_Error_NavigationPrefix + ex.Message);
            }
        }

        private void Web_CoreWebView2InitializationCompleted(object sender, CoreWebView2InitializationCompletedEventArgs e)
        {
            if (!e.IsSuccess)
            {
                Fallback(true, Strings.YR_Error_CouldNotInitialize);
                return;
            }
            ApplyZoom();
        }

        private async void Web_NavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            LblStatus.Text = e.IsSuccess
                ? Strings.YR_Status_Ready
                : (Strings.YR_Status_LoadFailed + " " + e.WebErrorStatus);

            if (!e.IsSuccess)
            {
                Fallback(true, Strings.YR_Error_LoadSvgPrefix + e.WebErrorStatus);
                return;
            }

            ApplyZoom();
            InjectScaleScriptFallback();
            await LocalizeMeteogramSvgAsync();   // <-- NEW: fix legend + headline to chosen language
        }

        // ---------------------- In-SVG localization ----------------------
        private static string JsEscape(string s) =>
            (s ?? string.Empty).Replace("\\", "\\\\").Replace("'", "\\'");

        private async Task LocalizeMeteogramSvgAsync()
        {
            try
            {
                if (_web?.CoreWebView2 == null) return;

                // Target labels from resources (they’ll resolve to EN or ES automatically)
                var tgtTemp = JsEscape(Strings.YR_SVG_Temperature);
                var tgtPrec = JsEscape(Strings.YR_SVG_Precipitation);
                var tgtWind = JsEscape(Strings.YR_SVG_Wind);
                var tgtForecastPrefix = JsEscape(Strings.YR_SVG_WeatherForecastFor) + " ";

                // Known forms the SVG may ship with (EN/ES today). We normalize them to the current locale.
                var js = @"
(function(){
  try {
    var KNOWN_TEMP = ['Temperature °C','Temperatura °C'];
    var KNOWN_PREC = ['Precipitation mm','Precipitación mm'];
    var KNOWN_WIND = ['Wind m/s','Viento m/s'];
    var KNOWN_FORECAST_PREFIX = ['Weather forecast for ','Pronóstico del tiempo para '];

    var T_TEMP = '" + tgtTemp + @"';
    var T_PREC = '" + tgtPrec + @"';
    var T_WIND = '" + tgtWind + @"';
    var T_FORECAST_PREFIX = '" + tgtForecastPrefix + @"';

    var svg = document.querySelector('svg');
    if (!svg) return;

    var texts = svg.querySelectorAll('text');

    texts.forEach(function(node){
      var t = (node.textContent || '').trim();

      // Legend labels
      if (KNOWN_TEMP.indexOf(t) >= 0) { node.textContent = T_TEMP; return; }
      if (KNOWN_PREC.indexOf(t) >= 0) { node.textContent = T_PREC; return; }
      if (KNOWN_WIND.indexOf(t) >= 0) { node.textContent = T_WIND; return; }

      // Big headline: preserve the place name
      for (var i=0;i<KNOWN_FORECAST_PREFIX.length;i++) {
        var p = KNOWN_FORECAST_PREFIX[i];
        if (t.indexOf(p) === 0 && t.length > p.length) {
          var place = t.substring(p.length);
          node.textContent = T_FORECAST_PREFIX + place;
          return;
        }
      }
    });

    // Accessibility hint
    document.documentElement.setAttribute('lang', '" + JsEscape(NormalizeYrLang(_lang)) + @"');
  } catch(_) {}
})();";

                // Tiny delay helps when SVG sub-resources lay out after onload
                await Task.Delay(60);
                await _web.CoreWebView2.ExecuteScriptAsync(js);
            }
            catch
            {
                // Non-fatal; keep going silently
            }
        }

        private void Fallback(bool show, string error)
        {
            FallbackPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            TxtError.Text = show ? (error ?? "") : "";
        }

        private void BtnReload_Click(object sender, RoutedEventArgs e)
        {
            Navigate();
        }
    }
}
