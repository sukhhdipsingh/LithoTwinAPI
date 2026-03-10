namespace LithoTwinAPI.Models;

public enum BatchStatus { Pending, Processing, Rerouted, Completed }

/// <summary>
/// A batch of wafers routed to a machine for exposure processing.
/// Status progresses from Pending → Processing → Completed, or Rerouted if no machines are available.
/// </summary>
public class WaferBatch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string AssignedMachineId { get; set; } = string.Empty;
    public BatchStatus Status { get; set; } = BatchStatus.Pending;
    public int WaferCount { get; set; } = 25; // standard lot size
    public string LayerId { get; set; } = "M1"; // metal-1 by default
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}