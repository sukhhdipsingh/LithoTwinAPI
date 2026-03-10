namespace LithoTwinAPI.Models;

/// <summary>
/// A single temperature sensor reading. Values may include fault-injected noise
/// when SensorFailure faults are active — see TelemetryService for details.
/// </summary>
public class TelemetryReading
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string MachineId { get; set; } = string.Empty;
    public double Temperature { get; set; }
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
}
