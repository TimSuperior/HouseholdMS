using System;
using System.Collections.Generic; // <-- add this
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HouseholdMS.View.EqTesting
{
    /// <summary>
    /// Image-only gallery with album "cards" and responsive image viewer.
    /// Arrow keys: Left/Right to navigate images; Home button to go back to album list.
    /// </summary>
    public partial class TemplateView : UserControl
    {
        private sealed class Album
        {
            public readonly string Title;
            public readonly string[] Images;
            public Album(string title, params string[] images)
            {
                if (images == null || images.Length == 0)
                    throw new ArgumentException("Album must contain at least one image.", nameof(images));
                Title = title ?? "Album";
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
                Name = name ?? "Gallery";
                Version = string.IsNullOrWhiteSpace(version) ? "" : version;
                Albums = albums;
            }
        }

        public sealed class AlbumSpec
        {
            public string Title { get; }
            public string[] Images { get; }
            public AlbumSpec(string title, params string[] images)
            { Title = title; Images = images ?? Array.Empty<string>(); }
        }

        // State
        private Gallery _gallery;
        private int _albumIndex = -1; // -1 = Home
        private int _imageIndex = -1;

        // Strong image cache (C# 7.3-compatible construction)
        private readonly Dictionary<string, BitmapImage> _imageCache =
            new Dictionary<string, BitmapImage>(StringComparer.OrdinalIgnoreCase);

        public TemplateView()
        {
            InitializeComponent();
            Loaded += (s, e) => this.Focus();

            // ---------- EDIT ONLY THIS REGION (or call public LoadGallery from outside) ----------
            LoadGallery(
                "Victron", "1.1",
                new AlbumSpec("Battery Visual Inspection",
                    "pack://application:,,,/Assets/Procedures/TPEN_Victron_charger_01.png",
                    "pack://application:,,,/Assets/Procedures/TPEN_Victron_charger_02.png",
                    "pack://application:,,,/Assets/Procedures/TPEN_Victron_charger_03.png",
                    "pack://application:,,,/Assets/Procedures/TPEN_Victron_charger_04.png",
                    "pack://application:,,,/Assets/Procedures/TPEN_Victron_charger_05.png",
                    "pack://application:,,,/Assets/Procedures/TPEN_Victron_charger_06.png",
                    "pack://application:,,,/Assets/Procedures/TPEN_Victron_charger_07.png"),
                new AlbumSpec("Wiring Check",
                    "pack://application:,,,/Assets/Procedures/01. TPEN_Victron-03.png",
                    "pack://application:,,,/Assets/Template/222.png",
                    "pack://application:,,,/Assets/Template/222.png"),
                new AlbumSpec("Voltage Measurement",
                    "pack://application:,,,/Assets/Procedures/TPEN_Victron01.png"),
                new AlbumSpec("Final Setup",
                    "pack://application:,,,/Assets/Procedures/TPEN_Victron01.png",
                    "pack://application:,,,/Assets/Manuals/batchar4.png",
                    "pack://application:,,,/Assets/Manuals/batchar5.png"),
                new AlbumSpec("Safety Checks",
                    "pack://application:,,,/Assets/Template/2img.png",
                    "pack://application:,,,/Assets/Template/3img.png")
            );
            // ---------- END EDIT REGION ----------
        }

        public void LoadGallery(string name, string version, params AlbumSpec[] albums)
        {
            if (albums == null || albums.Length == 0)
                throw new ArgumentException("At least one album is required.", nameof(albums));

            var internalAlbums = albums.Select(a =>
            {
                if (a.Images == null || a.Images.Length == 0)
                    throw new ArgumentException($"Album '{a.Title}' must contain at least one image.");
                return new Album(a.Title, a.Images);
            }).ToArray();

            _gallery = new Gallery(name, version, internalAlbums);
            _imageCache.Clear();
            RenderHome();
        }

        // Strong, frozen, in-memory loader (prevents disappearing images)
        private ImageSource LoadImageStrong(string uriString)
        {
            if (string.IsNullOrWhiteSpace(uriString)) return null;

            BitmapImage cached;
            if (_imageCache.TryGetValue(uriString, out cached))
                return cached;

            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad; // fully load now
                bmp.UriSource = new Uri(uriString, UriKind.RelativeOrAbsolute);
                bmp.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                bmp.EndInit();
                bmp.Freeze();
                _imageCache[uriString] = bmp;
                return bmp;
            }
            catch
            {
                return null;
            }
        }

        private void RenderHome()
        {
            _albumIndex = -1;
            _imageIndex = -1;

            HeaderTitle.Text = _gallery?.Name ?? "Gallery";
            HeaderVersion.Text = string.IsNullOrWhiteSpace(_gallery?.Version) ? "" : "v" + _gallery.Version;
            HeaderStep.Text = "";

            HeaderBar.Visibility = Visibility.Visible;
            FooterBar.Visibility = Visibility.Visible;
            HomeScroll.Visibility = Visibility.Visible;
            ImageScroll.Visibility = Visibility.Collapsed;

            AlbumList.Children.Clear();
            if (_gallery == null) return;

            for (int i = 0; i < _gallery.Albums.Length; i++)
            {
                var album = _gallery.Albums[i];

                var content = new StackPanel { Orientation = Orientation.Vertical };

                try
                {
                    var img = new Image
                    {
                        Height = 140,
                        Margin = new Thickness(0, 0, 0, 8),
                        Stretch = Stretch.Uniform,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        VerticalAlignment = VerticalAlignment.Center,
                        ClipToBounds = true,
                        SnapsToDevicePixels = true
                    };
                    RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.Fant);
                    img.Source = LoadImageStrong(album.Images[0]);
                    content.Children.Add(img);
                }
                catch { }

                content.Children.Add(new TextBlock
                {
                    Text = album.Title,
                    FontSize = 16,
                    FontWeight = FontWeights.SemiBold
                });
                content.Children.Add(new TextBlock
                {
                    Text = $"{album.Images.Length} image(s)",
                    FontSize = 12,
                    Foreground = Brushes.Gray
                });

                var btn = new Button
                {
                    Content = content,
                    Style = (Style)FindResource("AlbumButtonStyle"),
                    Tag = i
                };
                btn.Click += AlbumButton_Click;
                AlbumList.Children.Add(btn);
            }

            PrevBtn.IsEnabled = false;
            NextBtn.IsEnabled = false;
        }

        private void RenderImage()
        {
            if (_gallery == null || _albumIndex < 0) return;
            var album = _gallery.Albums[_albumIndex];

            HeaderBar.Visibility = Visibility.Visible;
            HeaderTitle.Text = _gallery.Name;
            HeaderVersion.Text = string.IsNullOrWhiteSpace(_gallery.Version) ? "" : "v" + _gallery.Version;
            HeaderStep.Text = $"{album.Title} — {_imageIndex + 1}/{album.Images.Length}";

            var uri = album.Images[_imageIndex];
            PageImage.Source = LoadImageStrong(uri);

            HomeScroll.Visibility = Visibility.Collapsed;
            ImageScroll.Visibility = Visibility.Visible;

            PrevBtn.IsEnabled = _imageIndex > 0;
            NextBtn.IsEnabled = _imageIndex < album.Images.Length - 1;

            UpdateImageViewboxMax();
        }

        private void UpdateImageViewboxMax()
        {
            if (ImageViewbox == null) return;
            ImageViewbox.MaxWidth = double.PositiveInfinity;
            ImageViewbox.MaxHeight = double.PositiveInfinity;
        }

        private void AlbumButton_Click(object sender, RoutedEventArgs e)
        {
            if (_gallery == null) return;
            var b = sender as Button;
            if (b != null && b.Tag is int && (int)b.Tag >= 0 && (int)b.Tag < _gallery.Albums.Length)
            {
                _albumIndex = (int)b.Tag;
                _imageIndex = 0;
                RenderImage();
            }
        }

        private void HomeBtn_Click(object sender, RoutedEventArgs e) => RenderHome();

        private void PrevBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_gallery == null || _albumIndex < 0) return;
            if (_imageIndex > 0)
            {
                _imageIndex--;
                RenderImage();
            }
        }

        private void NextBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_gallery == null || _albumIndex < 0) return;
            var album = _gallery.Albums[_albumIndex];
            if (_imageIndex < album.Images.Length - 1)
            {
                _imageIndex++;
                RenderImage();
            }
        }

        private void Root_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (_albumIndex < 0) return;
            if (e.Key == System.Windows.Input.Key.Left) PrevBtn_Click(this, new RoutedEventArgs());
            if (e.Key == System.Windows.Input.Key.Right) NextBtn_Click(this, new RoutedEventArgs());
        }
    }
}
