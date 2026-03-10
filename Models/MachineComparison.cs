namespace LithoTwinAPI.Models;

/// <summary>
/// Snapshot comparison of multiple machines.
/// </summary>
public class MachineComparison
{
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public int MachineCount { get; set; }
    public List<MachineComparisonEntry> Entries { get; set; } = new();
    public string? Recommendation { get; set; }
}

public class MachineComparisonEntry
{
    public string MachineId { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public double CurrentTemperature { get; set; }
    public double MaxOperatingTemp { get; set; }
    public double TemperatureHeadroomPercent { get; set; }
    public double ThroughputFactor { get; set; }
    public double UptimeHours { get; set; }
    public int ExposureCount { get; set; }
    public int ActiveFaultCount { get; set; }
}
