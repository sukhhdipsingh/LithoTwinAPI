using LithoTwinAPI.Domain;

namespace LithoTwinAPI.Models;

/// <summary>
/// Records an equipment fault. Faults persist until explicitly resolved during maintenance.
/// Each fault type has distinct effects on machine behavior — see <see cref="FaultType"/>.
/// </summary>
public class MachineFault
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string MachineId { get; set; } = string.Empty;
    public FaultType FaultType { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;

    /// <summary>Null while the fault is still active. Set when resolved during maintenance.</summary>
    public DateTime? ResolvedAt { get; set; }

    public bool IsActive => ResolvedAt == null;
}
