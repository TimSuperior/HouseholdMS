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

    public class SamplePoint
    {
        public int Index { get; set; }
        public double Value { get; set; }
    }

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

    public class UserSettings
    {
        public string AcDc { get; set; }
        public string Function { get; set; }
        public double Setpoint { get; set; }
        public double Pf { get; set; }
        public double Cf { get; set; }
    }
}
