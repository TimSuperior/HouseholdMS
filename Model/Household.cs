public class Household
{
    public int HouseholdID { get; set; }
    public string OwnerName { get; set; }
    public string Address { get; set; }
    public string ContactNum { get; set; }
    public string InstDate { get; set; }
    public string LastInspDate { get; set; }
    public string Note { get; set; } // New property for the optional note
}
