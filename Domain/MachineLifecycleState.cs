namespace LithoTwinAPI.Domain;

/// <summary>
/// Represents the lifecycle state of an EUV lithography machine.
/// Transitions are enforced by <see cref="MachineStateMachine"/> — no ad-hoc state changes allowed.
/// </summary>
public enum MachineLifecycleState
{
    /// <summary>Machine is powered but not processing. Safe to transition to Calibrating or Maintenance.</summary>
    Idle,

    /// <summary>Machine is performing alignment and calibration routines before production.</summary>
    Calibrating,

    /// <summary>Machine is actively processing wafers. Telemetry is live, throughput is at full capacity.</summary>
    Running,

    /// <summary>Machine has encountered a fault. No processing allowed until fault is resolved via Maintenance.</summary>
    Faulted,

    /// <summary>Machine is undergoing maintenance. Faults can be resolved. Must recalibrate before returning to Running.</summary>
    Maintenance
}
