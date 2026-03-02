namespace LithoTwinAPI.Models;

public enum MachineState { Active, Cooling, Maintenance }

public class Machine
{
    public string Id { get; set; } = string.Empty;
    public double CurrentTemperature { get; set; }

    // EUV source + optics typically require sub-25°C environment,
    // but each tool has its own spec depending on configuration
    public double MaxOperatingTemp { get; set; } = 24.0;

    public MachineState State { get; set; } = MachineState.Active;
    public double UptimeHours { get; set; }
    public int ExposureCount { get; set; }
    public int TotalWafersProcessed { get; set; }
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}