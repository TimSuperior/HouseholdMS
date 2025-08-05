using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using System.Data.SQLite; // <-- Updated for SQLite!
using HouseholdMS.Model;

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
            LoadTechnicians();
            LoadHouseholds();
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

        private void LoadTechnicians()
        {
            using (var conn = DatabaseHelper.GetConnection())
            {
                conn.Open();
                using (var cmd = new SQLiteCommand("SELECT TechnicianID, Name FROM Technicians", conn))
                using (var reader = cmd.ExecuteReader())
                {
                    List<object> list = new List<object>();
                    while (reader.Read())
                    {
                        list.Add(new { ID = reader.GetInt32(0), Name = reader.GetString(1) });
                    }
                    TechnicianComboBox.ItemsSource = list;
                    TechnicianComboBox.DisplayMemberPath = "Name";
                    TechnicianComboBox.SelectedValuePath = "ID";
                }
            }
        }

        private void LoadHouseholds()
        {
            using (var conn = DatabaseHelper.GetConnection())
            {
                conn.Open();
                using (var cmd = new SQLiteCommand("SELECT HouseholdID, OwnerName FROM Households", conn))
                using (var reader = cmd.ExecuteReader())
                {
                    List<object> list = new List<object>();
                    while (reader.Read())
                    {
                        list.Add(new { ID = reader.GetInt32(0), Name = reader.GetString(1) });
                    }
                    HouseholdComboBox.ItemsSource = list;
                    HouseholdComboBox.DisplayMemberPath = "Name";
                    HouseholdComboBox.SelectedValuePath = "ID";
                }
            }
        }

        private void DisplayCurrentStep()
        {
            TestStep step = steps[currentStepIndex];
            StepTitle.Text = step.Title;
            StepInstruction.Text = step.Instruction;

            foreach (UIElement panel in new UIElement[] {
                UserInfoPanel, InstructionImage, InspectionItemsPanel,
                MultiImagePanel, AnnotationPanel, AnnotationPanel5, AnnotationPanel6,
                AnnotationPanel7, AnnotationPanel8, AnnotationPanel9, AnnotationPanel10
            })
            {
                panel.Visibility = Visibility.Collapsed;
            }

            if (currentStepIndex == 0) { UserInfoPanel.Visibility = Visibility.Visible; InstructionImage.Visibility = Visibility.Visible; }
            else if (currentStepIndex == 1) InspectionItemsPanel.Visibility = Visibility.Visible;
            else if (currentStepIndex == 2) MultiImagePanel.Visibility = Visibility.Visible;
            else if (currentStepIndex == 3) AnnotationPanel.Visibility = Visibility.Visible;
            else if (currentStepIndex == 4) AnnotationPanel5.Visibility = Visibility.Visible;
            else if (currentStepIndex == 5) AnnotationPanel6.Visibility = Visibility.Visible;
            else if (currentStepIndex == 6) AnnotationPanel7.Visibility = Visibility.Visible;
            else if (currentStepIndex == 7) AnnotationPanel8.Visibility = Visibility.Visible;
            else if (currentStepIndex == 8) AnnotationPanel9.Visibility = Visibility.Visible;
            else if (currentStepIndex == 9) AnnotationPanel10.Visibility = Visibility.Visible;

            DeviceStatus.Visibility = step.RequiresDeviceStatus ? Visibility.Visible : Visibility.Collapsed;
            if (step.RequiresDeviceStatus) DeviceStatus.Text = "MPPT Controller Connected ✅";
        }

        private void OnNextStep(object sender, RoutedEventArgs e)
        {
            if (currentStepIndex == 0)
            {
                if (TechnicianComboBox.SelectedValue == null || HouseholdComboBox.SelectedValue == null)
                {
                    MessageBox.Show("Please select Technician and Household.", "Missing Info", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                int techId = Convert.ToInt32(TechnicianComboBox.SelectedValue);
                int houseId = Convert.ToInt32(HouseholdComboBox.SelectedValue);

                using (var conn = DatabaseHelper.GetConnection())
                {
                    conn.Open();
                    using (var cmd1 = new SQLiteCommand("SELECT COUNT(*) FROM Technicians WHERE TechnicianID = @id", conn))
                    {
                        cmd1.Parameters.AddWithValue("@id", techId);
                        if (Convert.ToInt32(cmd1.ExecuteScalar()) == 0)
                        {
                            MessageBox.Show("Technician does not exist in DB.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            return;
                        }
                    }

                    using (var cmd2 = new SQLiteCommand("SELECT COUNT(*) FROM Households WHERE HouseholdID = @id", conn))
                    {
                        cmd2.Parameters.AddWithValue("@id", houseId);
                        if (Convert.ToInt32(cmd2.ExecuteScalar()) == 0)
                        {
                            MessageBox.Show("Household does not exist in DB.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            return;
                        }
                    }
                }
            }

            if (currentStepIndex < steps.Count - 1)
            {
                currentStepIndex++;
                DisplayCurrentStep();
            }
            else
            {
                SaveTestDataToDatabase();
                MessageBox.Show("✅ All test steps completed and saved.", "Done", MessageBoxButton.OK, MessageBoxImage.Information);
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
            System.Diagnostics.Process.Start(Application.ResourceAssembly.Location);
        }

        private void OnUploadMultipleImages(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dlg = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg", Multiselect = true };
            if (dlg.ShowDialog() == true)
            {
                uploadedImages.Clear();
                foreach (string file in dlg.FileNames)
                {
                    uploadedImages.Add(new BitmapImage(new Uri(file)));
                }
                UploadedImageList.ItemsSource = uploadedImages;
            }
        }

        private void SaveTestDataToDatabase()
        {
            using (var conn = DatabaseHelper.GetConnection())
            {
                conn.Open();
                using (var cmd = new SQLiteCommand(@"INSERT INTO TestReports 
                    (HouseholdID, TechnicianID, TestDate, InspectionItems, Annotations, SettingsVerification, ImagePaths, DeviceStatus)
                    VALUES (@HouseholdID, @TechnicianID, @TestDate, @InspectionItems, @Annotations, @SettingsVerification, @ImagePaths, @DeviceStatus)", conn))
                {
                    cmd.Parameters.AddWithValue("@HouseholdID", HouseholdComboBox.SelectedValue);
                    cmd.Parameters.AddWithValue("@TechnicianID", TechnicianComboBox.SelectedValue);
                    cmd.Parameters.AddWithValue("@TestDate", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    cmd.Parameters.AddWithValue("@InspectionItems", string.Join(", ", GetSelectedItems()));
                    cmd.Parameters.AddWithValue("@Annotations", CombineAnnotations());
                    cmd.Parameters.AddWithValue("@SettingsVerification", "Voltage=220V, Freq=60Hz, PowerLimit=5000W");
                    cmd.Parameters.AddWithValue("@ImagePaths", string.Join(",", uploadedImages.Select(i => i.UriSource != null ? i.UriSource.LocalPath : "")));
                    cmd.Parameters.AddWithValue("@DeviceStatus", DeviceStatus.Text);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        private IEnumerable<string> GetSelectedItems()
        {
            if (BatteryCheckBox.IsChecked == true) yield return "Battery";
            if (InverterCheckBox.IsChecked == true) yield return "Inverter";
            if (WiringCheckBox.IsChecked == true) yield return "Wiring";
            if (MPPTCheckBox.IsChecked == true) yield return "MPPT";
        }

        private string CombineAnnotations()
        {
            List<string> list = new List<string>();
            if (!string.IsNullOrWhiteSpace(AnnotationBox.Text)) list.Add(AnnotationBox.Text);
            if (!string.IsNullOrWhiteSpace(AnnotationBox5.Text)) list.Add(AnnotationBox5.Text);
            if (!string.IsNullOrWhiteSpace(AnnotationBox6.Text)) list.Add(AnnotationBox6.Text);
            if (!string.IsNullOrWhiteSpace(AnnotationBox7.Text)) list.Add(AnnotationBox7.Text);
            if (!string.IsNullOrWhiteSpace(AnnotationBox9.Text)) list.Add(AnnotationBox9.Text);
            if (!string.IsNullOrWhiteSpace(AnnotationBox10.Text)) list.Add(AnnotationBox10.Text);
            return string.Join("\n", list);
        }

        private void OnImageClicked(object sender, MouseButtonEventArgs e)
        {
            if (sender is Image img && img.Source is BitmapImage bmp)
            {
                Window win = new Window
                {
                    Title = "Image Preview",
                    Width = 800,
                    Height = 600,
                    Background = Brushes.Black,
                    Content = CreateZoomViewer(bmp)
                };
                win.ShowDialog();
            }
        }

        private UIElement CreateZoomViewer(BitmapImage imgSrc)
        {
            Image img = new Image { Source = imgSrc, Stretch = Stretch.Uniform };
            ScaleTransform scale = new ScaleTransform(1.0, 1.0);
            img.RenderTransform = scale;
            img.RenderTransformOrigin = new Point(0.5, 0.5);

            img.MouseWheel += (s, e) =>
            {
                double delta = e.Delta > 0 ? 0.1 : -0.1;
                scale.ScaleX = Clamp(scale.ScaleX + delta, 0.5, 5);
                scale.ScaleY = Clamp(scale.ScaleY + delta, 0.5, 5);
            };

            return new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = img
            };
        }

        private double Clamp(double value, double min, double max)
        {
            return (value < min) ? min : (value > max) ? max : value;
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
    }
}
