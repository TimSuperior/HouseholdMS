// Helpers/ModbusRtuRaw.cs
using System;
using System.Diagnostics;
using System.IO.Ports;
using System.Threading;

namespace HouseholdMS.Helpers
{
    /// <summary>
    /// Minimal, robust Modbus RTU client for READ-ONLY ops (0x04).
    /// Adds a global I/O lock to prevent "COMx is denied" when reads overlap.
    /// </summary>
    public static class ModbusRtuRaw
    {
        private static readonly object _ioLock = new object();

        // Build Modbus RTU "Read Input Registers" (0x04)
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
            var resp = ExchangeRtu(portName, baud, timeoutMs, request);

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

        // ---------- Serial Exchange (header → data+CRC), with global lock ----------
        private static byte[] ExchangeRtu(string portName, int baud, int timeoutMs, byte[] request)
        {
            lock (_ioLock)
            {
                var sp = new SerialPort(portName, baud, Parity.None, 8, StopBits.One);
                sp.ReadTimeout = timeoutMs;
                sp.WriteTimeout = timeoutMs;
                sp.Handshake = Handshake.None;
                sp.DtrEnable = true; // many USB–RS485 adaptors prefer these on
                sp.RtsEnable = true;

                try
                {
                    sp.Open();
                    sp.DiscardInBuffer();
                    sp.DiscardOutBuffer();
                    sp.Write(request, 0, request.Length);

                    var sw = Stopwatch.StartNew();

                    while (sp.BytesToRead < 3)
                    {
                        if (sw.ElapsedMilliseconds > timeoutMs)
                            throw new TimeoutException("No response (0 bytes) from device.");
                        Thread.Sleep(5);
                    }

                    var header = new byte[3];
                    ReadExact(sp, header, 0, 3, timeoutMs, sw);

                    // Exception frame path: addr, (func|0x80), code, CRClo, CRChi
                    if ((header[1] & 0x80) != 0)
                    {
                        var crcEx = new byte[2];
                        ReadExact(sp, crcEx, 0, 2, timeoutMs, sw);

                        var exResp = new byte[5];
                        exResp[0] = header[0];
                        exResp[1] = header[1];
                        exResp[2] = header[2];
                        exResp[3] = crcEx[0];
                        exResp[4] = crcEx[1];

                        ushort crcCalcEx = Crc16Modbus(exResp, 3);
                        ushort crcRecvEx = (ushort)(exResp[3] | (exResp[4] << 8));
                        if (crcCalcEx != crcRecvEx) throw new Exception("CRC mismatch on exception frame.");
                        return exResp;
                    }

                    int byteCount = header[2];
                    if (byteCount < 0 || byteCount > 250)
                        throw new Exception("Invalid byteCount " + byteCount);

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

        // ---- Small helpers ----
        public static double S100(ushort reg) { return reg / 100.0; }            // V/A/°C scaled by 0.01
        public static double PwrFromU32S100(uint raw) { return raw / 100.0; }    // Power scaled by 0.01 W
        public static uint U32(ushort lo, ushort hi) { return (uint)(lo | (hi << 16)); }

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
