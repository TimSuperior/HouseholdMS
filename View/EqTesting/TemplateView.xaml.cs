using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace HouseholdMS.View.EqTesting
{
    /// <summary>
    /// Reusable, non-MVVM checklist wizard.
    /// Another dev edits only the constructor "EDIT REGION" or calls the public LoadProcedure overload.
    /// </summary>
    public partial class TemplateView : UserControl
    {
        // ===== Minimal private internals =====
        private sealed class Step { public readonly string Text; public Step(string t) { Text = t; } }

        private sealed class Page
        {
            public readonly string Title;
            public readonly string ImageUri;     // pack:// or file path
            public readonly Step[] Steps;
            public readonly bool[] Checked;      // runtime state preserved across nav
            public Page(string title, string imageUri, params string[] steps)
            {
                Title = title ?? "";
                ImageUri = imageUri ?? "";
                Steps = steps.Select(s => new Step(s)).ToArray();
                Checked = new bool[Steps.Length];
            }
        }

        private sealed class Procedure
        {
            public readonly string Name;
            public readonly string Version;
            public readonly Page[] Pages;
            public Procedure(string name, string version, params Page[] pages)
            {
                if (pages == null || pages.Length == 0)
                    throw new ArgumentException("At least one page is required.");
                Name = name ?? "Procedure";
                Version = string.IsNullOrWhiteSpace(version) ? "" : version;
                Pages = pages;
            }
        }

        // ===== Public result for hosts that want to save to DB =====
        public sealed class ProcedureResult
        {
            public string Name { get; }
            public string Version { get; }
            public bool[][] ChecksPerPage { get; }   // [page][step]
            internal ProcedureResult(string name, string version, bool[][] checks)
            { Name = name; Version = version; ChecksPerPage = checks; }
        }

        // ===== Optional simple spec type for public API =====
        public sealed class PageSpec
        {
            public string Title { get; }
            public string ImageUri { get; }
            public string[] Steps { get; }
            public PageSpec(string title, string imageUri, params string[] steps)
            { Title = title; ImageUri = imageUri; Steps = steps ?? Array.Empty<string>(); }
        }

        // ===== State =====
        private Procedure _proc;
        private int _index = -1;

        // Event host can subscribe to
        public event Action<ProcedureResult> Finished;

        public TemplateView()
        {
            InitializeComponent();

            // ---------- EDIT ONLY THIS REGION (or call public LoadProcedure from outside) ----------
            LoadProcedure(
                "Battery Maintenance", "1.0",
                new PageSpec("Battery Visual Inspection", "pack://application:,,,/Assets/Template/1img.png",
                    "Power off system and disconnect charger.",
                    "Verify terminals are tight and corrosion-free.",
                    "Check case for cracks or swelling."),
                new PageSpec("Wiring Check", "pack://application:,,,/Assets/Template/2img.png",
                    "Confirm polarity matches diagram.",
                    "Inspect insulation for damage.",
                    "Secure all cable ties."),
                new PageSpec("Voltage Measurement", "pack://application:,,,/Assets/Template/3img.png",
                    "Set multimeter to DC voltage.",
                    "Measure voltage at terminals.",
                    "Record readings for all batteries.")
            );
            // ---------- END EDIT REGION ----------
        }

        /// <summary>
        /// Public API: define a procedure without exposing internal types.
        /// </summary>
        public void LoadProcedure(string name, string version, params PageSpec[] pages)
        {
            if (pages == null || pages.Length == 0)
                throw new ArgumentException("At least one page is required.", nameof(pages));

            var pgs = pages.Select(ps => new Page(ps.Title, ps.ImageUri, ps.Steps)).ToArray();
            _proc = new Procedure(name, version, pgs);
            _index = 0;
            Render();
        }

        // ===== Rendering & navigation =====
        private void Render()
        {
            if (_proc == null || _index < 0 || _index >= _proc.Pages.Length) return;

            var p = _proc.Pages[_index];

            HeaderTitle.Text = _proc.Name;
            HeaderVersion.Text = string.IsNullOrWhiteSpace(_proc.Version) ? "" : ("v" + _proc.Version);
            HeaderStep.Text = $"Step {_index + 1} / {_proc.Pages.Length}";

            PageTitle.Text = p.Title;

            try
            {
                PageImage.Source = string.IsNullOrWhiteSpace(p.ImageUri)
                    ? null
                    : new BitmapImage(new Uri(p.ImageUri, UriKind.RelativeOrAbsolute));
            }
            catch { PageImage.Source = null; }

            StepsHost.Children.Clear();
            for (int i = 0; i < p.Steps.Length; i++)
            {
                var cb = new CheckBox
                {
                    Content = p.Steps[i].Text,
                    Margin = new Thickness(0, 6, 0, 0),
                    IsChecked = p.Checked[i]
                };
                cb.Checked += StepChanged;
                cb.Unchecked += StepChanged;
                StepsHost.Children.Add(cb);
            }

            UpdateNav();
        }

        private void StepChanged(object sender, RoutedEventArgs e)
        {
            SaveState();
            UpdateNav();
        }

        private void SaveState()
        {
            if (_proc == null || _index < 0) return;
            var p = _proc.Pages[_index];
            for (int i = 0; i < p.Checked.Length; i++)
            {
                var cb = (CheckBox)StepsHost.Children[i];
                p.Checked[i] = cb.IsChecked == true;
            }
        }

        private bool PageComplete()
        {
            var p = _proc.Pages[_index];
            for (int i = 0; i < p.Checked.Length; i++)
                if (!p.Checked[i]) return false;
            return true;
        }

        private void UpdateNav()
        {
            if (_proc == null) return;
            bool first = _index == 0;
            bool last = _index == _proc.Pages.Length - 1;
            bool done = PageComplete();

            PrevBtn.IsEnabled = !first;
            NextBtn.IsEnabled = !last && done;
            FinishBtn.Visibility = last ? Visibility.Visible : Visibility.Collapsed;
            FinishBtn.IsEnabled = last && done;

            StatusText.Text = done
                ? "All steps completed. You can proceed."
                : "Complete all steps to proceed.";
        }

        // ===== Buttons =====
        private void PrevBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_proc == null || _index <= 0) return;
            SaveState();
            _index--;
            Render();
        }

        private void NextBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_proc == null || _index >= _proc.Pages.Length - 1) return;
            SaveState();
            if (!PageComplete()) return;
            _index++;
            Render();
        }

        private void FinishBtn_Click(object sender, RoutedEventArgs e)
        {
            SaveState();
            if (!PageComplete()) return;

            // Build simple result and notify host
            var checks = _proc.Pages.Select(pg => (bool[])pg.Checked.Clone()).ToArray();
            Finished?.Invoke(new ProcedureResult(_proc.Name, _proc.Version, checks));

            MessageBox.Show("Procedure completed.");
        }
    }
}
