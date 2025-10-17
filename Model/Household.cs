using System;

namespace HouseholdMS.Model
{
    // Keep status strings consistent across UI/DB
    public static class HouseholdStatuses
    {
        public const string Operational = "Operational";
        public const string InService = "In Service";
        public const string NotOperational = "Not Operational";
    }

    public class Household
    {
        public int HouseholdID { get; set; }

        public string OwnerName { get; set; }

        // NEW: replaces UserName
        public string DNI { get; set; }

        // Address (either Municipality+District OR X+Y per DB CHECK)
        public string Municipality { get; set; } // nullable in DB
        public string District { get; set; } // nullable in DB

        // NEW: coordinate address option
        public double? X { get; set; }
        public double? Y { get; set; }

        public string ContactNum { get; set; }

        // DB stores TEXT dates; you can still use DateTime in code
        public DateTime InstallDate { get; set; }   // NOT NULL in DB
        public DateTime LastInspect { get; set; }   // NOT NULL in DB

        public string UserComm { get; set; }

        // Defaults to Operational to avoid null/status typos
        public string Statuss { get; set; } = HouseholdStatuses.Operational;

        // NEW: series fields (optional)
        public string SP { get; set; }  // Serie Panel
        public string SMI { get; set; }  // Serie Mòdulo Integrado
        public string SB { get; set; }  // Serie Bateria

        // ---- Back-compat shim ----
        // If any old code still references UserName, keep it compiling and map to DNI.
        [Obsolete("Use DNI instead of UserName")]
        public string UserName
        {
            get => DNI;
            set => DNI = value;
        }
    }
}
