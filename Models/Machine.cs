using LithoTwinAPI.Domain;

namespace LithoTwinAPI.Models;

public class Machine
{
    public string Id { get; set; } = string.Empty;
    public double CurrentTemperature { get; set; }

    /// <summary>
    /// EUV source + optics typically require sub-25°C environment,
    /// but each tool has its own spec depending on configuration.
    /// </summary>
    public double MaxOperatingTemp { get; set; } = 24.0;

    public MachineLifecycleState State { get; set; } = MachineLifecycleState.Idle;

    public double UptimeHours { get; set; }
    public int ExposureCount { get; set; }
    public int TotalWafersProcessed { get; set; }
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Effective throughput multiplier. Degraded by active faults (e.g. LaserDegradation).
    /// 1.0 = nominal, 0.0 = no output.
    /// </summary>
    public double ThroughputFactor { get; set; } = 1.0;
}