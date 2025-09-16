namespace HouseholdMS.Models
{
    public class SequenceStep
    {
        public int Index { get; set; }
        public int DurationMs { get; set; } = 1000;
        public string AcDc { get; set; } = "AC";        // AC|DC
        public string Function { get; set; } = "CURR";  // CURR|RES|VOLT|POW|SHOR
        public double Setpoint { get; set; } = 1.0;
        public double Pf { get; set; } = 1.0;
        public double Cf { get; set; } = 1.41;
        public int Repeat { get; set; } = 1;
        public string Note { get; set; }
    }
}
