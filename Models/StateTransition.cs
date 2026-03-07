using LithoTwinAPI.Domain;

namespace LithoTwinAPI.Models;

/// <summary>
/// Immutable audit record of a state transition. Every transition through the FSM
/// produces one of these — enables full reconstruction of machine lifecycle history.
/// </summary>
public class StateTransition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string MachineId { get; set; } = string.Empty;
    public MachineLifecycleState FromState { get; set; }
    public MachineLifecycleState ToState { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime TransitionedAt { get; set; } = DateTime.UtcNow;
}
