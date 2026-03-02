namespace LithoTwinAPI.Models;

public enum AlertSeverity { Info, Warning, Critical }

public class Alert
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string MachineId { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public AlertSeverity Severity { get; set; } = AlertSeverity.Info;
    public bool IsAcknowledged { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}