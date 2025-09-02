using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace HouseholdMS.View.UserControls
{
    public partial class CircularProgressBar : UserControl
    {
        public CircularProgressBar()
        {
            InitializeComponent();
        }

        // Percentage (0..100)
        public static readonly DependencyProperty PercentageProperty =
            DependencyProperty.Register(
                nameof(Percentage),
                typeof(double),
                typeof(CircularProgressBar),
                new PropertyMetadata(0.0, OnVisualPropertyChanged, CoercePercentage));

        private static object CoercePercentage(DependencyObject d, object baseValue)
        {
            double v = 0.0;
            try { v = Convert.ToDouble(baseValue); } catch { }
            if (v < 0) v = 0;
            if (v > 100) v = 100;
            return v;
        }

        public double Percentage
        {
            get { return (double)GetValue(PercentageProperty); }
            set { SetValue(PercentageProperty, value); }
        }

        // StrokeThickness
        public static readonly DependencyProperty StrokeThicknessProperty =
            DependencyProperty.Register(
                nameof(StrokeThickness),
                typeof(double),
                typeof(CircularProgressBar),
                new PropertyMetadata(8.0, OnVisualPropertyChanged));

        public double StrokeThickness
        {
            get { return (double)GetValue(StrokeThicknessProperty); }
            set { SetValue(StrokeThicknessProperty, value); }
        }

        // ProgressBrush
        public static readonly DependencyProperty ProgressBrushProperty =
            DependencyProperty.Register(
                nameof(ProgressBrush),
                typeof(Brush),
                typeof(CircularProgressBar),
                new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)), OnVisualPropertyChanged));

        public Brush ProgressBrush
        {
            get { return (Brush)GetValue(ProgressBrushProperty); }
            set { SetValue(ProgressBrushProperty, value); }
        }

        // TrackBrush
        public static readonly DependencyProperty TrackBrushProperty =
            DependencyProperty.Register(
                nameof(TrackBrush),
                typeof(Brush),
                typeof(CircularProgressBar),
                new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6)), OnVisualPropertyChanged));

        public Brush TrackBrush
        {
            get { return (Brush)GetValue(TrackBrushProperty); }
            set { SetValue(TrackBrushProperty, value); }
        }

        private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var ctrl = d as CircularProgressBar;
            if (ctrl != null)
                ctrl.UpdateVisuals();
        }

        private void UserControl_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateVisuals();
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateVisuals();
        }

        private void UpdateVisuals()
        {
            double w = this.ActualWidth;
            double h = this.ActualHeight;
            if (w <= 0 || h <= 0) return;

            // Track ellipse fills the control bounds
            if (TrackEllipse != null)
            {
                // Ellipse auto-sizes to the control; no extra work required
            }

            if (Percentage >= 99.999)
            {
                // Full ring visible, hide the arc path
                if (FullRing != null) FullRing.Visibility = Visibility.Visible;
                if (ProgressPath != null) ProgressPath.Visibility = Visibility.Collapsed;
            }
            else if (Percentage <= 0.001)
            {
                // Nothing to draw
                if (FullRing != null) FullRing.Visibility = Visibility.Collapsed;
                if (ProgressPath != null)
                {
                    ProgressPath.Visibility = Visibility.Collapsed;
                    ProgressPath.Data = null;
                }
            }
            else
            {
                if (FullRing != null) FullRing.Visibility = Visibility.Collapsed;
                if (ProgressPath != null)
                {
                    ProgressPath.Visibility = Visibility.Visible;
                    ProgressPath.Data = BuildArcGeometry(Percentage, w, h, StrokeThickness);
                }
            }
        }

        /// <summary>
        /// Builds an arc PathGeometry starting at 12 o'clock and sweeping clockwise
        /// for the given percentage. 100% is handled outside via FullRing.
        /// </summary>
        private static Geometry BuildArcGeometry(double percentage, double width, double height, double strokeThickness)
        {
            // Use the smaller dimension
            double size = Math.Min(width, height);
            double radius = (size - strokeThickness) / 2.0;
            if (radius <= 0) radius = 1;

            // Center point
            double cx = width / 2.0;
            double cy = height / 2.0;

            // Start angle = -90 degrees (12 o'clock)
            double startAngle = -90.0;
            // End angle based on percentage (avoid 360; handled by FullRing)
            double sweepAngle = 360.0 * (percentage / 100.0);
            if (sweepAngle > 359.999) sweepAngle = 359.999;
            double endAngle = startAngle + sweepAngle;

            Point startPoint = PointOnCircle(cx, cy, radius, startAngle);
            Point endPoint = PointOnCircle(cx, cy, radius, endAngle);

            bool isLargeArc = sweepAngle > 180.0;

            var figure = new PathFigure { StartPoint = startPoint, IsClosed = false, IsFilled = false };
            var arc = new ArcSegment
            {
                Point = endPoint,
                Size = new Size(radius, radius),
                IsLargeArc = isLargeArc,
                SweepDirection = SweepDirection.Clockwise
            };
            figure.Segments.Add(arc);

            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            return geometry;
        }

        private static Point PointOnCircle(double cx, double cy, double r, double angleDeg)
        {
            double rad = angleDeg * Math.PI / 180.0;
            double x = cx + r * Math.Cos(rad);
            double y = cy + r * Math.Sin(rad);
            return new Point(x, y);
        }
    }
}
