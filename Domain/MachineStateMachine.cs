using LithoTwinAPI.Models;

namespace LithoTwinAPI.Domain;

/// <summary>
/// Centralized finite state machine for machine lifecycle management.
/// All state transitions MUST go through this component — no scattered if/else chains.
///
/// Transition rules:
///   Idle → Calibrating, Maintenance
///   Calibrating → Running, Idle (abort calibration)
///   Running → Faulted, Maintenance (planned), Calibrating (recalibration)
///   Faulted → Maintenance (mandatory before any recovery)
///   Maintenance → Calibrating (after repairs, must recalibrate), Idle (shutdown)
///
/// Explicitly forbidden:
///   Running → Idle (must go through Maintenance or Calibrating)
///   Faulted → Running (must go through Maintenance → Calibrating first)
///   Faulted → Idle (unresolved faults cannot be ignored)
///   Faulted → Calibrating (cannot calibrate with active faults)
/// </summary>
public sealed class MachineStateMachine
{
    private static readonly HashSet<(MachineLifecycleState From, MachineLifecycleState To)> AllowedTransitions = new()
    {
        // Idle: machine is powered but not producing
        (MachineLifecycleState.Idle, MachineLifecycleState.Calibrating),
        (MachineLifecycleState.Idle, MachineLifecycleState.Maintenance),

        // Calibrating: alignment routines before production
        (MachineLifecycleState.Calibrating, MachineLifecycleState.Running),
        (MachineLifecycleState.Calibrating, MachineLifecycleState.Idle),

        // Running: active production — can fault, or go to planned maintenance/recalibration
        (MachineLifecycleState.Running, MachineLifecycleState.Faulted),
        (MachineLifecycleState.Running, MachineLifecycleState.Maintenance),
        (MachineLifecycleState.Running, MachineLifecycleState.Calibrating),

        // Faulted: only exit is Maintenance — faults must be resolved before anything else
        (MachineLifecycleState.Faulted, MachineLifecycleState.Maintenance),

        // Maintenance: after repairs, must recalibrate before running again
        (MachineLifecycleState.Maintenance, MachineLifecycleState.Calibrating),
        (MachineLifecycleState.Maintenance, MachineLifecycleState.Idle),
    };

    private MachineLifecycleState _currentState;

    public MachineLifecycleState CurrentState => _currentState;

    public MachineStateMachine(MachineLifecycleState initialState)
    {
        _currentState = initialState;
    }

    /// <summary>
    /// Attempts a state transition. Throws <see cref="InvalidStateTransitionException"/> if the
    /// transition violates lifecycle rules.
    /// </summary>
    /// <returns>A <see cref="StateTransition"/> record for persistence.</returns>
    public StateTransition TransitionTo(MachineLifecycleState target, string machineId, string reason)
    {
        if (_currentState == target)
            throw new InvalidStateTransitionException(_currentState, target, "Machine is already in this state.");

        if (!AllowedTransitions.Contains((_currentState, target)))
            throw new InvalidStateTransitionException(_currentState, target);

        var transition = new StateTransition
        {
            MachineId = machineId,
            FromState = _currentState,
            ToState = target,
            Reason = reason
        };

        _currentState = target;
        return transition;
    }

    /// <summary>
    /// Checks whether a transition is valid without performing it.
    /// </summary>
    public bool CanTransitionTo(MachineLifecycleState target)
        => _currentState != target && AllowedTransitions.Contains((_currentState, target));

    /// <summary>
    /// Returns all states reachable from the current state.
    /// </summary>
    public IReadOnlyList<MachineLifecycleState> GetAllowedTransitions()
        => AllowedTransitions
            .Where(t => t.From == _currentState)
            .Select(t => t.To)
            .ToList();
}
