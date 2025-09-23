using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HouseholdMS.View.EqTesting
{
    public partial class TestProcedure : UserControl
    {
        // ---------- Part model ----------
        public abstract class PartSpec { public abstract UIElement Build(); }

        public sealed class ImagePart : PartSpec
        {
            public string UriStr { get; }
            public ImagePart(string uri) { UriStr = uri; }
            public override UIElement Build()
            {
                var img = new Image
                {
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch
                };
                try
                {
                    if (!string.IsNullOrWhiteSpace(UriStr))
                        img.Source = new BitmapImage(new Uri(UriStr, UriKind.RelativeOrAbsolute));
                }
                catch { /* ignore */ }
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.Fant);
                return new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.Both, Child = img };
            }
        }

        public sealed class ControlFactoryPart : PartSpec
        {
            public Func<UIElement> Factory { get; }
            public ControlFactoryPart(Func<UIElement> factory) { Factory = factory; }
            public override UIElement Build() => Factory != null ? Factory() : Placeholder("Factory returned null");
        }

        public sealed class ControlTypePart : PartSpec
        {
            public string TypeName { get; }
            public ControlTypePart(string typeName) { TypeName = typeName; }
            public override UIElement Build()
            {
                if (string.IsNullOrWhiteSpace(TypeName)) return Placeholder("TypeName not provided");
                try
                {
                    var t = Type.GetType(TypeName, false);
                    if (t == null) return Placeholder("Type not found:\n" + TypeName);
                    var inst = Activator.CreateInstance(t) as UIElement;
                    return inst ?? Placeholder("Type is not a UIElement:\n" + TypeName);
                }
                catch (Exception ex) { return Placeholder("Error creating " + TypeName + "\n" + ex.Message); }
            }
        }

        // ---------- Page model ----------
        public sealed class PageSpec
        {
            public string Title { get; }
            public PartSpec Part1 { get; }
            public PartSpec Part2 { get; }
            public PartSpec Part3 { get; }
            public PageSpec(string title, PartSpec p1 = null, PartSpec p2 = null, PartSpec p3 = null)
            { Title = string.IsNullOrWhiteSpace(title) ? "Step" : title; Part1 = p1; Part2 = p2; Part3 = p3; }
        }

        private sealed class Procedure
        {
            public string Name { get; }
            public string Version { get; }
            public PageSpec[] Pages { get; }
            public Procedure(string name, string version, params PageSpec[] pages)
            {
                if (pages == null || pages.Length == 0) throw new ArgumentException("At least one page is required.", "pages");
                Name = string.IsNullOrWhiteSpace(name) ? "Procedure" : name;
                Version = string.IsNullOrWhiteSpace(version) ? "" : version;
                Pages = pages;
            }
        }

        // ---------- Helpers ----------
        public static PageSpec Page(string title, PartSpec p1 = null, PartSpec p2 = null, PartSpec p3 = null)
            => new PageSpec(title, p1, p2, p3);
        public static PartSpec Img(string packUri) => new ImagePart(packUri);
        public static PartSpec Ctrl(Func<UIElement> factory) => new ControlFactoryPart(factory);
        public static PartSpec Ctrl(string typeName) => new ControlTypePart(typeName);

        // ---------- State ----------
        private Procedure _proc;
        private int _pageIndex = -1;

        public TestProcedure()
        {
            InitializeComponent();
            Loaded += (s, e) => Focus();

            // SAMPLE wiring:
            // Left: MPPT mini (if you have it), Center: image, Right: IT8615 mini (loader)
            LoadProcedure(
                "Battery Charging Procedure", "1.0",
                Page("Step A",
                    // If you already have MpptMiniPanelControl:
                    Ctrl("HouseholdMS.View.EqTesting.MpptMiniPanelControl, HouseholdMS"),
                    Img("pack://application:,,,/Assets/Procedures/111.png"),
                    Ctrl(() => new It8615MiniPanelControl())
                ),
                Page("Step B",
                    null,
                    Img("pack://application:,,,/Assets/Procedures/111.png"),
                    Ctrl("HouseholdMS.View.EqTesting.It8615MiniPanelControl, HouseholdMS")
                ),
                Page("Diagram Only",
                    null,
                    Img("pack://application:,,,/Assets/Procedures/diagram.png"),
                    null)
            );
        }

        public void LoadProcedure(string name, string version, params PageSpec[] pages)
        {
            _proc = new Procedure(name, version, pages);
            RenderHome();
        }

        private void RenderHome()
        {
            _pageIndex = -1;

            HeaderTitle.Text = _proc != null ? _proc.Name : "Procedure";
            HeaderVersion.Text = _proc != null && !string.IsNullOrWhiteSpace(_proc.Version) ? "v" + _proc.Version : "";
            HeaderStep.Text = "";

            HomeScroll.Visibility = Visibility.Visible;
            ProcedureRoot.Visibility = Visibility.Collapsed;

            PageList.Children.Clear();
            if (_proc == null) return;

            for (int i = 0; i < _proc.Pages.Length; i++)
            {
                var page = _proc.Pages[i];
                var content = new StackPanel { Orientation = Orientation.Vertical };

                var thumbUri = (page.Part2 is ImagePart) ? ((ImagePart)page.Part2).UriStr : null;
                if (!string.IsNullOrWhiteSpace(thumbUri))
                {
                    try
                    {
                        var thumb = new Image
                        {
                            Height = 140,
                            Stretch = Stretch.Uniform,
                            HorizontalAlignment = HorizontalAlignment.Stretch,
                            VerticalAlignment = VerticalAlignment.Center,
                            ClipToBounds = true,
                            Margin = new Thickness(0, 0, 0, 8)
                        };
                        RenderOptions.SetBitmapScalingMode(thumb, BitmapScalingMode.Fant);
                        thumb.Source = new BitmapImage(new Uri(thumbUri, UriKind.RelativeOrAbsolute));
                        content.Children.Add(thumb);
                    }
                    catch { }
                }

                content.Children.Add(new TextBlock { Text = page.Title, FontSize = 16, FontWeight = FontWeights.SemiBold });

                var btn = new Button { Content = content, Style = (Style)FindResource("AlbumButtonStyle"), Tag = i };
                btn.Click += PageButton_Click;
                PageList.Children.Add(btn);
            }

            PrevBtn.IsEnabled = false;
            NextBtn.IsEnabled = false;
        }

        private void RenderPage()
        {
            if (_proc == null || _pageIndex < 0) return;

            var page = _proc.Pages[_pageIndex];

            HeaderTitle.Text = _proc.Name;
            HeaderVersion.Text = !string.IsNullOrWhiteSpace(_proc.Version) ? "v" + _proc.Version : "";
            HeaderStep.Text = page.Title + " — " + (_pageIndex + 1).ToString() + "/" + _proc.Pages.Length.ToString();

            HomeScroll.Visibility = Visibility.Collapsed;
            ProcedureRoot.Visibility = Visibility.Visible;

            BuildPartToHost(page.Part1, Part1Host, Col1);
            BuildPartToHost(page.Part2, Part2Host, Col2);
            BuildPartToHost(page.Part3, Part3Host, Col3);

            PrevBtn.IsEnabled = _pageIndex > 0;
            NextBtn.IsEnabled = _pageIndex < _proc.Pages.Length - 1;
        }

        private static void BuildPartToHost(PartSpec part, ContentPresenter host, ColumnDefinition col)
        {
            if (part == null)
            {
                host.Content = null;
                host.Visibility = Visibility.Collapsed;
                col.Width = new GridLength(0);
                return;
            }

            UIElement content = null;
            try { content = part.Build(); }
            catch (Exception ex) { content = Placeholder("Part error:\n" + ex.Message); }

            host.Content = content;
            host.Visibility = Visibility.Visible;
            col.Width = new GridLength(1, GridUnitType.Star);
        }

        // Events
        private void PageButton_Click(object sender, RoutedEventArgs e)
        {
            if (_proc == null) return;
            var btn = sender as Button;
            if (btn != null && btn.Tag is int)
            {
                int idx = (int)btn.Tag;
                if (idx >= 0 && idx < _proc.Pages.Length) { _pageIndex = idx; RenderPage(); }
            }
        }
        private void HomeBtn_Click(object sender, RoutedEventArgs e) { RenderHome(); }
        private void PrevBtn_Click(object sender, RoutedEventArgs e) { if (_proc == null || _pageIndex <= 0) return; _pageIndex--; RenderPage(); }
        private void NextBtn_Click(object sender, RoutedEventArgs e) { if (_proc == null || _pageIndex >= _proc.Pages.Length - 1) return; _pageIndex++; RenderPage(); }

        private void Root_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (_pageIndex < 0) return;
            if (e.Key == System.Windows.Input.Key.Left) PrevBtn_Click(this, new RoutedEventArgs());
            if (e.Key == System.Windows.Input.Key.Right) NextBtn_Click(this, new RoutedEventArgs());
        }

        // Placeholder
        private static Border Placeholder(string text)
        {
            return new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(229, 231, 235)),
                BorderThickness = new Thickness(1),
                Child = new TextBlock
                {
                    Text = text,
                    TextAlignment = TextAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(12),
                    Foreground = Brushes.Gray
                }
            };
        }
    }
}
