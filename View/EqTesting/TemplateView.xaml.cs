using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace HouseholdMS.View.EqTesting
{
    /// <summary>
    /// Image-only gallery with album "cards" and responsive image viewer.
    /// Edit the constructor's EDIT REGION or call LoadGallery(...) programmatically.
    /// Arrow keys: Left/Right to navigate images; Home button to go back to album list.
    /// </summary>
    public partial class TemplateView : UserControl
    {
        // ===== Minimal private internals =====
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

        // Public simple spec so callers don’t touch internals
        public sealed class AlbumSpec
        {
            public string Title { get; }
            public string[] Images { get; }
            public AlbumSpec(string title, params string[] images)
            { Title = title; Images = images ?? Array.Empty<string>(); }
        }

        // ===== State =====
        private Gallery _gallery;
        private int _albumIndex = -1; // -1 = Home
        private int _imageIndex = -1;

        public TemplateView()
        {
            InitializeComponent();

            // ensure we can capture arrow keys
            Loaded += (s, e) => this.Focus();

            // ---------- EDIT ONLY THIS REGION (or call public LoadGallery from outside) ----------
            LoadGallery(
                "Battery Charging Procedures", "1.1",
                new AlbumSpec("Battery Visual Inspection",
                    "pack://application:,,,/Assets/Template/1img.png",
                    "pack://application:,,,/Assets/Manuals/batchar1.png"),
                new AlbumSpec("Wiring Check",
                    "pack://application:,,,/Assets/Template/2img.png",
                    "pack://application:,,,/Assets/Manuals/batchar2.png",
                    "pack://application:,,,/Assets/Manuals/batchar3.png"),
                new AlbumSpec("Voltage Measurement",
                    "pack://application:,,,/Assets/Template/3img.png"),
                new AlbumSpec("Final Setup",
                    "pack://application:,,,/Assets/Template/1img.png",
                    "pack://application:,,,/Assets/Manuals/batchar4.png",
                    "pack://application:,,,/Assets/Manuals/batchar5.png"),
                new AlbumSpec("Safety Checks",
                    "pack://application:,,,/Assets/Template/2img.png",
                    "pack://application:,,,/Assets/Template/3img.png")
            );
            // ---------- END EDIT REGION ----------
        }

        /// <summary>
        /// Public API: set gallery with name/version and a list of AlbumSpec(title, images...).
        /// </summary>
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
            RenderHome();
        }

        // ===== Rendering =====
        private void RenderHome()
        {
            _albumIndex = -1;
            _imageIndex = -1;

            // Header
            HeaderTitle.Text = _gallery?.Name ?? "Gallery";
            HeaderVersion.Text = string.IsNullOrWhiteSpace(_gallery?.Version) ? "" : "v" + _gallery.Version;
            HeaderStep.Text = ""; // none on home

            // Toggle views
            HomeScroll.Visibility = Visibility.Visible;
            ImageScroll.Visibility = Visibility.Collapsed;

            // Build album cards
            AlbumList.Children.Clear();
            if (_gallery == null) return;

            for (int i = 0; i < _gallery.Albums.Length; i++)
            {
                var album = _gallery.Albums[i];

                // Fancy content for the card
                var content = new StackPanel
                {
                    Orientation = Orientation.Vertical
                };
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
                    Foreground = System.Windows.Media.Brushes.Gray
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

            // Footer state
            PrevBtn.IsEnabled = false;
            NextBtn.IsEnabled = false;
        }

        private void RenderImage()
        {
            if (_gallery == null || _albumIndex < 0) return;
            var album = _gallery.Albums[_albumIndex];

            // Header + page title
            HeaderTitle.Text = _gallery.Name;
            HeaderVersion.Text = string.IsNullOrWhiteSpace(_gallery.Version) ? "" : "v" + _gallery.Version;
            HeaderStep.Text = $"{album.Title} — Step {_imageIndex + 1} / {album.Images.Length}";

            PageTitle.Text = album.Title;

            // Load image
            try
            {
                var uri = album.Images[_imageIndex];
                PageImage.Source = string.IsNullOrWhiteSpace(uri)
                    ? null
                    : new BitmapImage(new Uri(uri, UriKind.RelativeOrAbsolute));
            }
            catch
            {
                PageImage.Source = null;
            }

            // Toggle views
            HomeScroll.Visibility = Visibility.Collapsed;
            ImageScroll.Visibility = Visibility.Visible;

            // Footer state
            PrevBtn.IsEnabled = _imageIndex > 0;
            NextBtn.IsEnabled = _imageIndex < album.Images.Length - 1;

            // Fit image to current viewport
            UpdateImageViewboxMax();
        }

        // ===== Responsive sizing =====
        private void ImageScroll_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateImageViewboxMax();

        private void UpdateImageViewboxMax()
        {
            // We cap the viewbox size so the image scales with the window but doesn't overflow
            if (ImageScroll == null || ImageViewbox == null) return;

            // Account for padding (16 all around in ScrollViewer + Border Padding)
            double w = Math.Max(0, ImageScroll.ActualWidth - 48);   // approx margins
            double h = Math.Max(0, ImageScroll.ActualHeight - 80);  // header text + paddings

            ImageViewbox.MaxWidth = w;
            ImageViewbox.MaxHeight = h;
        }

        // ===== Events / Navigation =====
        private void AlbumButton_Click(object sender, RoutedEventArgs e)
        {
            if (_gallery == null) return;
            if (sender is Button b && b.Tag is int idx && idx >= 0 && idx < _gallery.Albums.Length)
            {
                _albumIndex = idx;
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

        // Keyboard navigation: Left/Right arrows
        private void Root_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (_albumIndex < 0) return; // on home
            if (e.Key == System.Windows.Input.Key.Left) PrevBtn_Click(this, new RoutedEventArgs());
            if (e.Key == System.Windows.Input.Key.Right) NextBtn_Click(this, new RoutedEventArgs());
        }
    }
}
