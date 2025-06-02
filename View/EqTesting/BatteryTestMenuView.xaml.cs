using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace HouseholdMS.View.EqTesting
{
    /// <summary>
    /// Interaction logic for BatteryTestMenuView.xaml
    /// </summary>
    public partial class BatteryTestMenuView : UserControl
    {
        private int currentStep = 0;

        private readonly List<string> stepTitles = new List<string>
        {
            "1. Input Info (정보 입력)",
            "2. Cell Voltage Test (셀 전압 테스트)",
            "3. BMS Test (BMS 테스트)",
            "4. Cell Balance Test (셀 밸런스 테스트)",
            "5. Charge Test (충전 테스트)",
            "6. Discharge Test (방전 테스트)",
            "7. Save & Exit (저장 및 종료)"
        };

        private readonly List<string> stepInstructions = new List<string>
        {
            "Please enter the device info. (장비 정보를 입력해주세요.)",
            "Check individual cell voltages. (각 셀 전압을 확인합니다.)",
            "Verify BMS communication and wiring. (BMS 연결 및 통신 확인)",
            "Balance battery cells. (셀 밸런싱을 수행합니다.)",
            "Initiate battery charging. (배터리 충전을 시작합니다.)",
            "Begin battery discharge. (배터리 방전을 시작합니다.)",
            "Save results and disconnect device. (결과 저장 후 장비를 분리합니다.)"
        };

        public BatteryTestMenuView(string userRole = "Admin")
        {
            InitializeComponent();
            LoadStep();
        }

        private void LoadStep()
        {
            StepTitle.Text = stepTitles[currentStep];
            StepInstruction.Text = stepInstructions[currentStep];

            StepPanel1.Visibility = Visibility.Collapsed;
            StepPanel2.Visibility = Visibility.Collapsed;
            StepPanel3.Visibility = Visibility.Collapsed;
            StepPanel4.Visibility = Visibility.Collapsed;
            StepPanel5.Visibility = Visibility.Collapsed;
            StepPanel6.Visibility = Visibility.Collapsed;
            StepPanel7.Visibility = Visibility.Collapsed;

            switch (currentStep)
            {
                case 0:
                    StepPanel1.Visibility = Visibility.Visible;
                    break;
                case 1:
                    StepPanel2.Visibility = Visibility.Visible;
                    break;
                case 2:
                    StepPanel3.Visibility = Visibility.Visible;
                    break;
                case 3:
                    StepPanel4.Visibility = Visibility.Visible;
                    break;
                case 4:
                    StepPanel5.Visibility = Visibility.Visible;
                    break;
                case 5:
                    StepPanel6.Visibility = Visibility.Visible;
                    break;
                case 6:
                    StepPanel7.Visibility = Visibility.Visible;
                    break;
            }

            BtnPrev.IsEnabled = currentStep > 0;
            BtnNext.Content = currentStep < stepTitles.Count - 1
                ? "Next ➡ "
                : "Finish ";
        }

        private void OnNextStep(object sender, RoutedEventArgs e)
        {
            if (currentStep < stepTitles.Count - 1)
            {
                currentStep++;
            }
            else
            {
                MessageBox.Show("Test complete. Data saved. (점검 완료. 데이터가 저장되었습니다.)", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            LoadStep();
        }

        private void OnPrevStep(object sender, RoutedEventArgs e)
        {
            if (currentStep > 0)
            {
                currentStep--;
                LoadStep();
            }
        }
    }
}