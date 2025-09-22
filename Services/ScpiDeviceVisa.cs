using System;
using System.Runtime.InteropServices;
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
            _session.TerminationCharacterEnabled = true;   // ReadString uses this; ReadIEEEBlock ignores it
            _session.TerminationCharacter = 10;            // '\n'

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

            try
            {
                _io.WriteString(command);
            }
            catch (COMException ex)
            {
                TryClear();
                if (IsVisaTimeout(ex))
                {
                    // Swallow write timeouts to keep UI responsive; caller can decide what to do next.
                    return;
                }
                throw;
            }
        }

        public string Query(string command)
        {
            if (!IsOpen) throw new InvalidOperationException("VISA session is not open.");
            if (string.IsNullOrEmpty(command)) return string.Empty;
            if (!command.EndsWith("\n")) command += "\n";

            try
            {
                _io.WriteString(command);
            }
            catch (COMException ex)
            {
                TryClear();
                if (IsVisaTimeout(ex)) return string.Empty;
                throw;
            }

            try
            {
                // On some stacks a timeout throws from ReadString().
                return _io.ReadString();
            }
            catch (COMException ex)
            {
                TryClear();
                if (IsVisaTimeout(ex)) return string.Empty;
                throw;
            }
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
            }
            catch (COMException ex)
            {
                TryClear();
                if (timeoutOverrideMs > 0) _session.Timeout = old;
                if (IsVisaTimeout(ex)) return new byte[0];
                throw;
            }

            try
            {
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
            catch (COMException ex)
            {
                TryClear();
                if (IsVisaTimeout(ex)) return new byte[0];
                throw;
            }
            finally
            {
                if (timeoutOverrideMs > 0) _session.Timeout = old;
            }
        }

        private static bool IsVisaTimeout(COMException ex)
        {
            // TekVISA/Keysight VISA COM commonly surfaces this HResult for timeouts.
            // Fallback to message match to be robust across VISA flavors.
            uint h = (uint)ex.HResult;
            if (h == 0x80040011) return true; // "Timeout expired before operation completed."
            string msg = ex.Message ?? string.Empty;
            return msg.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0
                   || msg.IndexOf("time out", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void TryClear()
        {
            try { _session?.Clear(); } catch { /* ignore */ }
        }

        private void CloseInternal()
        {
            try
            {
                if (_io != null)
                {
                    try { _io.IO = null; } catch { }
                    _io = null;
                }
                if (_session != null)
                {
                    try { (_session as IDisposable)?.Dispose(); } catch { }
                    _session = null;
                }
                if (_rm != null)
                {
                    try { (_rm as IDisposable)?.Dispose(); } catch { }
                    _rm = null;
                }
            }
            finally
            {
                IsOpen = false;
            }
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
