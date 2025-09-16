using System;

namespace HouseholdMS.Models
{
    public class InstrumentReading
    {
        public DateTime Timestamp { get; set; }
        public double Vrms { get; set; }
        public double Irms { get; set; }
        public double Power { get; set; }
        public double Pf { get; set; }
        public double CrestFactor { get; set; }
        public double Freq { get; set; }
    }
}
