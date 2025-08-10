using System;
using System.Windows;
using System.Windows.Controls;
using HouseholdMS.Services;

namespace HouseholdMS.View.Measurement
{
    public partial class OscilloscopeView : UserControl
    {
        private ScpiDeviceVisa _visa;
        private OscilloscopeService _scope;

        public OscilloscopeView()
        {
            InitializeComponent();
        }

        private void Connect_Click(object sender, RoutedEventArgs e)
        {
            string visaAddress = VisaAddressBox.Text.Trim();
            StatusBlock.Text = "Connecting...";
            ResultBlock.Text = "";

            try
            {
                _visa?.Dispose();
                _visa = new ScpiDeviceVisa();
                if (_visa.Open(visaAddress))
                {
                    _scope = new OscilloscopeService(_visa);
                    StatusBlock.Text = "Connected!";
                }
                else
                {
                    _scope = null;
                    StatusBlock.Text = "Failed to connect.";
                }
            }
            catch (Exception ex)
            {
                StatusBlock.Text = "Error: " + ex.Message;
                _scope = null;
            }
        }

        private void ReadIDN_Click(object sender, RoutedEventArgs e)
        {
            ResultBlock.Text = "";
            if (_scope == null)
            {
                StatusBlock.Text = "Not connected.";
                return;
            }
            try
            {
                StatusBlock.Text = "Reading IDN...";
                string idn = _scope.Identify();
                ResultBlock.Text = "IDN: " + idn.Trim();
                StatusBlock.Text = "Success!";
            }
            catch (Exception ex)
            {
                ResultBlock.Text = "Ошибка: " + ex.Message;
                StatusBlock.Text = "Failed.";
            }
        }

        private void ReadVpp_Click(object sender, RoutedEventArgs e)
        {
            ResultBlock.Text = "";
            if (_scope == null)
            {
                StatusBlock.Text = "Not connected.";
                return;
            }
            try
            {
                StatusBlock.Text = "Measuring Vpp...";
                // Set measurement type and get value (channel 1)
                _scope.ConfigureChannel(1, 1.0, "DC"); // Example setup
                string vpp = _visa.Query("MEASUrement:IMMed:VALue?"); // Can be improved
                ResultBlock.Text = "Vpp: " + vpp.Trim();
                StatusBlock.Text = "Success!";
            }
            catch (Exception ex)
            {
                ResultBlock.Text = "Ошибка: " + ex.Message;
                StatusBlock.Text = "Failed.";
            }
        }

        private void Calibrate_Click(object sender, RoutedEventArgs e)
        {
            ResultBlock.Text = "";
            if (_scope == null)
            {
                StatusBlock.Text = "Not connected.";
                return;
            }
            try
            {
                StatusBlock.Text = "Calibrating...";
                int code = _scope.SelfCalibrate();
                if (code == 0)
                {
                    ResultBlock.Text = "Calibration OK";
                    StatusBlock.Text = "Success!";
                }
                else
                {
                    ResultBlock.Text = "Calibration error: " + code;
                    StatusBlock.Text = "Error!";
                }
            }
            catch (Exception ex)
            {
                ResultBlock.Text = "Ошибка: " + ex.Message;
                StatusBlock.Text = "Failed.";
            }
        }
    }
}
