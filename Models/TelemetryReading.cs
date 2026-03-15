namespace LithoTwinAPI.Models;

public class TelemetryReading
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string MachineId { get; set; } = string.Empty;
    public double Temperature { get; set; }
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
}
