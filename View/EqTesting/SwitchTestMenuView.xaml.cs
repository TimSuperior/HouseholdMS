using System;
using System.Windows;
using System.Windows.Controls;

namespace HouseholdMS.View.EqTesting
{
    public partial class SwitchTestMenuView : UserControl
    {
        private int currentStep = 0;
        private readonly int maxStep = 2;

        private readonly string[] stepTitles = new string[]
        {
            "Step 1: Test Information (점검 정보 입력)",
            "Step 2: Execute Switch Tests (스위치 테스트 수행)",
            "Step 3: Save Result (결과 저장)"
        };

        private readonly string[] stepInstructions = new string[]
        {
            "Enter basic information about the device to begin the test. (장비에 대한 기본 정보를 입력하세요.)",
            "Click each switch test button to perform the test. (각 스위치 테스트 버튼을 눌러 테스트를 수행하세요.)",
            "All tests done. You may save and finish. (모든 테스트가 완료되었습니다. 저장하고 종료하세요.)"
        };

        public SwitchTestMenuView(string userRole = "Admin")
        {
            InitializeComponent();
            DateTimeText.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            ShowStep(currentStep);
        }

        private void ShowStep(int step)
        {
            StepTitle.Text = stepTitles[step];
            StepInstruction.Text = stepInstructions[step];

            TestInfoPanel.Visibility = step == 0 ? Visibility.Visible : Visibility.Collapsed;
            SwitchTestPanel.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
            ResultPanel.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
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
            if (currentStep > 0)
            {
                currentStep--;
                ShowStep(currentStep);
            }
        }


    }
}