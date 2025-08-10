// ScpiDeviceVisa.cs
using System;
using Ivi.Visa.Interop;

namespace HouseholdMS.Services
{
    public class ScpiDeviceVisa : IDisposable
    {
        private ResourceManager _rm;
        private FormattedIO488 _io;

        public bool IsOpen => _io != null;

        public bool Open(string visaResourceString)
        {
            try
            {
                _rm = new ResourceManager();
                _io = new FormattedIO488();
                var session = _rm.Open(visaResourceString, AccessMode.NO_LOCK, 2000, "");
                _io.IO = (IMessage)session;
                return true;
            }
            catch
            {
                _io = null;
                _rm = null;
                return false;
            }
        }

        public void Write(string command)
        {
            if (_io == null) throw new InvalidOperationException("VISA session is not open.");
            _io.WriteString(command + "\n");
        }

        public string Query(string command)
        {
            if (_io == null) throw new InvalidOperationException("VISA session is not open.");
            _io.WriteString(command + "\n");
            return _io.ReadString();
        }

        public byte[] QueryBinary(string command, int maxLength = 4096)
        {
            if (_io == null) throw new InvalidOperationException("VISA session is not open.");
            _io.WriteString(command + "\n");
            object data = _io.ReadIEEEBlock(IEEEBinaryType.BinaryType_UI1, true, true);
            return (byte[])data;
        }

        public void Dispose()
        {
            if (_io != null && _io.IO != null)
            {
                try { System.Runtime.InteropServices.Marshal.ReleaseComObject(_io.IO); } catch { }
            }
            if (_io != null)
            {
                try { System.Runtime.InteropServices.Marshal.ReleaseComObject(_io); } catch { }
            }
            if (_rm != null)
            {
                try { System.Runtime.InteropServices.Marshal.ReleaseComObject(_rm); } catch { }
            }
            _io = null;
            _rm = null;
        }
    }
}
