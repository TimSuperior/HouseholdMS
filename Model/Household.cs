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
        public string UserName { get; set; }
        public string Municipality { get; set; }
        public string District { get; set; }
        public string ContactNum { get; set; }

        // DB stores TEXT dates; you can still use DateTime in code
        public DateTime InstallDate { get; set; }   // NOT NULL in DB
        public DateTime LastInspect { get; set; }   // NOT NULL in DB

        public string UserComm { get; set; }

        // Defaults to Operational to avoid null/status typos
        public string Statuss { get; set; } = HouseholdStatuses.Operational;
    }
}
