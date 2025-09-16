using System.Threading.Tasks;
using HouseholdMS.Models;

namespace HouseholdMS.Drivers
{
    public interface IInstrument
    {
        Task<string> IdentifyAsync();
        Task ToRemoteAsync();
        Task ToLocalAsync();
        Task DrainErrorQueueAsync();

        Task SetAcDcAsync(bool ac);
        Task SetFunctionAsync(string func);      // CURR/RES/VOLT/POW/SHOR
        Task SetSetpointAsync(double value);     // units depend on func
        string CurrentUnits { get; }

        Task SetPfCfAsync(double pf, double cf); // AC only
        Task EnableInputAsync(bool on);
        Task EStopAsync();

        // Ranges
        Task CacheRangesAsync();
        string DescribeRanges();

        // Meter
        Task<InstrumentReading> ReadAsync();

        // Scope
        Task ScopeConfigureAsync(string trigSource, string trigSlope, double trigLevel);
        Task ScopeRunAsync();
        Task ScopeSingleAsync();
        Task ScopeStopAsync();
        Task<(double[] v, double[] i)> FetchWaveformsAsync();

        // Harmonics (voltage)
        Task<double[]> MeasureVoltageHarmonicsAsync(int n);
    }
}
