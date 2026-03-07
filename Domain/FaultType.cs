namespace LithoTwinAPI.Domain;

/// <summary>
/// Categorizes equipment faults that can occur on a lithography machine.
/// Each fault type has distinct effects on telemetry, throughput, and system behavior.
/// </summary>
public enum FaultType
{
    /// <summary>
    /// Cooling subsystem cannot dissipate heat fast enough.
    /// Effect: temperature spikes in telemetry, forces transition to Faulted.
    /// </summary>
    ThermalOverload,

    /// <summary>
    /// EUV source power degradation over time.
    /// Effect: overlay accuracy degrades, throughput reduced.
    /// </summary>
    LaserDegradation,

    /// <summary>
    /// One or more alignment/temperature sensors producing unreliable data.
    /// Effect: telemetry noise increases, readings become untrustworthy.
    /// </summary>
    SensorFailure
}
