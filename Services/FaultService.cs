using LithoTwinAPI.Data;
using LithoTwinAPI.Domain;
using LithoTwinAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace LithoTwinAPI.Services;

/// <summary>
/// Manages equipment fault lifecycle: injection, propagation, and resolution.
/// 
/// Faults are not decorative — each type has a documented causal effect on system behavior.
/// See <see cref="FaultType"/> and docs/system-invariants.md for the full propagation model.
/// </summary>
public class FaultService
{
    private readonly AppDbContext _db;

    public FaultService(AppDbContext db) => _db = db;

    /// <summary>
    /// Injects a fault and applies its effects. If the machine is Running,
    /// the fault forces an automatic transition to Faulted via the FSM.
    /// Invariant: faults are never silent on a Running machine.
    /// </summary>
    public async Task<MachineFault> InjectFaultAsync(
        string machineId, FaultType faultType, string description)
    {
        var machine = await _db.Machines.FindAsync(machineId)
            ?? throw new KeyNotFoundException($"No machine found with id '{machineId}'");

        var fault = new MachineFault
        {
            MachineId = machineId,
            FaultType = faultType,
            Description = description
        };
        _db.MachineFaults.Add(fault);

        ApplyFaultDegradation(machine, faultType);

        // Running machines must transition to Faulted when a fault occurs
        if (machine.State == MachineLifecycleState.Running)
        {
            var fsm = new MachineStateMachine(machine.State);
            var transition = fsm.TransitionTo(
                MachineLifecycleState.Faulted, machineId,
                $"Auto-transition: {faultType} fault detected");

            machine.State = fsm.CurrentState;
            _db.StateTransitions.Add(transition);

            _db.Alerts.Add(new Alert
            {
                MachineId = machineId,
                Severity = AlertSeverity.Critical,
                Message = $"Fault detected: {faultType}. Machine transitioned to Faulted. {description}"
            });
        }

        machine.LastUpdated = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return fault;
    }

    /// <summary>
    /// Resolves all active faults. Only permitted in Maintenance state.
    /// Invariant: fault resolution restores throughput factor to nominal (1.0).
    /// </summary>
    public async Task<List<MachineFault>> ResolveFaultsAsync(string machineId)
    {
        var machine = await _db.Machines.FindAsync(machineId)
            ?? throw new KeyNotFoundException($"No machine found with id '{machineId}'");

        if (machine.State != MachineLifecycleState.Maintenance)
            throw new InvalidOperationException(
                $"Faults can only be resolved during Maintenance. Machine '{machineId}' is in {machine.State}.");

        var activeFaults = await _db.MachineFaults
            .Where(f => f.MachineId == machineId && f.ResolvedAt == null)
            .ToListAsync();

        var now = DateTime.UtcNow;
        foreach (var fault in activeFaults)
            fault.ResolvedAt = now;

        machine.ThroughputFactor = 1.0;
        machine.LastUpdated = now;

        await _db.SaveChangesAsync();
        return activeFaults;
    }

    /// <summary>
    /// Returns all unresolved faults for the specified machine, ordered by most recent first.
    /// </summary>
    public async Task<List<MachineFault>> GetActiveFaultsAsync(string machineId)
    {
        return await _db.MachineFaults
            .Where(f => f.MachineId == machineId && f.ResolvedAt == null)
            .OrderByDescending(f => f.OccurredAt)
            .ToListAsync();
    }

    /// <summary>
    /// Applies the causal degradation effect for a specific fault type.
    /// ThermalOverload → temperature effects (applied via simulation)
    /// LaserDegradation → throughput reduction
    /// SensorFailure → telemetry noise (applied at ingestion time)
    /// </summary>
    private static void ApplyFaultDegradation(Machine machine, FaultType faultType)
    {
        switch (faultType)
        {
            case FaultType.LaserDegradation:
                machine.ThroughputFactor = Math.Max(0.1,
                    machine.ThroughputFactor - SystemConstants.LaserDegradationThroughputPenalty);
                break;
            case FaultType.ThermalOverload:
            case FaultType.SensorFailure:
                // These faults have their effects applied elsewhere:
                // ThermalOverload → temperature spike in simulation engine
                // SensorFailure → noise injection at telemetry ingestion
                break;
        }
    }
}
