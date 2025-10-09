// HouseholdMS.View.UserControls.YrMeteogramWindow.xaml.cs  (C# 7.3 compatible)
using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace HouseholdMS.View.UserControls
{
    public partial class YrMeteogramWindow : Window
    {
        private readonly string _locationId;
        private readonly string _lang;
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

        // Build SVG URL for the WebView tab
        private string BuildUrl()
        {
            if (_locationId.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                bool hasSvg = _locationId.IndexOf("meteogram.svg", StringComparison.OrdinalIgnoreCase) >= 0;
                return hasSvg ? _locationId : (_locationId.TrimEnd('/') + "/meteogram.svg");
            }
            return "https://www.yr.no/" + _lang + "/content/" + _locationId.Trim('/') + "/meteogram.svg";
        }

        private void Navigate()
        {
            try
            {
                string url = BuildUrl();
                if (_web != null && _web.CoreWebView2 != null) _web.CoreWebView2.Navigate(url);
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
        }
    }
}
