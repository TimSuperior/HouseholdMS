using System;

public class Household
{
    public int HouseholdID { get; set; }
    public string OwnerName { get; set; }           // Contractor
    public string UserName { get; set; }            // New: From DB
    public string Municipality { get; set; }        // Subdivision/Area
    public string District { get; set; }
    public string ContactNum { get; set; }
    public DateTime InstallDate { get; set; }       // Changed to DateTime
    public DateTime LastInspect { get; set; }       // Changed to DateTime
    public string UserComm { get; set; }            // Renamed from Note
    public string Statuss { get; set; }             // Matches DB
}
