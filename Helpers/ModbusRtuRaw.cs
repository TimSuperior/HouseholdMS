// Helpers/ModbusRtuRaw.cs
using System;
using System.IO.Ports;

namespace HouseholdMS.Helpers
{
    public static class ModbusRtuRaw
    {
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

        // Read input registers; returns raw 16-bit register array (ushort[])
        public static ushort[] ReadInputRegisters(string portName, int baud, byte slaveId,
                                                  ushort startAddress, ushort count,
                                                  int timeoutMs = 800)
        {
            var request = BuildReadInputRegs(slaveId, startAddress, count);
            var expectedLen = 5 + 2 * count; // addr, func, byteCount, data(2*count), CRC(2)
            byte[] resp = Exchange(portName, baud, timeoutMs, request, expectedLen);

            // Basic sanity
            if (resp[0] != slaveId) throw new Exception("Slave mismatch: got " + resp[0] + ", expected " + slaveId);
            if (resp[1] == 0x84) throw new Exception("Modbus exception: code " + resp[2]);
            if (resp[1] != 0x04) throw new Exception("Function mismatch: " + resp[1]);
            if (resp[2] != 2 * count) throw new Exception("ByteCount mismatch: " + resp[2] + " != " + (2 * count));

            // CRC check
            ushort crcCalc = Crc16Modbus(resp, expectedLen - 2);
            ushort crcRecv = (ushort)(resp[expectedLen - 2] | (resp[expectedLen - 1] << 8));
            if (crcCalc != crcRecv) throw new Exception("CRC mismatch (calc " + crcCalc.ToString("X4") + " != recv " + crcRecv.ToString("X4") + ")");

            // Extract registers
            var regs = new ushort[count];
            for (int i = 0; i < count; i++)
            {
                int idx = 3 + i * 2; // after byteCount
                regs[i] = (ushort)((resp[idx] << 8) | resp[idx + 1]);
            }
            return regs;
        }

        // Low-level exchange (C# 7.3 compatible: no 'using var')
        private static byte[] Exchange(string portName, int baud, int timeoutMs, byte[] request, int expectedLen)
        {
            var sp = new SerialPort(portName, baud, Parity.None, 8, StopBits.One);
            sp.ReadTimeout = timeoutMs;
            sp.WriteTimeout = timeoutMs;
            sp.Handshake = Handshake.None;

            try
            {
                sp.Open();
                sp.DiscardInBuffer();
                sp.DiscardOutBuffer();

                sp.Write(request, 0, request.Length);

                var buf = new byte[expectedLen];
                int read = 0;
                while (read < expectedLen)
                {
                    int b = sp.ReadByte(); // throws on timeout
                    buf[read++] = (byte)b;
                }
                return buf;
            }
            finally
            {
                try
                {
                    if (sp.IsOpen) sp.Close();
                }
                catch { /* ignore */ }
                sp.Dispose();
            }
        }

        // Helpers for decoding
        public static double S100(ushort reg) { return reg / 100.0; } // V/A scaled by 0.01

        public static uint U32(ushort[] regs, int idxLoHi)
        {
            // EPEVER uses two 16-bit regs (Low then High) -> 32-bit unsigned
            return (uint)(regs[idxLoHi] | (regs[idxLoHi + 1] << 16));
        }

        public static double PwrFromU32S100(uint raw) { return raw / 100.0; } // Watts scaled by 0.01

        // Standard CRC16-Modbus
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
