public class InventoryItem
{
    public int ItemID { get; set; }
    public string ItemType { get; set; }
    public int TotalQuantity { get; set; }
    public int UsedQuantity { get; set; }
    public string LastRestockedDate { get; set; }
}
