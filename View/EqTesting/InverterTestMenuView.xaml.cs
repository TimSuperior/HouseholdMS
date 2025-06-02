using System.Windows;
using System.Windows.Controls;

namespace HouseholdMS.View.EqTesting
{ 
    public partial class InverterTestMenuView : UserControl
    {
        private int currentStep = 0;

        private readonly string[] stepTitles = new string[]
        {
            "1. Input Information (입력 정보)",
            "2. Visual Inspection (육안 점검)",
            "3. Power Switch Test (전원 스위치 점검)",
            "4. Input Voltage Test (입력 전압 점검)",
            "5. Output Voltage Test (출력 전압 점검)",
            "6. Reset Switch Test (리셋 스위치 점검)",
            "7. Save Results & Exit (결과 저장 및 종료)"
        };

        private readonly string[] stepInstructions = new string[]
        {
            "Please enter the basic equipment information. (장비의 기본 정보를 입력해주세요.)",
            "Visually inspect the equipment's exterior and wiring status. (장비의 외관 상태와 배선 상태를 육안으로 점검합니다.)",
            "Check the connection status of the power switch. (전원 스위치 연결 상태를 확인합니다.)",
            "Inspect the input voltage condition of the equipment. (입력 전압 상태를 점검합니다.)",
            "Check the output voltage status of the equipment. (출력 전압 상태를 점검합니다.)",
            "Verify the functionality of the reset switch. (리셋 스위치 동작 여부를 확인합니다.)",
            "Save the inspection results and finish the test. (결과를 저장하고 점검을 종료합니다.)"
        };

        public InverterTestMenuView(string userRole = "Admin")
        {
            InitializeComponent();
            LoadStep();
        }

        private void LoadStep()
        {
            StepTitle.Text = stepTitles[currentStep];
            StepInstruction.Text = stepInstructions[currentStep];

            DeviceInfoPanel.Visibility = (currentStep == 0) ? Visibility.Visible : Visibility.Collapsed;
            TestStepStatusText.Visibility = (currentStep != 0 && currentStep != 6) ? Visibility.Visible : Visibility.Collapsed;

            if (TestStepStatusText.Visibility == Visibility.Visible)
                TestStepStatusText.Text = $"✅ Step '{stepTitles[currentStep]}' marked as complete.";
        }

        private void OnPrevStep(object sender, RoutedEventArgs e)
        {
            if (currentStep > 0)
            {
                currentStep--;
                LoadStep();
            }
        }

        private void OnNextStep(object sender, RoutedEventArgs e)
        {
            if (currentStep < stepTitles.Length - 1)
            {
                currentStep++;
                LoadStep();
            }
        }

        private void OnClose(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Test closed. Returning to main screen.");
            // Optional: Notify parent or navigate back
        }
    }
}