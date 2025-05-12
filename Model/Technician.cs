public class Technician
{
    public int TechnicianID { get; set; }
    public string Namee { get; set; }
    public string ContactNumm { get; set; }
    public string Addresss { get; set; }
    public string AssignedAreaa { get; set; }
    public string Notee { get; set; }

    public override string ToString()
    {
        return Namee;
        
    }
}
