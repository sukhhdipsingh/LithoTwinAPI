using LithoTwinAPI.Domain;
using LithoTwinAPI.Models;

namespace LithoTwinAPI.Simulation;

/// <summary>
/// Pure computation engine for thermal simulation.
/// Computes per-tick temperature drift as a deterministic function of machine state and active faults.
/// 
/// Formula: drift = base_drift(state) + Σ fault_effects(active_faults)
/// 
/// This is separated from the BackgroundService infrastructure to keep
/// the computation testable and the execution lifecycle independent.
/// </summary>
public static class SimulationEngine
{
    private static readonly Random _rng = new();

    /// <summary>
    /// Computes temperature change for a single simulation tick.
    /// 
    /// State-dependent behavior:
    ///   Running → gradual heat accumulation from EUV source and wafer stage
    ///   Calibrating → moderate heat from alignment laser and stage positioning
    ///   Idle → passive drift toward ambient baseline (Newton's cooling approximation)
    ///   Faulted → slight cooling as machine is not processing
    ///   Maintenance → no thermal simulation (returns 0)
    /// </summary>
    public static double ComputeThermalDrift(
        MachineLifecycleState state, IReadOnlyList<FaultType> activeFaults)
    {
        double baseDrift = state switch
        {
            MachineLifecycleState.Running => 0.05 + _rng.NextDouble() * 0.1,
            MachineLifecycleState.Calibrating => 0.02 + _rng.NextDouble() * 0.04,
            MachineLifecycleState.Idle => (_rng.NextDouble() - 0.5) * 0.02,
            MachineLifecycleState.Faulted => -0.05 + _rng.NextDouble() * 0.03,
            _ => 0
        };

        // ThermalOverload causes temperature spikes even while faulted
        if (activeFaults.Contains(FaultType.ThermalOverload))
            baseDrift += SystemConstants.ThermalOverloadDriftSpikeC;

        // SensorFailure corrupts the drift measurement
        if (activeFaults.Contains(FaultType.SensorFailure))
            baseDrift += (_rng.NextDouble() - 0.5) * SystemConstants.SensorFailureNoiseAmplitudeC;

        return baseDrift;
    }

    /// <summary>
    /// Determines whether a machine has exceeded its thermal operating limit.
    /// Returns true if fault injection is needed.
    /// </summary>
    public static bool IsOverheatCondition(
        Machine machine, IReadOnlyList<FaultType> activeFaults)
    {
        return machine.State == MachineLifecycleState.Running
            && machine.CurrentTemperature >= machine.MaxOperatingTemp
            && !activeFaults.Contains(FaultType.ThermalOverload);
    }
}
