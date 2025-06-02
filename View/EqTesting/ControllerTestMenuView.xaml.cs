using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace HouseholdMS.View.EqTesting
{
    public partial class ControllerTestMenuView : UserControl
    {
        private int currentStep = 0;

        private readonly List<string> stepTitles = new List<string>()
        {
            "1. Input Info (입력 정보)",
            "2. Visual Inspection (육안 점검)",
            "3. Manual Test (수동 점검)",
            "4. Screen/Button Check (화면/버튼 점검)",
            "5. Settings Check (설정값 확인)",
            "6. Temp Sensor Check (온도 센서 점검)",
            "7. PV Input Test (기능 점검: PV 입력)",
            "8. Battery Output Test (기능 점검: 배터리 출력)",
            "9. Load Test (기능 점검: 부하)",
            "10. Power Off & Disconnect (전원 차단 및 케이블 분리)",
            "11. Save & Exit (결과 저장 및 종료)"
        };

        private readonly List<string> stepInstructions = new List<string>()
        {
            "Enter the device's basic information. (장비의 기본 정보를 입력합니다.)",
            "Visually inspect the exterior and wiring. (장비의 외관 상태와 배선 상태를 육안으로 점검합니다.)",
            "Perform manual checks following on-screen instructions. (화면의 안내에 따라 사용자가 수동 점검을 진행합니다.)",
            "Check the screen and buttons for abnormalities. (장비의 화면 및 버튼의 상태를 점검합니다.)",
            "Verify that the initial settings are correct. (초기 설정값을 확인하고 이상 여부를 점검합니다.)",
            "Check the temperature sensor connection. (온도 센서 연결 상태를 점검합니다.)",
            "Inspect the PV input status. (PV 입력 연결 상태를 확인합니다.)",
            "Inspect the battery output status. (배터리 출력 상태를 확인합니다.)",
            "Check the load output connection. (부하 연결 상태를 확인합니다.)",
            "Turn off the power and disconnect cables. (전원을 차단하고 연결된 케이블을 분리합니다.)",
            "Save all results and finish the test. (모든 결과를 저장하고 점검을 종료합니다.)"
        };

        public ControllerTestMenuView(string userRole = "Admin")
        {
            InitializeComponent();
            UpdateStepUI();
        }

        private void UpdateStepUI()
        {
            StepTitle.Text = stepTitles[currentStep];
            StepInstruction.Text = stepInstructions[currentStep];

            // Only show input panel during Step 0
            if (UserInfoPanel != null)
            {
                UserInfoPanel.Visibility = (currentStep == 0) ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void OnNextStep(object sender, RoutedEventArgs e)
        {
            if (currentStep < stepTitles.Count - 1)
            {
                currentStep++;
                UpdateStepUI();
            }
        }

        private void OnPrevStep(object sender, RoutedEventArgs e)
        {
            if (currentStep > 0)
            {
                currentStep--;
                UpdateStepUI();
            }
        }

        private void OnClose(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Closing the test module.");
        }
    }
}