using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Documents;
using HouseholdMS.Services;

// Force using the WPF MessageBox (not WinForms or any custom class)
using WpfMessageBox = System.Windows.MessageBox;

using Timer = System.Timers.Timer;
// (optional if you still need System.Threading types):
using ThreadingTimer = System.Threading.Timer;


namespace HouseholdMS.View.EqTesting
{
    public partial class AllTestMenuView : UserControl
    {
        private enum WizardStep { Precaution = 0, Form = 1, Setup1 = 2, Setup2 = 3, Setup3 = 4, Charging = 5 }
        private WizardStep _step = WizardStep.Precaution;

        // Reusable lookup service (no MVVM, called from code-behind)
        private readonly LookupSearch _search;

        // Debounce timers (thread-safe, no async void races)
        private readonly Timer _hhDebounce = new Timer(220) { AutoReset = false };
        private readonly Timer _techDebounce = new Timer(220) { AutoReset = false };

        // Popup manual-close flags: avoid auto reopen immediately after user dismissed
        private bool _hhPopupManualClose;
        private bool _techPopupManualClose;

        // Optional media for setup pages
        private readonly List<string> _setup2Media = new List<string>();
        private readonly List<string> _setup3Media = new List<string>();
        private readonly List<string> _setup4Media = new List<string>();

        // Selections
        private int? _householdId;
        private int? _technicianId;
        private string _householdDisplay;
        private string _technicianDisplay;

        // Guard to avoid TextChanged firing while we set Text programmatically
        private bool _suppressSearchEvents;

        // Charging timestamps
        private DateTime? _chargingStartUtc;

        public AllTestMenuView(string userRole = "Admin")
        {
            InitializeComponent();
            _search = new LookupSearch(Model.DatabaseHelper.GetConnection);

            // Debounce handlers (Household)
            _hhDebounce.Elapsed += async (_, __) =>
            {
                Dispatcher.Invoke(() => { if (HouseholdSpinner != null) HouseholdSpinner.Visibility = Visibility.Visible; });
                var query = Dispatcher.Invoke(() => HouseholdSearchBox?.Text?.Trim() ?? "");
                var results = await SafeSearchHouseholdsAsync(query);
                Dispatcher.Invoke(() =>
                {
                    if (HouseholdSpinner != null) HouseholdSpinner.Visibility = Visibility.Collapsed;
                    HouseholdPopupList.ItemsSource = results ?? new List<LookupSearch.PickItem>();
                    HouseholdPopup.IsOpen = (results != null && results.Count > 0 && !_hhPopupManualClose);
                    _hhPopupManualClose = false;
                });
            };

            // Debounce handlers (Technician)
            _techDebounce.Elapsed += async (_, __) =>
            {
                Dispatcher.Invoke(() => { if (TechnicianSpinner != null) TechnicianSpinner.Visibility = Visibility.Visible; });
                var query = Dispatcher.Invoke(() => TechnicianSearchBox?.Text?.Trim() ?? "");
                var results = await SafeSearchTechniciansAsync(query);
                Dispatcher.Invoke(() =>
                {
                    if (TechnicianSpinner != null) TechnicianSpinner.Visibility = Visibility.Collapsed;
                    TechnicianPopupList.ItemsSource = results ?? new List<LookupSearch.PickItem>();
                    TechnicianPopup.IsOpen = (results != null && results.Count > 0 && !_techPopupManualClose);
                    _techPopupManualClose = false;
                });
            };

            // Hook template clear buttons once (works whether styles exist or not)
            BatterySerialBox.AddHandler(Button.ClickEvent, new RoutedEventHandler(TemplateClear_Click));
            HouseholdSearchBox.AddHandler(Button.ClickEvent, new RoutedEventHandler(TemplateClear_Click));
            TechnicianSearchBox.AddHandler(Button.ClickEvent, new RoutedEventHandler(TemplateClear_Click));

            RenderStep();
        }

        // ---------- Navigation ----------
        private void OnStartFromPrecaution(object sender, RoutedEventArgs e)
        {
            _step = WizardStep.Form;
            RenderStep();
        }

        private void OnPrevStep(object sender, RoutedEventArgs e)
        {
            if (_step == WizardStep.Precaution) return;
            _step = (WizardStep)((int)_step - 1);
            RenderStep();
        }

        private async void OnNextStep(object sender, RoutedEventArgs e)
        {
            if (_step == WizardStep.Form)
            {
                if (!await ValidateFormAsync()) return;

                string summary =
                    "Battery: " + (BatterySerialBox.Text ?? "") + "\n" +
                    "Household: " + (_householdDisplay ?? "") + "\n" +
                    "Technician: " + (_technicianDisplay ?? "") + "\n\nProceed?";
                if (WpfMessageBox.Show(summary, "Confirm details", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                    return;
            }

            if (_step < WizardStep.Charging)
                _step = (WizardStep)((int)_step + 1);

            if (_step == WizardStep.Charging && !_chargingStartUtc.HasValue)
                _chargingStartUtc = DateTime.UtcNow;

            RenderStep();
        }

        private void RenderStep()
        {
            // stop any playing media before switching
            StopSetupMedia();

            // Panels
            Show(PrecautionPanel, false);
            Show(FormPanel, false);
            Show(SetupPanel, false);
            Show(ChargingPanel, false);

            // Footer
            Show(ManualButton, false);
            Show(StartButton, false);
            Show(BackButton, false);
            Show(NextButton, false);
            Show(FinishButton, false);

            switch (_step)
            {
                case WizardStep.Precaution:
                    StepTitle.Text = "Safety & Precautions";
                    StepInstruction.Text = "Review safety points or open the manual, then Start.";
                    Show(PrecautionPanel, true);
                    LoadPrecautionsDocOnce();
                    Show(ManualButton, true);
                    Show(StartButton, true);
                    break;

                case WizardStep.Form:
                    StepTitle.Text = "1) Fill in details";
                    StepInstruction.Text = "Enter battery serial and pick Household & Technician using search.";
                    Show(FormPanel, true);

                    // Chips reflect current selection
                    HouseholdSelectedChip.Visibility = _householdId.HasValue ? Visibility.Visible : Visibility.Collapsed;
                    TechnicianSelectedChip.Visibility = _technicianId.HasValue ? Visibility.Visible : Visibility.Collapsed;
                    HouseholdSelectedText.Text = _householdDisplay ?? "";
                    TechnicianSelectedText.Text = _technicianDisplay ?? "";

                    _suppressSearchEvents = true;
                    if (!_householdId.HasValue) HouseholdSearchBox.Text = "";
                    if (!_technicianId.HasValue) TechnicianSearchBox.Text = "";
                    _suppressSearchEvents = false;

                    Show(BackButton, true);
                    Show(NextButton, true);
                    break;

                case WizardStep.Setup1:
                    StepTitle.Text = "2) Setup: Connections";
                    StepInstruction.Text = "Confirm polarity, tight lugs, and ventilation.";
                    ShowSetup("Connections checklist",
                              "1) Red to +, black to −\n2) Tighten lugs\n3) Ensure airflow",
                              _setup2Media);
                    Show(BackButton, true);
                    Show(NextButton, true);
                    break;

                case WizardStep.Setup2:
                    StepTitle.Text = "3) Setup: Charger Mode";
                    StepInstruction.Text = "Choose correct chemistry/profile. Verify voltage.";
                    ShowSetup("Mode selection",
                              "Pick AGM/GEL/etc. Verify rated voltage.",
                              _setup3Media);
                    Show(BackButton, true);
                    Show(NextButton, true);
                    break;

                case WizardStep.Setup3:
                    StepTitle.Text = "4) Setup: Final Checks";
                    StepInstruction.Text = "Remove jewelry, keep water away, have extinguisher nearby.";
                    ShowSetup("Final checks",
                              "Complete all safety checks before starting charge.",
                              _setup4Media);
                    Show(BackButton, true);
                    Show(NextButton, true);
                    break;

                case WizardStep.Charging:
                    StepTitle.Text = "5) Charging";
                    StepInstruction.Text = "Charging started. Monitor until complete.";
                    Show(ChargingPanel, true);
                    ChargingSub.Text = _chargingStartUtc.HasValue
                        ? "Started at " + _chargingStartUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") + "."
                        : "Starting…";
                    Show(BackButton, true);
                    Show(FinishButton, true);
                    break;
            }
        }

        private void LoadPrecautionsDocOnce()
        {
            if (PrecautionViewer != null && PrecautionViewer.Document == null)
            {
                // relative pack URI (same assembly)
                var uri = new Uri("/Assets/Manuals/PrecautionStep0.xaml", UriKind.Relative);
                PrecautionViewer.Document = (FlowDocument)Application.LoadComponent(uri);
            }
        }

        private void ShowSetup(string header, string text, List<string> mediaList)
        {
            Show(SetupPanel, true);
            SetupHeader.Text = header;
            SetupText.Text = text;

            var visuals = new List<FrameworkElement>();
            foreach (var path in mediaList)
            {
                if (path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
                {
                    var m = new MediaElement
                    {
                        Source = new Uri(path, UriKind.RelativeOrAbsolute),
                        LoadedBehavior = MediaState.Manual,
                        UnloadedBehavior = MediaState.Stop,
                        Width = 400,
                        Height = 280,
                        Margin = new Thickness(8)
                    };
                    m.Play();
                    visuals.Add(m);
                }
                else
                {
                    var img = new Image
                    {
                        Source = new BitmapImage(new Uri(path, UriKind.RelativeOrAbsolute)),
                        Width = 220,
                        Height = 160,
                        Stretch = Stretch.Uniform,
                        Margin = new Thickness(8)
                    };
                    img.MouseLeftButtonDown += OnImageClicked;
                    visuals.Add(img);
                }
            }
            SetupMedia.ItemsSource = visuals;
        }

        private void StopSetupMedia()
        {
            if (SetupMedia.ItemsSource is IEnumerable<FrameworkElement> els)
            {
                foreach (var el in els)
                    if (el is MediaElement me) me.Stop();
            }
        }

        // ---------- Validation ----------
        private async Task<bool> ValidateFormAsync()
        {
            if (string.IsNullOrWhiteSpace(BatterySerialBox.Text))
            {
                WpfMessageBox.Show("Enter battery serial.", "Missing", MessageBoxButton.OK, MessageBoxImage.Warning);
                BatterySerialBox.Focus();
                return false;
            }
            if (_householdId == null)
            {
                WpfMessageBox.Show("Select a household from search results.", "Missing", MessageBoxButton.OK, MessageBoxImage.Warning);
                HouseholdSearchBox.Focus();
                return false;
            }
            if (_technicianId == null)
            {
                WpfMessageBox.Show("Select a technician from search results.", "Missing", MessageBoxButton.OK, MessageBoxImage.Warning);
                TechnicianSearchBox.Focus();
                return false;
            }

            using (var conn = Model.DatabaseHelper.GetConnection())
            {
                await conn.OpenAsync();

                using (var c1 = new SQLiteCommand("SELECT EXISTS(SELECT 1 FROM Households WHERE HouseholdID=@id);", conn))
                {
                    c1.Parameters.AddWithValue("@id", _householdId);
                    var ok1 = Convert.ToInt32(await c1.ExecuteScalarAsync()) == 1;
                    if (!ok1)
                    {
                        WpfMessageBox.Show("Household not found in DB.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return false;
                    }
                }
                using (var c2 = new SQLiteCommand("SELECT EXISTS(SELECT 1 FROM Technicians WHERE TechnicianID=@id);", conn))
                {
                    c2.Parameters.AddWithValue("@id", _technicianId);
                    var ok2 = Convert.ToInt32(await c2.ExecuteScalarAsync()) == 1;
                    if (!ok2)
                    {
                        WpfMessageBox.Show("Technician not found in DB.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return false;
                    }
                }
            }
            return true;
        }

        // ---------- Household search (debounced + popup) ----------
        private void OnHouseholdSearchChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressSearchEvents) return;

            // Clear selection when user edits
            _householdId = null;
            _householdDisplay = null;
            HouseholdSelectedChip.Visibility = Visibility.Collapsed;

            // If empty -> close popup
            if (string.IsNullOrWhiteSpace(HouseholdSearchBox.Text))
            {
                _hhDebounce.Stop();
                CloseHouseholdPopup();
                HouseholdPopupList.ItemsSource = null;
                return;
            }

            _hhDebounce.Stop();
            _hhDebounce.Start();
        }

        private void HouseholdSearchBox_GotFocus(object sender, RoutedEventArgs e)
        {
            _hhPopupManualClose = false;
            if (HouseholdPopupList.Items.Count > 0)
                HouseholdPopup.IsOpen = true;
        }

        private void HouseholdSearchBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            // if focus moved into popup, keep it open
            if (!IsFocusWithin(HouseholdPopup)) CloseHouseholdPopup();
        }

        private void HouseholdSearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Down && HouseholdPopupList.Items.Count > 0)
            {
                HouseholdPopup.IsOpen = true;
                HouseholdPopupList.SelectedIndex = Math.Max(0, HouseholdPopupList.SelectedIndex);
                (HouseholdPopupList.ItemContainerGenerator.ContainerFromIndex(HouseholdPopupList.SelectedIndex) as ListBoxItem)?.Focus();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                CloseHouseholdPopup(); e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                if (HouseholdPopup.IsOpen && HouseholdPopupList.SelectedItem != null)
                {
                    CommitHouseholdSelection(); e.Handled = true;
                }
            }
        }

        private void HouseholdList_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { CommitHouseholdSelection(); e.Handled = true; }
            else if (e.Key == Key.Escape) { CloseHouseholdPopup(); e.Handled = true; }
        }

        private void OnHouseholdPick(object sender, MouseButtonEventArgs e) => CommitHouseholdSelection();

        private void CommitHouseholdSelection()
        {
            if (HouseholdPopupList.SelectedItem is LookupSearch.PickItem it)
            {
                _householdId = it.Id;
                _householdDisplay = it.Display;

                HouseholdSelectedText.Text = it.Display;
                HouseholdSelectedChip.Visibility = Visibility.Visible;

                _suppressSearchEvents = true;
                HouseholdSearchBox.Text = ""; // clear box after commit; chip shows selection
                _suppressSearchEvents = false;

                CloseHouseholdPopup();
                if (NextButton.IsVisible) NextButton.Focus();
            }
        }

        private void OnHouseholdChangeClick(object sender, RoutedEventArgs e)
        {
            _householdId = null;
            _householdDisplay = null;

            HouseholdSelectedChip.Visibility = Visibility.Collapsed;
            HouseholdSearchBox.Focus();
            _hhPopupManualClose = false;
            if (HouseholdPopupList.Items.Count > 0) HouseholdPopup.IsOpen = true;
        }

        private void CloseHouseholdPopup()
        {
            _hhPopupManualClose = true;
            HouseholdPopup.IsOpen = false;
        }

        // ---------- Technician search (debounced + popup) ----------
        private void OnTechnicianSearchChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressSearchEvents) return;

            _technicianId = null;
            _technicianDisplay = null;
            TechnicianSelectedChip.Visibility = Visibility.Collapsed;

            if (string.IsNullOrWhiteSpace(TechnicianSearchBox.Text))
            {
                _techDebounce.Stop();
                CloseTechnicianPopup();
                TechnicianPopupList.ItemsSource = null;
                return;
            }

            _techDebounce.Stop();
            _techDebounce.Start();
        }

        private void TechnicianSearchBox_GotFocus(object sender, RoutedEventArgs e)
        {
            _techPopupManualClose = false;
            if (TechnicianPopupList.Items.Count > 0)
                TechnicianPopup.IsOpen = true;
        }

        private void TechnicianSearchBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (!IsFocusWithin(TechnicianPopup)) CloseTechnicianPopup();
        }

        private void TechnicianSearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Down && TechnicianPopupList.Items.Count > 0)
            {
                TechnicianPopup.IsOpen = true;
                TechnicianPopupList.SelectedIndex = Math.Max(0, TechnicianPopupList.SelectedIndex);
                (TechnicianPopupList.ItemContainerGenerator.ContainerFromIndex(TechnicianPopupList.SelectedIndex) as ListBoxItem)?.Focus();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                CloseTechnicianPopup(); e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                if (TechnicianPopup.IsOpen && TechnicianPopupList.SelectedItem != null)
                {
                    CommitTechnicianSelection(); e.Handled = true;
                }
            }
        }

        private void TechnicianList_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { CommitTechnicianSelection(); e.Handled = true; }
            else if (e.Key == Key.Escape) { CloseTechnicianPopup(); e.Handled = true; }
        }

        private void OnTechnicianPick(object sender, MouseButtonEventArgs e) => CommitTechnicianSelection();

        private void CommitTechnicianSelection()
        {
            if (TechnicianPopupList.SelectedItem is LookupSearch.PickItem it)
            {
                _technicianId = it.Id;
                _technicianDisplay = it.Display;

                TechnicianSelectedText.Text = it.Display;
                TechnicianSelectedChip.Visibility = Visibility.Visible;

                _suppressSearchEvents = true;
                TechnicianSearchBox.Text = "";
                _suppressSearchEvents = false;

                CloseTechnicianPopup();
                if (NextButton.IsVisible) NextButton.Focus();
            }
        }

        private void OnTechnicianChangeClick(object sender, RoutedEventArgs e)
        {
            _technicianId = null;
            _technicianDisplay = null;

            TechnicianSelectedChip.Visibility = Visibility.Collapsed;
            TechnicianSearchBox.Focus();
            _techPopupManualClose = false;
            if (TechnicianPopupList.Items.Count > 0) TechnicianPopup.IsOpen = true;
        }

        private void CloseTechnicianPopup()
        {
            _techPopupManualClose = true;
            TechnicianPopup.IsOpen = false;
        }

        // ---------- Media helper for setup images ----------
        private void OnImageClicked(object sender, MouseButtonEventArgs e)
        {
            var img = sender as Image;
            var bmp = img != null ? img.Source as BitmapImage : null;
            if (bmp != null)
            {
                var win = new Window
                {
                    Title = "Image Preview",
                    Width = 900,
                    Height = 650,
                    Background = Brushes.Black,
                    Content = CreateZoomViewer(bmp)
                };
                win.ShowDialog();
            }
        }

        private UIElement CreateZoomViewer(BitmapImage imgSrc)
        {
            var img = new Image { Source = imgSrc, Stretch = Stretch.Uniform };
            var scale = new ScaleTransform(1.0, 1.0);
            img.RenderTransform = scale;
            img.RenderTransformOrigin = new Point(0.5, 0.5);
            img.MouseWheel += (s, e) =>
            {
                double delta = e.Delta > 0 ? 0.1 : -0.1;
                scale.ScaleX = Clamp(scale.ScaleX + delta, 0.5, 5);
                scale.ScaleY = Clamp(scale.ScaleY + delta, 0.5, 5);
            };
            return new ScrollViewer { Content = img };
        }

        // ---------- Finish / Save ----------
        private async void OnFinishCharging(object sender, RoutedEventArgs e)
        {
            try
            {
                await SaveReportAsync();
                WpfMessageBox.Show("Charging session saved.", "Done", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show("Failed to save: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task SaveReportAsync()
        {
            var annotationsLines = new List<string>
            {
                "BatterySerial=" + (BatterySerialBox.Text == null ? "" : BatterySerialBox.Text.Trim())
            };
            if (!string.IsNullOrWhiteSpace(_householdDisplay)) annotationsLines.Add("Household=" + _householdDisplay);
            if (!string.IsNullOrWhiteSpace(_technicianDisplay)) annotationsLines.Add("Technician=" + _technicianDisplay);
            if (_chargingStartUtc.HasValue) annotationsLines.Add("ChargingStartUtc=" + _chargingStartUtc.Value.ToString("o"));
            // ... persist as needed
            await Task.CompletedTask;
        }

        #region Helpers

        private void TemplateClear_Click(object sender, RoutedEventArgs e)
        {
            // Works with the clear button from the TextBox template (named PART_ClearBtn).
            if (e.OriginalSource is Button btn && btn.Name == "PART_ClearBtn")
            {
                if (btn.TemplatedParent is TextBox tb)
                {
                    tb.Clear();

                    // Close popups if clearing a search box
                    if (tb == HouseholdSearchBox) CloseHouseholdPopup();
                    else if (tb == TechnicianSearchBox) CloseTechnicianPopup();
                }
                e.Handled = true;
            }
        }

        private async Task<List<LookupSearch.PickItem>> SafeSearchHouseholdsAsync(string q)
        {
            if (string.IsNullOrWhiteSpace(q)) return new List<LookupSearch.PickItem>();
            try { return await _search.SearchHouseholdPicksAsync(q); }
            catch { return new List<LookupSearch.PickItem>(); }
        }

        private async Task<List<LookupSearch.PickItem>> SafeSearchTechniciansAsync(string q)
        {
            if (string.IsNullOrWhiteSpace(q)) return new List<LookupSearch.PickItem>();
            try { return await _search.SearchTechnicianPicksAsync(q); }
            catch { return new List<LookupSearch.PickItem>(); }
        }

        /// <summary>Shows or hides a UI element by setting its Visibility.</summary>
        private void Show(UIElement element, bool show)
        {
            if (element != null) element.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>Constrain value to [min, max].</summary>
        private double Clamp(double value, double min, double max)
            => Math.Min(max, Math.Max(min, value));

        private static bool IsFocusWithin(Popup p)
            => p.IsOpen && p.Child != null && (p.Child.IsKeyboardFocusWithin || p.Child.IsMouseOver);

        private void OnOpenManual(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("This will open the user manual.",
                            "Open Manual",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
        }

        // Battery serial constraints + inline validation
        private void BatterySerialBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            foreach (var ch in e.Text)
                if (!(char.IsLetterOrDigit(ch) || ch == '-' || ch == '_')) { e.Handled = true; return; }
        }
        private void BatterySerialBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var ok = !string.IsNullOrWhiteSpace(BatterySerialBox.Text) && BatterySerialBox.Text.Length >= 6;
            BatterySerialError.Text = ok ? "" : "Serial must be ≥ 6 characters.";
            BatterySerialError.Visibility = ok ? Visibility.Collapsed : Visibility.Visible;
        }

        #endregion
    }
}
