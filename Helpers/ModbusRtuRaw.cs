using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Ports;
using System.Text;
using System.Threading;

namespace HouseholdMS.Helpers
{
    /// <summary>
    /// Minimal, robust Modbus RTU client for READ-ONLY ops.
    /// Supports:
    ///  - 0x04 Read Input Registers (existing)
    ///  - 0x2B/0x0E Read Device Identification (new) for generic device names
    /// Adds a global I/O lock to prevent "COMx is denied" when reads overlap.
    /// </summary>
    public static class ModbusRtuRaw
    {
        private static readonly object _ioLock = new object();

        // ---------- Public helpers ----------
        public static double S100(ushort reg) { return reg / 100.0; }            // V/A/°C scaled by 0.01
        public static double PwrFromU32S100(uint raw) { return raw / 100.0; }    // Power scaled by 0.01 W
        public static uint U32(ushort lo, ushort hi) { return (uint)(lo | (hi << 16)); }

        // ---------- 0x04: Read Input Registers ----------
        public static byte[] BuildReadInputRegs(byte slaveId, ushort startAddress, ushort count)
        {
            var frame = new byte[8];
            frame[0] = slaveId;
            frame[1] = 0x04;
            frame[2] = (byte)(startAddress >> 8);
            frame[3] = (byte)(startAddress & 0xFF);
            frame[4] = (byte)(count >> 8);
            frame[5] = (byte)(count & 0xFF);
            ushort crc = Crc16Modbus(frame, 6);
            frame[6] = (byte)(crc & 0xFF);        // CRC lo
            frame[7] = (byte)((crc >> 8) & 0xFF); // CRC hi
            return frame;
        }

        public static ushort[] ReadInputRegisters(string portName, int baud, byte slaveId,
                                                  ushort startAddress, ushort count,
                                                  int timeoutMs = 1500)
        {
            var request = BuildReadInputRegs(slaveId, startAddress, count);
            var resp = ExchangeRtuFixedByteCount(portName, baud, timeoutMs, request, expectFunc: 0x04);

            if (resp == null || resp.Length < 5) throw new Exception("Short/empty response.");
            if (resp[0] != slaveId) throw new Exception("Slave mismatch: got " + resp[0] + ", expected " + slaveId);
            if (resp[1] == 0x84) throw new Exception("Modbus exception code " + resp[2]);
            if (resp[1] != 0x04) throw new Exception("Function mismatch: " + resp[1]);

            int byteCount = resp[2];
            if (byteCount != 2 * count) throw new Exception("ByteCount mismatch: " + byteCount + " != " + (2 * count));

            var regs = new ushort[count];
            for (int i = 0; i < count; i++)
            {
                int idx = 3 + i * 2;
                regs[i] = (ushort)((resp[idx] << 8) | resp[idx + 1]);
            }
            return regs;
        }

        public static ushort[] TryReadInputRegisters(string portName, int baud, byte slaveId,
                                                     ushort startAddress, ushort count,
                                                     int timeoutMs, out string error)
        {
            try
            {
                error = null;
                return ReadInputRegisters(portName, baud, slaveId, startAddress, count, timeoutMs);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }

        // ---------- 0x2B/0x0E: Read Device Identification ----------
        public enum DeviceIdCategory : byte
        {
            Basic = 0x01,    // VendorName (00), ProductCode (01), MajorMinorRevision (02)
            Regular = 0x02,
            Extended = 0x03
        }

        public static Dictionary<byte, string> TryReadDeviceIdentification(
            string portName, int baud, byte slaveId,
            DeviceIdCategory category,
            int timeoutMs,
            out string error)
        {
            try
            {
                var req = BuildReadDeviceId(slaveId, category, startObjectId: 0x00);
                var resp = ExchangeRtuDeviceId(portName, baud, timeoutMs, req, slaveId);

                // resp: addr, 0x2B, 0x0E, readDevIdCode, conformity, moreFollows, nextObjectId, numberOfObjects, [obj*], CRClo, CRChi
                if (resp == null || resp.Length < 11) throw new Exception("Short response.");

                if (resp[1] == 0xAB) // 0x2B | 0x80
                    throw new Exception("Modbus exception on device-id: " + resp[2]);

                if (resp[1] != 0x2B || resp[2] != 0x0E)
                    throw new Exception("Unexpected function/MEI: " + resp[1] + "/" + resp[2]);

                byte n = resp[7];
                int idx = 8;

                var dict = new Dictionary<byte, string>();
                for (int i = 0; i < n; i++)
                {
                    if (idx + 1 >= resp.Length) break;
                    byte objId = resp[idx++];
                    byte len = resp[idx++];
                    if (idx + len > resp.Length) break;

                    // Per spec, text is ASCII
                    string val = Encoding.ASCII.GetString(resp, idx, len);
                    dict[objId] = val;
                    idx += len;
                }

                error = null;
                return dict;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }

        private static byte[] BuildReadDeviceId(byte slaveId, DeviceIdCategory category, byte startObjectId)
        {
            // RTU frame: addr, 0x2B, 0x0E, ReadDevIdCode(0x01..0x04) choose "Read basic/regular/extended", objectId, CRC
            // We use "Read Device ID" with category code:
            // Request PDU: 0x2B, 0x0E, ReadDeviceIdCode(0x01..0x04), objectId
            byte readDevIdCode = (byte)category; // 0x01 basic, 0x02 regular, 0x03 extended (conformity may limit)
            var frame = new byte[6];
            frame[0] = slaveId;
            frame[1] = 0x2B;
            frame[2] = 0x0E;
            frame[3] = readDevIdCode;
            frame[4] = startObjectId;
            ushort crc = Crc16Modbus(frame, 5);
            Array.Resize(ref frame, 7);
            frame[5] = (byte)(crc & 0xFF);
            frame[6] = (byte)((crc >> 8) & 0xFF);
            return frame;
        }

        // ---------- Serial Exchange ----------
        // For 0x04, response includes a ByteCount. Use that to know exact size.
        private static byte[] ExchangeRtuFixedByteCount(string portName, int baud, int timeoutMs, byte[] request, byte expectFunc)
        {
            lock (_ioLock)
            {
                var sp = new SerialPort(portName, baud, Parity.None, 8, StopBits.One)
                {
                    ReadTimeout = timeoutMs,
                    WriteTimeout = timeoutMs,
                    Handshake = Handshake.None,
                    DtrEnable = true,
                    RtsEnable = true
                };

                try
                {
                    sp.Open();
                    sp.DiscardInBuffer();
                    sp.DiscardOutBuffer();
                    sp.Write(request, 0, request.Length);

                    var sw = Stopwatch.StartNew();

                    // Wait until we at least have addr, func, byteCount (3 bytes)
                    while (sp.BytesToRead < 3)
                    {
                        if (sw.ElapsedMilliseconds > timeoutMs)
                            throw new TimeoutException("No response (0 bytes).");
                        Thread.Sleep(5);
                    }

                    var header = new byte[3];
                    ReadExact(sp, header, 0, 3, timeoutMs, sw);

                    if ((header[1] & 0x80) != 0)
                    {
                        var ex = new byte[2];
                        ReadExact(sp, ex, 0, 2, timeoutMs, sw);
                        var respEx = new byte[5] { header[0], header[1], header[2], ex[0], ex[1] };
                        ushort crcCalcEx = Crc16Modbus(respEx, 3);
                        ushort crcRecvEx = (ushort)(respEx[3] | (respEx[4] << 8));
                        if (crcCalcEx != crcRecvEx) throw new Exception("CRC mismatch on exception frame.");
                        return respEx;
                    }

                    if (header[1] != expectFunc)
                        throw new Exception("Unexpected function: " + header[1]);

                    int byteCount = header[2];
                    if (byteCount < 0 || byteCount > 250) throw new Exception("Invalid byteCount " + byteCount);

                    var dataPlusCrc = new byte[byteCount + 2];
                    ReadExact(sp, dataPlusCrc, 0, dataPlusCrc.Length, timeoutMs, sw);

                    var resp = new byte[3 + dataPlusCrc.Length];
                    Buffer.BlockCopy(header, 0, resp, 0, 3);
                    Buffer.BlockCopy(dataPlusCrc, 0, resp, 3, dataPlusCrc.Length);

                    // CRC check
                    ushort crcCalc = Crc16Modbus(resp, resp.Length - 2);
                    ushort crcRecv = (ushort)(resp[resp.Length - 2] | (resp[resp.Length - 1] << 8));
                    if (crcCalc != crcRecv) throw new Exception("CRC mismatch.");
                    return resp;
                }
                finally
                {
                    try { if (sp.IsOpen) sp.Close(); } catch { }
                    sp.Dispose();
                }
            }
        }

        // For 0x2B/0x0E, response length is variable. Parse structure to know when to stop.
        private static byte[] ExchangeRtuDeviceId(string portName, int baud, int timeoutMs, byte[] request, byte expectSlave)
        {
            lock (_ioLock)
            {
                var sp = new SerialPort(portName, baud, Parity.None, 8, StopBits.One)
                {
                    ReadTimeout = timeoutMs,
                    WriteTimeout = timeoutMs,
                    Handshake = Handshake.None,
                    DtrEnable = true,
                    RtsEnable = true
                };

                try
                {
                    sp.Open();
                    sp.DiscardInBuffer();
                    sp.DiscardOutBuffer();
                    sp.Write(request, 0, request.Length);

                    var sw = Stopwatch.StartNew();

                    // Read minimal header: addr, func, meiType, readDevIdCode, conformity, moreFollows, nextObjectId, numberOfObjects
                    var head = new byte[8];
                    ReadExact(sp, head, 0, 8, timeoutMs, sw);

                    if (head[0] != expectSlave) throw new Exception("Slave mismatch: " + head[0]);
                    if (head[1] == 0xAB) // 0x2B|0x80 exception
                    {
                        // read exception code + CRC
                        var rest = new byte[2]; ReadExact(sp, rest, 0, 2, timeoutMs, sw);
                        var exFrame = new byte[5] { head[0], head[1], head[2], rest[0], rest[1] };
                        return exFrame;
                    }
                    if (head[1] != 0x2B || head[2] != 0x0E)
                        throw new Exception("Unexpected function/MEI.");

                    byte numberOfObjects = head[7];

                    // Read objects: [objId, len, data...] repeated
                    var payload = new List<byte>();
                    payload.AddRange(head);

                    for (int i = 0; i < numberOfObjects; i++)
                    {
                        var ol = new byte[2];
                        ReadExact(sp, ol, 0, 2, timeoutMs, sw);
                        payload.AddRange(ol);

                        int len = ol[1];
                        if (len < 0 || len > 252) throw new Exception("Bad object length");

                        var data = new byte[len];
                        if (len > 0) ReadExact(sp, data, 0, len, timeoutMs, sw);
                        payload.AddRange(data);
                    }

                    // CRC
                    var crc = new byte[2];
                    ReadExact(sp, crc, 0, 2, timeoutMs, sw);

                    // Build frame
                    var frame = new byte[payload.Count + 2];
                    payload.CopyTo(frame, 0);
                    frame[frame.Length - 2] = crc[0];
                    frame[frame.Length - 1] = crc[1];

                    // CRC check
                    ushort crcCalc = Crc16Modbus(frame, frame.Length - 2);
                    ushort crcRecv = (ushort)(frame[frame.Length - 2] | (frame[frame.Length - 1] << 8));
                    if (crcCalc != crcRecv) throw new Exception("CRC mismatch.");

                    return frame;
                }
                finally
                {
                    try { if (sp.IsOpen) sp.Close(); } catch { }
                    sp.Dispose();
                }
            }
        }

        private static void ReadExact(SerialPort sp, byte[] buffer, int offset, int count, int timeoutMs, Stopwatch sw)
        {
            int read = 0;
            while (read < count)
            {
                int remaining = timeoutMs - (int)sw.ElapsedMilliseconds;
                if (remaining <= 0) throw new TimeoutException("Timed out while reading serial data (" + read + "/" + count + ").");
                sp.ReadTimeout = remaining;
                int n = sp.Read(buffer, offset + read, count - read);
                if (n <= 0) throw new TimeoutException("Serial read returned no data (" + read + "/" + count + ").");
                read += n;
            }
        }

        private static ushort Crc16Modbus(byte[] data, int len)
        {
            ushort crc = 0xFFFF;
            for (int pos = 0; pos < len; pos++)
            {
                crc ^= data[pos];
                for (int i = 0; i < 8; i++)
                {
                    bool lsb = (crc & 0x0001) != 0;
                    crc >>= 1;
                    if (lsb) crc ^= 0xA001;
                }
            }
            return crc;
        }
    }
}
