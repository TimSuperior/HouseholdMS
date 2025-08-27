using System;
using System.Globalization;
using System.IO;
using System.Windows;

namespace HouseholdMS.Model
{
    /// <summary>
    /// One-file typography + UI scaling:
    /// - BaseFontSize (semantic) stays constant
    /// - FontScale drives AppUiScale (visual zoom) so EVERYTHING scales
    /// - Also exposes AppFontSizeTitle/Label derived from base for consistent proportions
    /// </summary>
    public static class AppTypographySettings
    {
        public static double BaseFontSize { get; private set; } = 14.0; // DIP
        public static double FontScale { get; private set; } = 1.00; // x1.00 (UI zoom)

        // Keep your current look ratios for header/labels (22 and 13 when base is 14)
        private const double TitleRel = 22.0 / 14.0;  // ≈ 1.5714
        private const double LabelRel = 13.0 / 14.0;  // ≈ 0.9286

        private static string Dir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "HouseholdMS");
        private static string PathFile => Path.Combine(Dir, "settings.ini");

        public static void Load()
        {
            try
            {
                if (File.Exists(PathFile))
                {
                    foreach (var line in File.ReadAllLines(PathFile))
                    {
                        var t = line.Trim();
                        if (t.Length == 0 || t.StartsWith("#")) continue;
                        var kv = t.Split(new[] { '=' }, 2);
                        if (kv.Length != 2) continue;

                        if (kv[0].Trim().Equals("BaseFontSize", StringComparison.OrdinalIgnoreCase) &&
                            double.TryParse(kv[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var b))
                            BaseFontSize = b;

                        if (kv[0].Trim().Equals("FontScale", StringComparison.OrdinalIgnoreCase) &&
                            double.TryParse(kv[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var s))
                            FontScale = s;
                    }
                }
            }
            catch { /* keep defaults */ }

            Apply();
        }

        public static void Save()
        {
            if (!Directory.Exists(Dir)) Directory.CreateDirectory(Dir);
            File.WriteAllText(PathFile,
$@"# HouseholdMS global settings (machine-wide)
BaseFontSize={BaseFontSize.ToString(CultureInfo.InvariantCulture)}
FontScale={FontScale.ToString(CultureInfo.InvariantCulture)}
");
        }

        /// <summary>Set base size and visual UI scale, then apply.</summary>
        public static void Set(double baseSize, double scale)
        {
            BaseFontSize = baseSize;
            FontScale = scale;
            Apply();
        }

        /// <summary>Push current values into Application.Resources.</summary>
        public static void Apply()
        {
            void applyCore()
            {
                if (Application.Current == null) return;

                // Semantic font sizes (NOT scaled here) to avoid double-scaling
                var basePx = BaseFontSize;
                Application.Current.Resources["AppFontSize"] = basePx;
                Application.Current.Resources["AppFontSizeTitle"] = basePx * TitleRel;
                Application.Current.Resources["AppFontSizeLabel"] = basePx * LabelRel;

                // Visual zoom used by LayoutTransform (scales everything)
                Application.Current.Resources["AppUiScale"] = FontScale;
            }

            var d = Application.Current?.Dispatcher;
            if (d == null || d.CheckAccess()) applyCore();
            else d.Invoke((Action)applyCore);
        }
    }
}
