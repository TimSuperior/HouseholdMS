using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using System.IO;
using System.Windows.Resources;

namespace HouseholdMS.View.EqTesting
{
    /// <summary>
    /// Image viewer with a single album: "Battery Visual Inspection".
    /// Arrow keys (Left/Right) and Back/Next buttons navigate images.
    /// Strong caching + multi-path loading to avoid the "blank image" issue.
    /// </summary>
    public partial class AllTestMenuView : UserControl
    {
        // --- Keep the same simple data contracts so you can edit image paths easily ---
        private sealed class Album
        {
            public readonly string Title;
            public readonly string[] Images;
            public Album(string title, params string[] images)
            {
                if (images == null || images.Length == 0)
                    throw new ArgumentException("Album must contain at least one image.", nameof(images));
                Title = string.IsNullOrWhiteSpace(title) ? "Album" : title;
                Images = images;
            }
        }

        private sealed class Gallery
        {
            public readonly string Name;
            public readonly string Version;
            public readonly Album[] Albums;
            public Gallery(string name, string version, params Album[] albums)
            {
                if (albums == null || albums.Length == 0)
                    throw new ArgumentException("At least one album is required.", nameof(albums));
                Name = string.IsNullOrWhiteSpace(name) ? "Gallery" : name;
                Version = string.IsNullOrWhiteSpace(version) ? "" : version;
                Albums = albums;
            }
        }

        public sealed class AlbumSpec
        {
            public string Title { get; }
            public string[] Images { get; }
            public AlbumSpec(string title, params string[] images)
            {
                Title = title;
                Images = images ?? new string[0];
            }
        }

        // --- State ---
        private Gallery _gallery;
        private Album _album;
        private int _imageIndex = -1;

        // Strong image cache
        private readonly Dictionary<string, BitmapImage> _imageCache =
            new Dictionary<string, BitmapImage>(StringComparer.OrdinalIgnoreCase);

        public AllTestMenuView()
        {
            InitializeComponent();

            Loaded += (s, e) => this.Focus(); // enable keyboard navigation

            // ===== EDIT THE IMAGE PATHS HERE AS YOU LIKE =====
            // Only the "Battery Visual Inspection" album remains.
            LoadGalleryAndOpen(
                name: "Victron",
                version: "1.1",
                defaultAlbumTitle: "Battery Visual Inspection",
                new AlbumSpec("Battery Visual Inspection",
                    "pack://application:,,,/Assets/Procedures/TPEN_Victron_charger_07_01.png",
                    "pack://application:,,,/Assets/Procedures/TPEN_Victron_charger_07_02.png",
                    "pack://application:,,,/Assets/Procedures/TPEN_Victron_charger_07_03.png",
                    "pack://application:,,,/Assets/Procedures/TPEN_Victron_charger_07_04.png",
                    "pack://application:,,,/Assets/Procedures/TPEN_Victron_charger_07_05.png",
                    "pack://application:,,,/Assets/Procedures/TPEN_Victron_charger_07_06.png",
                    "pack://application:,,,/Assets/Procedures/TPEN_Victron_charger_07_07.png"
                )
            );            // ===== END EDIT AREA =====
        }

        private void LoadGalleryAndOpen(string name, string version, string defaultAlbumTitle, params AlbumSpec[] albums)
        {
            if (albums == null || albums.Length == 0)
                throw new ArgumentException("At least one album is required.", nameof(albums));

            var internalAlbums = albums.Select(a =>
            {
                if (a.Images == null || a.Images.Length == 0)
                    throw new ArgumentException("Album '" + a.Title + "' must contain at least one image.");
                return new Album(a.Title, a.Images);
            }).ToArray();

            _gallery = new Gallery(name, version, internalAlbums);

            // choose album
            Album chosen = internalAlbums.FirstOrDefault(a =>
                string.Equals(a.Title, defaultAlbumTitle, StringComparison.OrdinalIgnoreCase))
                ?? internalAlbums[0];

            OpenAlbum(chosen);
        }

        private void OpenAlbum(Album album)
        {
            _album = album;
            _imageIndex = 0;
            _imageCache.Clear();
            RenderImage();
        }

        // Multi-strategy, strong loader to avoid blank images:
        // 1) Try direct Uri load (pack/file/relative)
        // 2) If it's an application pack, also try Application.GetResourceStream with the relative part
        // 3) If it's site-of-origin, try combining with base directory
        // 4) Try raw file path
        private ImageSource LoadImageStrong(string uriString)
        {
            if (string.IsNullOrWhiteSpace(uriString)) return null;

            BitmapImage cached;
            if (_imageCache.TryGetValue(uriString, out cached))
                return cached;

            // Strategy 1: standard Uri load
            try
            {
                var bmp1 = new BitmapImage();
                bmp1.BeginInit();
                bmp1.CacheOption = BitmapCacheOption.OnLoad;
                bmp1.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                bmp1.UriSource = new Uri(uriString, UriKind.RelativeOrAbsolute);
                bmp1.EndInit();
                bmp1.Freeze();
                _imageCache[uriString] = bmp1;
                return bmp1;
            }
            catch { /* fall through */ }

            // Strategy 2: Application resource stream from pack application
            try
            {
                const string APP = "pack://application:,,,/";
                string rel = uriString;
                if (rel.StartsWith(APP, StringComparison.OrdinalIgnoreCase))
                    rel = rel.Substring(APP.Length);

                Uri relUri;
                if (Uri.TryCreate(rel, UriKind.Relative, out relUri))
                {
                    StreamResourceInfo sri = Application.GetResourceStream(relUri);
                    if (sri != null && sri.Stream != null)
                    {
                        using (var s = sri.Stream)
                        {
                            var bmp2 = new BitmapImage();
                            bmp2.BeginInit();
                            bmp2.CacheOption = BitmapCacheOption.OnLoad;
                            bmp2.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                            bmp2.StreamSource = s;
                            bmp2.EndInit();
                            bmp2.Freeze();
                            _imageCache[uriString] = bmp2;
                            return bmp2;
                        }
                    }
                }
            }
            catch { /* fall through */ }

            // Strategy 3: site-of-origin => file next to exe
            try
            {
                const string SOO = "pack://siteoforigin:,,,/";
                if (uriString.StartsWith(SOO, StringComparison.OrdinalIgnoreCase))
                {
                    string localPath = uriString.Substring(SOO.Length).TrimStart('/', '\\');
                    string abs = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, localPath);
                    if (File.Exists(abs))
                    {
                        using (var fs = File.OpenRead(abs))
                        {
                            var bmp3 = new BitmapImage();
                            bmp3.BeginInit();
                            bmp3.CacheOption = BitmapCacheOption.OnLoad;
                            bmp3.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                            bmp3.StreamSource = fs;
                            bmp3.EndInit();
                            bmp3.Freeze();
                            _imageCache[uriString] = bmp3;
                            return bmp3;
                        }
                    }
                }
            }
            catch { /* fall through */ }

            // Strategy 4: raw file path
            try
            {
                if (File.Exists(uriString))
                {
                    using (var fs = File.OpenRead(uriString))
                    {
                        var bmp4 = new BitmapImage();
                        bmp4.BeginInit();
                        bmp4.CacheOption = BitmapCacheOption.OnLoad;
                        bmp4.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                        bmp4.StreamSource = fs;
                        bmp4.EndInit();
                        bmp4.Freeze();
                        _imageCache[uriString] = bmp4;
                        return bmp4;
                    }
                }
            }
            catch { /* fall through */ }

            return null;
        }

        private void RenderImage()
        {
            if (_gallery == null || _album == null || _imageIndex < 0 || _imageIndex >= _album.Images.Length)
                return;

            HeaderTitle.Text = _gallery.Name;
            HeaderVersion.Text = string.IsNullOrWhiteSpace(_gallery.Version) ? "" : "v" + _gallery.Version;
            HeaderStep.Text = _album.Title + " — " + (_imageIndex + 1) + "/" + _album.Images.Length;

            string uri = _album.Images[_imageIndex];
            var src = LoadImageStrong(uri);
            PageImage.Source = src;
            PageImage.ToolTip = (src == null) ? ("Missing image: " + uri) : null; // non-invasive hint

            PrevBtn.IsEnabled = _imageIndex > 0;
            NextBtn.IsEnabled = _imageIndex < _album.Images.Length - 1;

            if (ImageViewbox != null)
            {
                ImageViewbox.MaxWidth = double.PositiveInfinity;
                ImageViewbox.MaxHeight = double.PositiveInfinity;
            }
        }

        // ---- UI events ----
        private void PrevBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_album == null) return;
            if (_imageIndex > 0)
            {
                _imageIndex--;
                RenderImage();
            }
        }

        private void NextBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_album == null) return;
            if (_imageIndex < _album.Images.Length - 1)
            {
                _imageIndex++;
                RenderImage();
            }
        }

        private void Root_KeyDown(object sender, KeyEventArgs e)
        {
            if (_album == null) return;

            if (e.Key == Key.Left)
            {
                PrevBtn_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Key.Right)
            {
                NextBtn_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
        }
    }
}
