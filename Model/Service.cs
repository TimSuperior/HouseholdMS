using System;

namespace HouseholdMS.Model
{
    /// <summary>
    /// Service ticket created automatically when a Household moves to "In Service".
    /// Open = FinishDate == null
    /// </summary>
    public class Service
    {
        public int ServiceID { get; set; }
        public int HouseholdID { get; set; }

        // Optional until you assign in the Tile 2 UI
        public int? TechnicianID { get; set; }

        // Free-text fields you fill while servicing
        public string Problem { get; set; }
        public string Action { get; set; }
        public string InventoryUsed { get; set; }

        // SQLite stores TEXT timestamps (datetime('now')); DateTime is fine in code
        public DateTime StartDate { get; set; }       // set automatically on creation
        public DateTime? FinishDate { get; set; }     // set when service finishes

        // Convenience flag for filtering/binding
        public bool IsOpen => !FinishDate.HasValue;
    }
}
