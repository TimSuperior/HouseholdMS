using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace HouseholdMS.View.EqTesting
{
    public partial class AllTestMenuView : UserControl
    {
        private readonly List<TestStep> steps;
        private int currentStepIndex = 0;
        private readonly List<BitmapImage> uploadedImages = new List<BitmapImage>();


        public AllTestMenuView(string userRole = "Admin")
        {
            InitializeComponent();
            steps = LoadTestSteps();
            DisplayCurrentStep();
        }

        private List<TestStep> LoadTestSteps()
        {
            return new List<TestStep>
            {
                new TestStep("1. 점검 정보 입력 (Input Inspection Info)", "Enter device ID, location, technician name, and inspection date/time."),
                new TestStep("2. 점검 항목 선택 (Select Inspection Items)", "Select the inspection items to include in this test session."),
                new TestStep("3. 육안검사 (Visual Inspection)", "Check for corrosion, loose wires, physical damage. Take a photo if needed.", true),
                new TestStep("4. 수동 점검 (Manual Inspection)", "Follow on-screen steps to check wiring, connections, or specific modules."),
                new TestStep("5. 접촉 나사 이상 유무 (Contact Screw Check)", "Check if terminals are loose, burnt, or melted. Record results."),
                new TestStep("6. 절연 상태 점검 (Insulation Status)", "Measure insulation resistance using tools. Confirm safe values."),
                new TestStep("7. MPPT 이상 유무 (MPPT Check)", "Check the MPPT controller display or connection status.", false, true),
                new TestStep("8. 설정 확인 (Settings Verification)", "Compare current settings with reference values. Flag mismatches."),
                new TestStep("9. AC 부하 테스트 (AC Load Test)", "Test AC output. Confirm switching and actual power delivery."),
                new TestStep("10. 장비 전원 차단 및 케이블 분리 (Shutdown & Disconnect)", "Turn off the device and safely disconnect all test cables."),
                new TestStep("11. 결과 저장 및 출력 (Save & Export Results)", "Save test data to DB. Optionally export a PDF report.")
            };
        }

        private void DisplayCurrentStep()
        {
            var step = steps[currentStepIndex];
            StepTitle.Text = step.Title;
            StepInstruction.Text = step.Instruction;

            // Default hide everything
            UserInfoPanel.Visibility = Visibility.Collapsed;
            //ImageUploadPanel.Visibility = Visibility.Collapsed;
            DeviceStatus.Visibility = Visibility.Collapsed;
            InstructionImage.Visibility = Visibility.Collapsed;
            InspectionItemsPanel.Visibility = Visibility.Collapsed;
            MultiImagePanel.Visibility = Visibility.Collapsed;
            AnnotationPanel.Visibility = Visibility.Collapsed;
            AnnotationPanel5.Visibility = Visibility.Collapsed;
            AnnotationPanel6.Visibility = Visibility.Collapsed;
            AnnotationPanel7.Visibility = Visibility.Collapsed;
            AnnotationPanel8.Visibility = Visibility.Collapsed;
            AnnotationPanel9.Visibility = Visibility.Collapsed;
            AnnotationPanel10.Visibility = Visibility.Collapsed;
            // Step-based panel control
            switch (currentStepIndex)
            {
                case 0:
                    UserInfoPanel.Visibility = Visibility.Visible;
                    InstructionImage.Visibility = Visibility.Visible;
                    break;
                case 1:
                    InspectionItemsPanel.Visibility = Visibility.Visible;
                    break;
                case 2:
                    MultiImagePanel.Visibility = Visibility.Visible;
                    break;
                case 3:
                    AnnotationPanel.Visibility = Visibility.Visible;
                    if (uploadedImages.Count > 0)
                    {
                        //AnnotatedImage.Source = uploadedImages[0];
                    }
                    break;
                case 4:
                    AnnotationPanel5.Visibility = Visibility.Visible;
                    break;
                case 5:
                    AnnotationPanel6.Visibility = Visibility.Visible;
                    break;
                case 6:
                    AnnotationPanel7.Visibility = Visibility.Visible;
                    break;
                case 7:
                    AnnotationPanel8.Visibility = Visibility.Visible;
                    break;
                case 8:
                    AnnotationPanel9.Visibility = Visibility.Visible;
                    break;
                case 9:
                    AnnotationPanel10.Visibility = Visibility.Visible;
                    break;
            }

            if (step.RequiresImage)
                //ImageUploadPanel.Visibility = Visibility.Visible;

            if (step.RequiresDeviceStatus)
            {
                DeviceStatus.Visibility = Visibility.Visible;
                DeviceStatus.Text = "MPPT Controller Connected ✅";
            }

            // Lock step 1 inputs after filled
            bool isStep0 = currentStepIndex == 0;
            DeviceIdBox.IsEnabled = isStep0;
            LocationBox.IsEnabled = isStep0;
            TechnicianBox.IsEnabled = isStep0;
        }

        private void OnNextStep(object sender, RoutedEventArgs e)
        {
            if (currentStepIndex == 0)
            {
                if (string.IsNullOrWhiteSpace(DeviceIdBox.Text) ||
                    string.IsNullOrWhiteSpace(LocationBox.Text) ||
                    string.IsNullOrWhiteSpace(TechnicianBox.Text))
                {
                    MessageBox.Show("⚠ Please fill in all required fields: Device ID, Location, and Technician Name.",
                        "Missing Information", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            if (currentStepIndex < steps.Count - 1)
            {
                currentStepIndex++;
                DisplayCurrentStep();
            }
            else
            {
                MessageBox.Show("✅ All 11 test steps completed. You can now save or export the results.",
                    "Test Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void OnPrevStep(object sender, RoutedEventArgs e)
        {
            if (currentStepIndex > 0)
            {
                currentStepIndex--;
                DisplayCurrentStep();
            }
        }

        private void OnClose(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Test session has been closed. Returning to Step 1.",
                            "Session Ended", MessageBoxButton.OK, MessageBoxImage.Information);

            // Reset step index
            currentStepIndex = 0;

            // Clear Step 1 inputs
            DeviceIdBox.Text = string.Empty;
            LocationBox.Text = string.Empty;
            TechnicianBox.Text = string.Empty;

            // Clear uploaded image
            
            uploadedImages.Clear();
            UploadedImageList.ItemsSource = null;

            // Clear Annotation TextBoxes
            AnnotationBox.Text = string.Empty;
            AnnotationBox5.Text = string.Empty;
            AnnotationBox6.Text = string.Empty;
            AnnotationBox7.Text = string.Empty;
            //AnnotationBox8.Text = string.Empty;
            AnnotationBox9.Text = string.Empty;
            AnnotationBox10.Text = string.Empty;

            // Uncheck Inspection CheckBoxes (if named)
            BatteryCheckBox.IsChecked = false;
            InverterCheckBox.IsChecked = false;
            WiringCheckBox.IsChecked = false;
            MPPTCheckBox.IsChecked = false;

            // Reset UI
            DisplayCurrentStep();
        }



        private void OnUploadImage(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg" };
            if (dlg.ShowDialog() == true)
            {
                //UploadedImage.Source = new BitmapImage(new Uri(dlg.FileName));
            }
        }

        private void OnUploadMultipleImages(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Images|*.png;*.jpg;*.jpeg",
                Multiselect = true
            };

            if (dlg.ShowDialog() == true)
            {
                uploadedImages.Clear();
                foreach (var file in dlg.FileNames)
                {
                    var image = new BitmapImage(new Uri(file));
                    uploadedImages.Add(image);
                }

                UploadedImageList.ItemsSource = uploadedImages;
            }
        }

        private class TestStep
        {
            public string Title { get; }
            public string Instruction { get; }
            public bool RequiresImage { get; }
            public bool RequiresDeviceStatus { get; }

            public TestStep(string title, string instruction, bool requiresImage = false, bool requiresDeviceStatus = false)
            {
                Title = title;
                Instruction = instruction;
                RequiresImage = requiresImage;
                RequiresDeviceStatus = requiresDeviceStatus;
            }
        }

        private void OnImageClicked(object sender, MouseButtonEventArgs e)
        {
            var imageControl = sender as Image;
            if (imageControl?.Source is BitmapImage bitmapImage)
            {
                var previewWindow = new Window
                {
                    Title = "🔍 Image Preview",
                    Width = 800,
                    Height = 600,
                    Background = Brushes.Black,
                    Content = CreateZoomViewer(bitmapImage)
                };

                previewWindow.ShowDialog();
            }
        }

        private UIElement CreateZoomViewer(BitmapImage imageSource)
        {
            var img = new Image
            {
                Source = imageSource,
                Stretch = Stretch.Uniform
            };

            var scaleTransform = new ScaleTransform(1.0, 1.0);
            img.RenderTransform = scaleTransform;
            img.RenderTransformOrigin = new Point(0.5, 0.5);

            img.MouseWheel += (s, e) =>
            {
                double zoomDelta = e.Delta > 0 ? 0.1 : -0.1;
                scaleTransform.ScaleX += zoomDelta;
                scaleTransform.ScaleY += zoomDelta;

                // Limit zoom between 0.5x and 5x
                scaleTransform.ScaleX = Math.Max(0.5, Math.Min(5, scaleTransform.ScaleX));
                scaleTransform.ScaleY = Math.Max(0.5, Math.Min(5, scaleTransform.ScaleY));
            };

            return new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = img
            };
        }

    }
}
