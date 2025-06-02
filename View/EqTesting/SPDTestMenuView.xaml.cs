using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace HouseholdMS.View.EqTesting
{
    public partial class SPDTestMenuView : UserControl
    {
        private int currentStep = 1;
        private const int maxStep = 6;

        private readonly Dictionary<int, UIElement> stepPanels;
        private readonly Dictionary<int, string> stepTitles;
        private readonly Dictionary<int, string> stepInstructions;

        public SPDTestMenuView(string userRole = "Admin")
        {
            InitializeComponent();

            stepPanels = new Dictionary<int, UIElement>
            {
                { 1, Step1Panel },
                { 2, Step2Panel },
                { 3, Step3Panel },
                { 4, Step4Panel },
                { 5, Step5Panel },
                { 6, Step6Panel }
            };

            stepTitles = new Dictionary<int, string>
            {
                { 1, "Step 1: Enter SPD Test Info" },
                { 2, "Step 2: Connect Device" },
                { 3, "Step 3: Perform Test" },
                { 4, "Step 4: Check for Abnormality" },
                { 5, "Step 5: Save & Export Results" },
                { 6, "Step 6: Finalize Test" }
            };

            stepInstructions = new Dictionary<int, string>
            {
                { 1, "Input SPD name, serial number, and installation location." },
                { 2, "Connect the SPD test device and verify all settings." },
                { 3, "Execute the SPD test. Wait for the system to collect data." },
                { 4, "If abnormal behavior is detected, disconnect the cable and shut down safely." },
                { 5, "Store test results and optionally export to PDF or print." },
                { 6, "Test finished. Click Finish to return to the main menu." }
            };

            ShowStep(currentStep);
        }

        private void OnNextStep(object sender, RoutedEventArgs e)
        {
            if (currentStep < maxStep)
            {
                currentStep++;
                ShowStep(currentStep);
            }
        }

        private void OnPrevStep(object sender, RoutedEventArgs e)
        {
            if (currentStep > 1)
            {
                currentStep--;
                ShowStep(currentStep);
            }
        }

        private void OnClose(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("❌ Test canceled. Returning to the main menu.");
            // TODO: Replace with actual navigation logic
        }

        private void ShowStep(int step)
        {
            foreach (var panel in stepPanels.Values)
                panel.Visibility = Visibility.Collapsed;

            if (stepPanels.ContainsKey(step))
                stepPanels[step].Visibility = Visibility.Visible;

            StepTitle.Text = stepTitles.TryGetValue(step, out var title) ? title : "SPD Test";
            StepInstruction.Text = stepInstructions.TryGetValue(step, out var instruction) ? instruction : "";
        }
    }
}