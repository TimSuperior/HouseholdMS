// Helpers/EpeverRegisters.cs
namespace HouseholdMS.Helpers
{
    /// <summary>
    /// Common EPEVER real-time input registers (0x04 function).
    /// We read a continuous block starting at 0x3100.
    /// </summary>
    public static class EpeverRegisters
    {
        public const ushort BLOCK_START = 0x3100;
        public const ushort BLOCK_COUNT = 27; // covers 0x3100..0x311A inclusive

        // Offsets within that block:
        public const int OFF_PV_VOLT = 0x3100 - BLOCK_START; // /100
        public const int OFF_PV_CURR = 0x3101 - BLOCK_START; // /100
        public const int OFF_PV_PWR_LO = 0x3102 - BLOCK_START; // (lo,hi) /100
        // 0x3103 = PV power hi

        public const int OFF_BAT_VOLT = 0x310C - BLOCK_START; // /100
        public const int OFF_BAT_CURR = 0x310D - BLOCK_START; // /100 (charge current)
        public const int OFF_BAT_PWR_LO = 0x310E - BLOCK_START; // (lo,hi) /100
        // 0x310F = battery charge power hi

        public const int OFF_SOC = 0x311A - BLOCK_START; // % (0..100)
    }
}
