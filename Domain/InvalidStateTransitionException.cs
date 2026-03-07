namespace LithoTwinAPI.Domain;

/// <summary>
/// Thrown when a state transition violates the machine lifecycle rules.
/// This is a domain error, not an infrastructure error — it means the caller
/// attempted something the system explicitly forbids.
/// </summary>
public class InvalidStateTransitionException : Exception
{
    public MachineLifecycleState FromState { get; }
    public MachineLifecycleState ToState { get; }

    public InvalidStateTransitionException(MachineLifecycleState from, MachineLifecycleState to)
        : base($"Invalid state transition: {from} → {to}. This transition is not permitted by the machine lifecycle.")
    {
        FromState = from;
        ToState = to;
    }

    public InvalidStateTransitionException(MachineLifecycleState from, MachineLifecycleState to, string reason)
        : base($"Invalid state transition: {from} → {to}. {reason}")
    {
        FromState = from;
        ToState = to;
    }
}
