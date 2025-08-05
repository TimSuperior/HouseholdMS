using System;

public class Household
{
    public int HouseholdID { get; set; }
    public string OwnerName { get; set; }
    public string UserName { get; set; }
    public string Municipality { get; set; }
    public string District { get; set; }
    public string ContactNum { get; set; }
    public DateTime InstallDate { get; set; }     // Non-nullable!
    public DateTime LastInspect { get; set; }     // Non-nullable!
    public string UserComm { get; set; }
    public string Statuss { get; set; }
}
