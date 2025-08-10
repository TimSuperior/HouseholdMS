using HouseholdMS.Services;

public class OscilloscopeService
{
    private readonly ScpiDeviceVisa scpi;
    public OscilloscopeService(ScpiDeviceVisa scpiDevice) { scpi = scpiDevice; }

    public string Identify() => scpi.Query("*IDN?");
    public int SelfCalibrate() => int.Parse(scpi.Query("*CAL?"));
    public string GetStatus() => scpi.Query("CALibrate:INTERNal:STATus?");

    public void ConfigureChannel(int ch, double scale, string coupling = "DC")
    {
        scpi.Write($":CH{ch}:SCAle {scale}");
        scpi.Write($":CH{ch}:COUPling {coupling}");
    }

    public void SetTimebase(double secDiv) => scpi.Write($":TIMebase:SCALe {secDiv}");

    public double[] GetWaveform(int ch)
    {
        scpi.Write(":WAVeform:FORMat BYTE");
        scpi.Write($":WAVeform:SOURce CHAN{ch}");
        string pre = scpi.Query(":WAVeform:PREamble?");
        byte[] raw = scpi.QueryBinary(":WAVeform:DATA?");
        return ParseWaveform(raw, pre);
    }

    private double[] ParseWaveform(byte[] raw, string preamble)
    {
        // Replace this with a real parser using preamble values
        double scale = 0.01;
        double[] volts = new double[raw.Length];
        for (int i = 0; i < raw.Length; i++) volts[i] = (raw[i] - 128) * scale;
        return volts;
    }
}
