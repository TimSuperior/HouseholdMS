using HouseholdMS.Helpers;
using System;
using System.IO.Ports;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace HouseholdMS.View.UserControls
{
    public partial class EpeverMonitorControl : UserControl
    {
        private CancellationTokenSource _cts;
        private DispatcherTimer _timer;
        private bool _connected;
        private bool _polling;

        public EpeverMonitorControl()
        {
            InitializeComponent();
            RefreshPorts();
            TxtStatus.Text = "Select COM port, set ID, click Connect.";
            this.Unloaded += EpeverMonitorControl_Unloaded;
        }

        private void EpeverMonitorControl_Unloaded(object sender, RoutedEventArgs e)
        {
            // Make sure timers/ports stop when control is removed from visual tree
            Disconnect();
        }

        // --- UI events ---
        private void BtnRefresh_Click(object sender, RoutedEventArgs e) { RefreshPorts(); }

        private void BtnConnect_Click(object sender, RoutedEventArgs e)
        {
            if (CmbPorts.SelectedItem == null)
            {
                MessageBox.Show("Select a COM port first.", "EPEVER Monitor",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            byte unitId;
            if (!byte.TryParse(TxtId.Text.Trim(), out unitId) || unitId < 1 || unitId > 247)
            {
                MessageBox.Show("Device ID must be 1..247.", "EPEVER Monitor",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _cts = new CancellationTokenSource();
            _connected = true;
            SetUiState(true);

            // Auto timer (1s)
            _timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background,
                (s, _) => { if (ChkAuto.IsChecked == true) { var _ = PollOnceSafe(); } }, this.Dispatcher);
            _timer.Start();

            TxtLink.Text = "Connected (idle)";
            TxtLink.Foreground = (System.Windows.Media.Brush)FindResource("Warn");
            TxtStatus.Text = "Connected to " + CmbPorts.SelectedItem + " @ " + GetBaud() + " (ID=" + unitId + ").";
            var __ = PollOnceSafe(); // immediate first read
        }

        private void BtnDisconnect_Click(object sender, RoutedEventArgs e) { Disconnect(); }

        private void BtnRead_Click(object sender, RoutedEventArgs e)
        {
            var _ = PollOnceSafe();
        }

        // --- Internals ---
        private void RefreshPorts()
        {
            var ports = SerialPort.GetPortNames().OrderBy(s =>
            {
                int n;
                if (s.StartsWith("COM", StringComparison.OrdinalIgnoreCase) && int.TryParse(s.Substring(3), out n))
                    return n;
                return int.MaxValue;
            }).ThenBy(s => s).ToList();

            CmbPorts.ItemsSource = ports;
            if (ports.Count > 0) CmbPorts.SelectedIndex = 0;
        }

        private int GetBaud()
        {
            var item = (CmbBaud.SelectedItem as ComboBoxItem);
            int b;
            return (item != null && int.TryParse(item.Content.ToString(), out b)) ? b : 115200;
        }

        private async Task PollOnceSafe()
        {
            if (!_connected || _polling) return;
            _polling = true;
            try
            {
                await Task.Run(delegate { PollOnce(_cts != null ? _cts.Token : CancellationToken.None); });
            }
            catch (Exception ex)
            {
                TxtStatus.Text = "Error: " + ex.Message;
                TxtLink.Text = "Comm error";
                TxtLink.Foreground = (System.Windows.Media.Brush)FindResource("Bad");
            }
            finally
            {
                _polling = false;
            }
        }

        private void PollOnce(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            string port = null;
            this.Dispatcher.Invoke(delegate { port = CmbPorts.SelectedItem != null ? CmbPorts.SelectedItem.ToString() : null; });
            int baud = GetBaud();
            byte unit = byte.Parse(TxtId.Text.Trim());

            // Read 0x3100..0x311A (27 regs)
            ushort[] regs = ModbusRtuRaw.ReadInputRegisters(
                port, baud, unit,
                EpeverRegisters.BLOCK_START,
                EpeverRegisters.BLOCK_COUNT,
                800);

            // Decode
            double pvV = ModbusRtuRaw.S100(regs[EpeverRegisters.OFF_PV_VOLT]);
            double pvA = ModbusRtuRaw.S100(regs[EpeverRegisters.OFF_PV_CURR]);
            uint pvW_raw = ModbusRtuRaw.U32(regs, EpeverRegisters.OFF_PV_PWR_LO);
            double pvW = ModbusRtuRaw.PwrFromU32S100(pvW_raw);

            double batV = ModbusRtuRaw.S100(regs[EpeverRegisters.OFF_BAT_VOLT]);
            double batA = ModbusRtuRaw.S100(regs[EpeverRegisters.OFF_BAT_CURR]);
            uint batW_raw = ModbusRtuRaw.U32(regs, EpeverRegisters.OFF_BAT_PWR_LO);
            double batW = ModbusRtuRaw.PwrFromU32S100(batW_raw);

            int soc = regs[EpeverRegisters.OFF_SOC];

            // Update UI
            this.Dispatcher.Invoke(delegate
            {
                TxtPvV.Text = "Voltage: " + pvV.ToString("F2") + " V";
                TxtPvA.Text = "Current: " + pvA.ToString("F2") + " A";
                TxtPvW.Text = "Power:   " + pvW.ToString("F1") + " W";

                TxtBatV.Text = "Voltage: " + batV.ToString("F2") + " V";
                TxtBatA.Text = "Charge Current: " + batA.ToString("F2") + " A";
                TxtBatW.Text = "Charge Power:   " + batW.ToString("F1") + " W";

                TxtSoc.Text = "SOC: " + soc + " %";
                TxtUpdated.Text = "Last update: " + DateTime.Now.ToString("HH:mm:ss");

                TxtStatus.Text = "OK";
                TxtLink.Text = "Polling";
                TxtLink.Foreground = (System.Windows.Media.Brush)FindResource("Good");

                var head = string.Join(" ", regs.Take(12).Select(r => r.ToString("X4")));
                TxtRaw.Text = "3100..: " + head + " ...";
            });
        }

        private void Disconnect()
        {
            if (_timer != null) _timer.Stop();
            if (_cts != null) _cts.Cancel();
            if (_cts != null) _cts.Dispose();
            _cts = null;
            _connected = false;
            SetUiState(false);

            TxtLink.Text = "Disconnected";
            TxtLink.Foreground = (System.Windows.Media.Brush)FindResource("Bad");
            TxtStatus.Text = "Disconnected.";
        }

        private void SetUiState(bool connected)
        {
            BtnConnect.IsEnabled = !connected;
            BtnDisconnect.IsEnabled = connected;
            BtnRead.IsEnabled = connected;
            ChkAuto.IsEnabled = connected;
            CmbPorts.IsEnabled = !connected;
            CmbBaud.IsEnabled = !connected;
            TxtId.IsEnabled = !connected;
        }
    }
}
