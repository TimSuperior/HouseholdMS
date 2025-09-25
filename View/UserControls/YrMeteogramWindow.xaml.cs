using System;
using System.Diagnostics;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace HouseholdMS.View.UserControls
{
    public partial class YrMeteogramWindow : Window
    {
        private readonly string _locationId;
        private readonly string _lang;
        private Microsoft.Web.WebView2.Wpf.WebView2 _web;

        public YrMeteogramWindow(string locationId, string lang)
        {
            InitializeComponent();
            _locationId = locationId;
            _lang = string.IsNullOrWhiteSpace(lang) ? "en" : lang.Trim().ToLowerInvariant();
            Loaded += YrMeteogramWindow_Loaded;
        }

        private async void YrMeteogramWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _web = new Microsoft.Web.WebView2.Wpf.WebView2();
                _web.CoreWebView2InitializationCompleted += Web_CoreWebView2InitializationCompleted;
                _web.NavigationCompleted += Web_NavigationCompleted;
                WebHost.Children.Add(_web);

                await _web.EnsureCoreWebView2Async();
                Navigate();
            }
            catch (Exception ex)
            {
                Fallback(true, "WebView2 initialization failed: " + ex.Message);
            }
        }

        private string BuildUrl()
        {
            return "https://www.yr.no/" + _lang + "/content/" + _locationId + "/meteogram.svg";
        }

        private void Navigate()
        {
            try
            {
                _web.Source = new Uri(BuildUrl());
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
            LblStatus.Text = e.IsSuccess ? "Meteogram ready." : ("Failed to load. " + e.WebErrorStatus);
            if (!e.IsSuccess) Fallback(true, "Failed to load SVG. ErrorStatus=" + e.WebErrorStatus);
        }

        private void Fallback(bool show, string error)
        {
            FallbackPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            TxtError.Text = show ? (error ?? "") : "";
        }

        private void BtnReload_Click(object sender, RoutedEventArgs e) => Navigate();

        private void BtnOpen_Click(object sender, RoutedEventArgs e)
        {
            try { Process.Start(new ProcessStartInfo(BuildUrl()) { UseShellExecute = true }); }
            catch (Exception ex) { Fallback(true, "Open browser failed: " + ex.Message); }
        }
    }
}
