using System;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HouseholdMS.Services
{
    public interface IScpiDevice : IDisposable
    {
        void Connect();
        void Disconnect();
        bool IsConnected { get; }
        string Query(string command);
        Task<string> QueryAsync(string command);
        void Write(string command);
        Task WriteAsync(string command);
        string ReadMeasurement();
        Task<string> ReadMeasurementAsync();
        string ReadDeviceID();
        Task<string> ReadDeviceIDAsync();
        void SetFunction(string function);
        Task SetFunctionAsync(string function);
    }

    public class ScpiDevice : IScpiDevice
    {
        protected SerialPort _port;
        protected readonly object _ioLock = new object();

        public string PortName { get; }
        public int BaudRate { get; }
        public Parity Parity { get; }
        public int DataBits { get; }
        public StopBits StopBits { get; }
        public Handshake Handshake { get; }
        public string LineEnding { get; private set; } = "\n";

        public bool IsConnected => _port != null && _port.IsOpen;

        public ScpiDevice(
            string portName,
            int baudRate = 9600,
            Parity parity = Parity.None,
            int dataBits = 8,
            StopBits stopBits = StopBits.One,
            Handshake handshake = Handshake.None,
            string lineEnding = "\n")
        {
            PortName = portName ?? throw new ArgumentNullException(nameof(portName));
            BaudRate = baudRate;
            Parity = parity;
            DataBits = dataBits;
            StopBits = stopBits;
            Handshake = handshake;
            LineEnding = lineEnding;
        }

        public virtual void Connect()
        {
            Disconnect();

            try
            {
                _port?.Dispose();

                _port = new SerialPort(PortName, BaudRate, Parity, DataBits)
                {
                    StopBits = StopBits,
                    Handshake = Handshake,
                    ReadTimeout = 2000,
                    WriteTimeout = 2000,
                    NewLine = LineEnding,
                    DtrEnable = true,
                    RtsEnable = false,
                    Encoding = Encoding.ASCII
                };

                _port.Open();

                try
                {
                    EnsureDeviceResponds(); // Try reading ID
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Warning: Device not responding properly. " + ex.Message);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to open port {PortName}: {ex.Message}", ex);
            }
        }

        public virtual void Disconnect()
        {
            try
            {
                if (_port != null && _port.IsOpen)
                    _port.Close();
            }
            catch { }
        }

        public virtual void Write(string command)
        {
            Console.WriteLine("Writing SCPI: " + command);
            if (!IsConnected)
                throw new InvalidOperationException("Device not connected.");

            lock (_ioLock)
            {
                try
                {
                    _port.DiscardInBuffer();
                    _port.DiscardOutBuffer();
                    _port.Write(command + LineEnding);
                }
                catch (Exception ex)
                {
                    throw new IOException($"Write failed: {ex.Message}", ex);
                }
            }
        }

        public Task WriteAsync(string command)
        {
            return Task.Run(() => Write(command));
        }

        public virtual string Query(string command)
        {
            if (!IsConnected)
                throw new InvalidOperationException("Device not connected.");

            lock (_ioLock)
            {
                try
                {
                    _port.DiscardInBuffer();
                    _port.DiscardOutBuffer();
                    _port.Write(command + LineEnding);
                    return _port.ReadLine();
                }
                catch (TimeoutException ex)
                {
                    throw new TimeoutException($"Timeout on command '{command}'", ex);
                }
                catch (Exception ex)
                {
                    throw new IOException($"Query failed on command '{command}': {ex.Message}", ex);
                }
            }
        }

        public Task<string> QueryAsync(string command)
        {
            return Task.Run(() => Query(command));
        }

        private string QuerySafe(string command)
        {
            try
            {
                return Query(command);
            }
            catch (TimeoutException)
            {
                string original = LineEnding;
                try
                {
                    SetLineEnding("\r\n");
                    return Query(command);
                }
                catch
                {
                    throw;
                }
                finally
                {
                    SetLineEnding(original);
                }
            }
        }

        private string QueryFirstAvailable(params string[] queries)
        {
            TimeoutException lastTimeout = null;

            foreach (var cmd in queries)
            {
                try { return QuerySafe(cmd); }
                catch (TimeoutException tex) { lastTimeout = tex; }
            }

            throw lastTimeout ?? new TimeoutException("All SCPI queries failed.");
        }

        private void EnsureDeviceResponds()
        {
            string id = ReadDeviceID();

            if (string.IsNullOrWhiteSpace(id))
                throw new IOException("Device returned empty ID.");

            if (!id.ToLowerInvariant().Contains("mp730889"))
                throw new IOException("Unexpected device ID: " + id);
        }

        public virtual string ReadMeasurement()
        {
            return QuerySafe("MEAS?");
        }

        public virtual Task<string> ReadMeasurementAsync()
        {
            return Task.Run(() => ReadMeasurement());
        }

        public virtual string ReadDeviceID()
        {
            return QuerySafe("*IDN?");
        }

        public virtual Task<string> ReadDeviceIDAsync()
        {
            return Task.Run(() => ReadDeviceID());
        }

        public virtual void SetFunction(string function)
        {
            Console.WriteLine("Setting function: " + function);
            if (string.IsNullOrWhiteSpace(function))
                throw new ArgumentException("Function cannot be null or empty.");

            string scpi;


            if (function.Contains(":"))
            {
                scpi = function;
            }
            else
            {
                string f = function.ToLowerInvariant();

                switch (f)
                {
                    case "volt": scpi = "CONFigure:VOLTage:DC"; break;
                    case "volt:ac": scpi = "CONFigure:VOLTage:AC"; break;
                    case "curr": scpi = "CONFigure:CURRent:DC"; break;
                    case "curr:ac": scpi = "CONFigure:CURRent:AC"; break;
                    case "res": scpi = "CONFigure:RESistance"; break;
                    case "cont": scpi = "CONFigure:CONTinuity"; break;
                    case "dio":
                    case "diode": scpi = "CONFigure:DIODe"; break;
                    case "cap": scpi = "CONFigure:CAPacitance"; break;
                    case "freq": scpi = "CONFigure:FREQuency"; break;
                    case "per":
                    case "period": scpi = "CONFigure:PERiod"; break;
                    case "temp:rtd": scpi = "CONFigure:TEMPerature:RTD"; break;
                    default: throw new ArgumentException("Unknown function: " + function);
                }
            }

            try
            {
                Console.WriteLine("[DEBUG] Setting SCPI function: " + scpi);
                Write("SYST:REM"); // ← force remote mode
                Thread.Sleep(100);
                Write(scpi);
                Thread.Sleep(200); // ← allow switch delay
                var resp = QueryFirstAvailable("FUNC?", "FUNCtion?");
                Console.WriteLine("[DEBUG] Device confirmed: " + resp);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to set function '{function}': {ex.Message}", ex);
            }
        }

        public Task SetFunctionAsync(string function)
        {
            return Task.Run(() => SetFunction(function));
        }

        public void SetLineEnding(string newEnding)
        {
            LineEnding = newEnding;
            if (_port != null)
                _port.NewLine = newEnding;
        }

        public void Dispose()
        {
            try
            {
                Disconnect();
                _port?.Dispose();
                _port = null;
            }
            catch { }
        }
    }
}
