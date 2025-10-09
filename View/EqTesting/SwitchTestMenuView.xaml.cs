// View/EqTesting/SwitchTestMenuView.xaml.cs
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
    /// Single-album image viewer for Switch tests.
    /// Back/Next buttons + Left/Right arrow keys.
    /// Up/Down keys suppressed to avoid dotted focus cues.
    /// </summary>
    public partial class SwitchTestMenuView : UserControl
    {
        // -------------------- SIMPLE LANGUAGE SWITCH (images) --------------------
        // Edit only these arrays when you add/remove frames.
        // EN = "TPEN_*", ES = "TPES_*". (Add KO later if you want.)
        private static readonly string[] EN_IMAGES = new[]
        {
            "pack://application:,,,/Assets/Procedures/TPEN_PnK_Inverter_01.png",
            "pack://application:,,,/Assets/Procedures/TPEN_PnK_Inverter_02.png",
            "pack://application:,,,/Assets/Procedures/TPEN_PnK_Inverter_03.png",
            "pack://application:,,,/Assets/Procedures/TPEN_PnK_Inverter_04.png",
            "pack://application:,,,/Assets/Procedures/TPEN_PnK_Inverter_05.png",
            "pack://application:,,,/Assets/Procedures/TPEN_PnK_Inverter_06.png",
            "pack://application:,,,/Assets/Procedures/TPEN_PnK_Inverter_07.png",
        };

        private static readonly string[] ES_IMAGES = new[]
        {
            "pack://application:,,,/Assets/Procedures/TPES_PnK_Inverter_01.png",
            "pack://application:,,,/Assets/Procedures/TPES_PnK_Inverter_02.png",
            "pack://application:,,,/Assets/Procedures/TPES_PnK_Inverter_03.png",
            "pack://application:,,,/Assets/Procedures/TPES_PnK_Inverter_04.png",
            "pack://application:,,,/Assets/Procedures/TPES_PnK_Inverter_05.png",
            "pack://application:,,,/Assets/Procedures/TPES_PnK_Inverter_06.png",
            "pack://application:,,,/Assets/Procedures/TPES_PnK_Inverter_07.png",
        };

        // If you add Korean later, just define:
        // private static readonly string[] KO_IMAGES = new[] { "pack://.../TPKO_Switch_01.png", ... };

        private static string GetSavedLanguage()
        {
            try
            {
                var path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "HouseholdMS", "ui.language");
                if (File.Exists(path))
                {
                    var code = (File.ReadAllText(path) ?? "").Trim().ToLowerInvariant();
                    if (code == "en" || code == "es" || code == "ko")
                        return code;
                }
            }
            catch { /* ignore and fall back */ }
            return "en";
        }

        private static string[] GetImagesForLang(string lang)
        {
            if (lang == "es") return ES_IMAGES;
            // if (lang == "ko") return KO_IMAGES; // when you add them
            return EN_IMAGES; // default
        }
        // ----------------------------------------------------------------

        // -------------------- MINI L10N BLOCK (texts) --------------------
        private static readonly Dictionary<string, Dictionary<string, string>> L10N =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["en"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["gallery_name"] = "Switch Panel",
                    ["album_switch"] = "Switch Tests",
                    ["missing_image"] = "Missing image: {0}",
                    ["err_one_album_required"] = "At least one album is required.",
                    ["err_album_empty"] = "Album '{0}' must contain at least one image.",
                    ["prev"] = "Back",
                    ["next"] = "Next",
                    ["version_prefix"] = "v",
                    ["step_sep"] = " — "
                },
                ["es"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["gallery_name"] = "Panel de interruptores",
                    ["album_switch"] = "Pruebas de interruptores",
                    ["missing_image"] = "Imagen ausente: {0}",
                    ["err_one_album_required"] = "Se requiere al menos un álbum.",
                    ["err_album_empty"] = "El álbum '{0}' debe contener al menos una imagen.",
                    ["prev"] = "Atrás",
                    ["next"] = "Siguiente",
                    ["version_prefix"] = "v",
                    ["step_sep"] = " — "
                },
                ["ko"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["gallery_name"] = "스위치 패널",
                    ["album_switch"] = "스위치 테스트",
                    ["missing_image"] = "이미지를 찾을 수 없음: {0}",
                    ["err_one_album_required"] = "앨범이 하나 이상 필요합니다.",
                    ["err_album_empty"] = "앨범 '{0}'에는 하나 이상의 이미지가 있어야 합니다.",
                    ["prev"] = "이전",
                    ["next"] = "다음",
                    ["version_prefix"] = "v",
                    ["step_sep"] = " — "
                }
            };

        private Dictionary<string, string> _ST; // active string table

        private static Dictionary<string, string> GetStrings(string lang)
        {
            Dictionary<string, string> st;
            if (!L10N.TryGetValue(lang ?? "en", out st)) st = L10N["en"];
            return st;
        }

        private string T(string key)
        {
            if (_ST == null) return key;
            string v; return _ST.TryGetValue(key, out v) ? v : key;
        }
        // ----------------------------------------------------------------

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

        private Gallery _gallery;
        private Album _album;
        private int _imageIndex = -1;

        // Strong cache prevents disappearing images
        private readonly Dictionary<string, BitmapImage> _imageCache =
            new Dictionary<string, BitmapImage>(StringComparer.OrdinalIgnoreCase);

        public SwitchTestMenuView()
        {
            InitializeComponent();
            Loaded += (s, e) => this.Focus(); // keep focus for keyboard handling

            // ---- Choose the set once based on saved language ----
            var lang = GetSavedLanguage();
            _ST = GetStrings(lang);
            var imgs = GetImagesForLang(lang);

            // Immediately open the Switch Tests album.
            var albumTitle = T("album_switch");
            LoadGalleryAndOpen(
                name: T("gallery_name"),
                version: "1.1",
                defaultAlbumTitle: albumTitle,
                new AlbumSpec(albumTitle, imgs)
            );

            // Optional: localize button captions if present in XAML
            if (PrevBtn != null) PrevBtn.Content = T("prev");
            if (NextBtn != null) NextBtn.Content = T("next");
        }

        private void LoadGalleryAndOpen(string name, string version, string defaultAlbumTitle, params AlbumSpec[] albums)
        {
            if (albums == null || albums.Length == 0)
                throw new ArgumentException(T("err_one_album_required"), nameof(albums));

            Album[] internalAlbums = albums.Select(a =>
            {
                if (a.Images == null || a.Images.Length == 0)
                    throw new ArgumentException(string.Format(T("err_album_empty"), a.Title));
                return new Album(a.Title, a.Images);
            }).ToArray();

            _gallery = new Gallery(name, version, internalAlbums);

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

        // Robust loader: tries direct URI, pack resource stream, site-of-origin, then raw file path.
        private ImageSource LoadImageStrong(string uriString)
        {
            if (string.IsNullOrWhiteSpace(uriString)) return null;

            BitmapImage cached;
            if (_imageCache.TryGetValue(uriString, out cached))
                return cached;

            // 1) Direct Uri load (pack/file/absolute/relative)
            try
            {
                BitmapImage bmp1 = new BitmapImage();
                bmp1.BeginInit();
                bmp1.CacheOption = BitmapCacheOption.OnLoad;
                bmp1.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                bmp1.UriSource = new Uri(uriString, UriKind.RelativeOrAbsolute);
                bmp1.EndInit();
                bmp1.Freeze();
                _imageCache[uriString] = bmp1;
                return bmp1;
            }
            catch { }

            // 2) Application resource stream (for pack application resources)
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
                        using (Stream s = sri.Stream)
                        {
                            BitmapImage bmp2 = new BitmapImage();
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
            catch { }

            // 3) Site-of-origin mapping (file next to exe)
            try
            {
                const string SOO = "pack://siteoforigin:,,,/";
                if (uriString.StartsWith(SOO, StringComparison.OrdinalIgnoreCase))
                {
                    string localPath = uriString.Substring(SOO.Length).TrimStart('/', '\\');
                    string abs = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, localPath);
                    if (File.Exists(abs))
                    {
                        using (FileStream fs = File.OpenRead(abs))
                        {
                            BitmapImage bmp3 = new BitmapImage();
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
            catch { }

            // 4) Raw file path
            try
            {
                if (File.Exists(uriString))
                {
                    using (FileStream fs = File.OpenRead(uriString))
                    {
                        BitmapImage bmp4 = new BitmapImage();
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
            catch { }

            return null;
        }

        private void RenderImage()
        {
            if (_gallery == null || _album == null || _imageIndex < 0 || _imageIndex >= _album.Images.Length)
                return;

            HeaderTitle.Text = _gallery.Name;

            var ver = string.IsNullOrWhiteSpace(_gallery.Version) ? "" : T("version_prefix") + _gallery.Version;
            HeaderVersion.Text = ver;

            HeaderStep.Text = _album.Title + T("step_sep") + (_imageIndex + 1) + "/" + _album.Images.Length;

            string uri = _album.Images[_imageIndex];
            ImageSource src = LoadImageStrong(uri);
            PageImage.Source = src;
            PageImage.ToolTip = (src == null) ? string.Format(T("missing_image"), uri) : null;

            PrevBtn.IsEnabled = _imageIndex > 0;
            NextBtn.IsEnabled = _imageIndex < _album.Images.Length - 1;

            if (ImageViewbox != null)
            {
                ImageViewbox.MaxWidth = double.PositiveInfinity;
                ImageViewbox.MaxHeight = double.PositiveInfinity;
            }
        }

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

        private void Root_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Swallow Up/Down so WPF doesn't shift focus and draw dotted focus cues
            if (e.Key == Key.Up || e.Key == Key.Down)
            {
                e.Handled = true;
                return;
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
            else if (e.Key == Key.Up || e.Key == Key.Down)
            {
                // belt & suspenders
                e.Handled = true;
            }
        }
    }
}
