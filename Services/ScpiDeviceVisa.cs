using System;
using Ivi.Visa.Interop;

namespace HouseholdMS.Services
{
    public sealed class ScpiDeviceVisa : IDisposable
    {
        private ResourceManager _rm;
        private FormattedIO488 _io;
        private IMessage _session;
        private bool _disposed;

        public bool IsOpen { get; private set; }
        private int _timeoutMs = 15000;

        public int TimeoutMs
        {
            get { return (_session != null) ? _session.Timeout : _timeoutMs; }
            set { _timeoutMs = value; if (_session != null) _session.Timeout = value; }
        }

        public bool Open(string resourceAddress, int timeoutMs = 15000)
        {
            CloseInternal();
            _rm = new ResourceManager();
            _session = (IMessage)_rm.Open(resourceAddress, AccessMode.NO_LOCK, timeoutMs, string.Empty);
            _session.Timeout = timeoutMs;
            _session.TerminationCharacterEnabled = true;
            _session.TerminationCharacter = 10; // \n
            TryClear();

            _io = new FormattedIO488 { IO = _session };
            _timeoutMs = timeoutMs;
            IsOpen = true;
            return true;
        }

        public void SetTimeout(int milliseconds) => TimeoutMs = milliseconds;
        public void Clear() => TryClear();

        public void Write(string command)
        {
            if (!IsOpen) throw new InvalidOperationException("VISA session is not open.");
            if (string.IsNullOrEmpty(command)) return;
            if (!command.EndsWith("\n")) command += "\n";
            try { _io.WriteString(command); }
            catch (System.Runtime.InteropServices.COMException) { TryClear(); throw; }
        }

        public string Query(string command)
        {
            if (!IsOpen) throw new InvalidOperationException("VISA session is not open.");
            if (string.IsNullOrEmpty(command)) return string.Empty;
            if (!command.EndsWith("\n")) command += "\n";
            try { _io.WriteString(command); return _io.ReadString(); }
            catch (System.Runtime.InteropServices.COMException) { TryClear(); throw; }
        }

        public byte[] QueryBinary(string command, int timeoutOverrideMs = 0)
        {
            if (!IsOpen) throw new InvalidOperationException("VISA session is not open.");
            if (string.IsNullOrEmpty(command)) return new byte[0];
            if (!command.EndsWith("\n")) command += "\n";

            int old = _session.Timeout;
            if (timeoutOverrideMs > 0) _session.Timeout = timeoutOverrideMs;

            try
            {
                _io.WriteString(command);
                object block = _io.ReadIEEEBlock(IEEEBinaryType.BinaryType_UI1);
                if (block is Array arr)
                {
                    int len = arr.Length;
                    var bytes = new byte[len];
                    for (int i = 0; i < len; i++) bytes[i] = Convert.ToByte(arr.GetValue(i));
                    return bytes;
                }
                return new byte[0];
            }
            catch (System.Runtime.InteropServices.COMException) { TryClear(); throw; }
            finally { if (timeoutOverrideMs > 0) _session.Timeout = old; }
        }

        private void TryClear() { try { _session?.Clear(); } catch { } }

        private void CloseInternal()
        {
            try
            {
                if (_io != null) { try { _io.IO = null; } catch { } _io = null; }
                if (_session != null) { try { (_session as IDisposable)?.Dispose(); } catch { } _session = null; }
                if (_rm != null) { try { (_rm as IDisposable)?.Dispose(); } catch { } _rm = null; }
            }
            finally { IsOpen = false; }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            CloseInternal();
            GC.SuppressFinalize(this);
        }
    }
}
