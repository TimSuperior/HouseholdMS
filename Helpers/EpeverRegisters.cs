// Helpers/EpeverRegisters.cs
namespace HouseholdMS.Helpers
{
    /// <summary>
    /// EPEVER real-time INPUT registers (FC=0x04), chunked for safety. READ-ONLY ONLY.
    /// Addresses gathered from vendor protocol maps for Tracer/XTRA families.
    /// </summary>
    public static class EpeverRegisters
    {
        // -------- Real-time (PV / Battery charge / Load) --------
        // PV: 0x3100..0x3103
        public const ushort PV_START = 0x3100;   // [V, A, P_lo, P_hi]
        public const ushort PV_COUNT = 4;
        public const int PV_VOLT = 0;            // /100 V
        public const int PV_CURR = 1;            // /100 A
        public const int PV_PWR_LO = 2;          // /100 W (lo,hi)
        public const int PV_PWR_HI = 3;

        // Battery CHARGE side: 0x3104..0x3107
        public const ushort BATC_START = 0x3104; // [V, I, P_lo, P_hi]
        public const ushort BATC_COUNT = 4;
        public const int BATC_VOLT = 0;          // /100 V
        public const int BATC_CURR = 1;          // /100 A (charge current)
        public const int BATC_PWR_LO = 2;        // /100 W (lo,hi)
        public const int BATC_PWR_HI = 3;

        // Load output (discharge): 0x310C..0x310F
        public const ushort LOAD_START = 0x310C; // [V, I, P_lo, P_hi]
        public const ushort LOAD_COUNT = 4;
        public const int LOAD_VOLT = 0;          // /100 V
        public const int LOAD_CURR = 1;          // /100 A
        public const int LOAD_PWR_LO = 2;        // /100 W (lo,hi)
        public const int LOAD_PWR_HI = 3;

        // -------- State of Charge --------
        public const ushort SOC_ADDR = 0x311A;   // % (×1)
        public const ushort SOC_COUNT = 1;

        // -------- Temperatures (several firmwares) --------
        // Common set 1:
        public const ushort TEMP1_START = 0x3110; // [Battery, Ambient, Controller] /100 °C (some models)
        public const ushort TEMP1_COUNT = 3;
        public const int TEMP1_BATT = 0;
        public const int TEMP1_AMBIENT = 1;
        public const int TEMP1_CTRL = 2;

        // Alternate set (seen on some maps): 0x311B..0x311D
        public const ushort TEMP2_START = 0x311B; // [Batt probe, Batt internal, Controller] /100 °C
        public const ushort TEMP2_COUNT = 3;

        // -------- Status bitfields --------
        // 0x3200 Battery/Device status; 0x3201 Charging equipment status (stage bits live here)
        public const ushort STAT_START = 0x3200;  // read 2 regs (0x3200, 0x3201)
        public const ushort STAT_COUNT = 2;

        public static string DecodeChargingStageFrom3201(ushort reg3201)
        {
            // D3..D2: 00 None, 01 Float, 02 Boost, 03 Equalize
            int stage = (reg3201 >> 2) & 0x03;
            switch (stage)
            {
                case 1: return "Float";
                case 2: return "Boost";
                case 3: return "Equalize";
                default: return "None";
            }
        }

        // -------- Energy counters (kWh ×0.01) + today’s min/max battery V --------
        public const ushort EV_BATT_VMAX_TODAY = 0x3302;  // /100 V
        public const ushort EV_BATT_VMIN_TODAY = 0x3303;  // /100 V

        // Consumed energy (load side), kWh×0.01: today/month/year/total
        public const ushort EV_CONS_TODAY_LO = 0x3304;
        public const ushort EV_CONS_TODAY_HI = 0x3305;
        public const ushort EV_CONS_MONTH_LO = 0x3306;
        public const ushort EV_CONS_MONTH_HI = 0x3307;
        public const ushort EV_CONS_YEAR_LO = 0x3308;
        public const ushort EV_CONS_YEAR_HI = 0x3309;
        public const ushort EV_CONS_TOTAL_LO = 0x330A;
        public const ushort EV_CONS_TOTAL_HI = 0x330B;

        // Generated energy (PV side), kWh×0.01: today/month/year/total
        public const ushort EV_GEN_TODAY_LO = 0x330C;
        public const ushort EV_GEN_TODAY_HI = 0x330D;
        public const ushort EV_GEN_MONTH_LO = 0x330E;
        public const ushort EV_GEN_MONTH_HI = 0x330F;
        public const ushort EV_GEN_YEAR_LO = 0x3310;
        public const ushort EV_GEN_YEAR_HI = 0x3311;
        public const ushort EV_GEN_TOTAL_LO = 0x3312;
        public const ushort EV_GEN_TOTAL_HI = 0x3313;

        // Optional: CO2 reduction (ton ×0.01)
        public const ushort EV_CO2_TON_LO = 0x3314;
        public const ushort EV_CO2_TON_HI = 0x3315;

        // -------- Rated data (read-only sanity) --------
        public const ushort RATED_INPUT_VOLT = 0x3000;   // /100 V
        public const ushort RATED_CHG_CURR = 0x3005;   // /100 A
        public const ushort RATED_LOAD_CURR = 0x300E;   // /100 A
    }
}
