using LithoTwinAPI.Data;
using LithoTwinAPI.Domain;
using LithoTwinAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace LithoTwinAPI.Services;

/// <summary>
/// Manages machine lifecycle state transitions through the FSM.
/// All state changes are validated, audited, and produce explicit domain errors on violation.
/// </summary>
public class MachineLifecycleService
{
    private readonly AppDbContext _db;

    public MachineLifecycleService(AppDbContext db)
    {
        _db = db;
        _db.Database.EnsureCreated();
    }

    /// <summary>
    /// Performs a validated state transition through the FSM.
    /// Every transition is recorded in the audit log.
    /// Throws <see cref="InvalidStateTransitionException"/> if the transition is forbidden.
    /// </summary>
    public async Task<StateTransition> TransitionStateAsync(
        string machineId, MachineLifecycleState targetState, string reason)
    {
        var machine = await FindMachineOrThrowAsync(machineId);
        var fsm = new MachineStateMachine(machine.State);

        var transition = fsm.TransitionTo(targetState, machineId, reason);

        machine.State = fsm.CurrentState;
        machine.LastUpdated = DateTime.UtcNow;

        _db.StateTransitions.Add(transition);
        await _db.SaveChangesAsync();

        return transition;
    }

    public async Task<List<StateTransition>> GetTransitionHistoryAsync(string machineId)
    {
        return await _db.StateTransitions
            .Where(t => t.MachineId == machineId)
            .OrderByDescending(t => t.TransitionedAt)
            .Take(50)
            .ToListAsync();
    }

    public async Task<List<Machine>> GetAllMachinesAsync()
        => await _db.Machines.ToListAsync();

    /// <summary>
    /// Computes machine health as a weighted score of temperature, uptime, and lifecycle state.
    /// Active faults degrade the score proportionally.
    /// </summary>
    public async Task<object> ComputeHealthScoreAsync(string machineId)
    {
        var machine = await FindMachineOrThrowAsync(machineId);
        var activeFaultCount = await _db.MachineFaults
            .Where(f => f.MachineId == machineId && f.ResolvedAt == null)
            .CountAsync();

        double tempScore = ComputeTemperatureScore(machine);
        double uptimeScore = ComputeUptimeScore(machine);
        double stateScore = machine.State switch
        {
            MachineLifecycleState.Running => 100,
            MachineLifecycleState.Calibrating => 70,
            MachineLifecycleState.Idle => 50,
            MachineLifecycleState.Faulted => 0,
            MachineLifecycleState.Maintenance => 10,
            _ => 0
        };

        // Weights: temperature matters most for EUV optics stability
        double overall = (tempScore * 0.5) + (uptimeScore * 0.2) + (stateScore * 0.3);
        overall = Math.Max(0, overall - (activeFaultCount * 15));

        string comment = overall switch
        {
            >= 80 => "healthy — nominal operating conditions",
            >= 60 => "degraded — monitor closely",
            >= 40 => "needs attention — schedule maintenance",
            _ => "critical — take offline"
        };

        return new
        {
            machineId,
            overallScore = Math.Round(overall, 1),
            comment,
            activeFaultCount,
            throughputFactor = machine.ThroughputFactor,
            breakdown = new
            {
                temperature = new { score = Math.Round(tempScore, 1), weight = 0.5,
                    detail = $"{machine.CurrentTemperature:F1}°C / {machine.MaxOperatingTemp:F1}°C" },
                uptime = new { score = Math.Round(uptimeScore, 1), weight = 0.2,
                    detail = $"{machine.UptimeHours:F0}h" },
                state = new { score = stateScore, weight = 0.3,
                    detail = machine.State.ToString() }
            }
        };
    }

    /// <summary>
    /// Predicts maintenance urgency based on uptime cycles and overlay drift monitoring.
    /// </summary>
    public async Task<object> PredictMaintenanceAsync(string machineId)
    {
        var machine = await FindMachineOrThrowAsync(machineId);

        double hoursLeft = Math.Max(0,
            SystemConstants.MaintenanceIntervalHours -
            (machine.UptimeHours % SystemConstants.MaintenanceIntervalHours));

        string urgency;
        if (hoursLeft < SystemConstants.MaintenanceImminentThresholdHours) urgency = "imminent";
        else if (hoursLeft < SystemConstants.MaintenanceUpcomingThresholdHours) urgency = "upcoming";
        else urgency = "not_due";

        var recentExposures = await _db.ExposureResults
            .Where(e => e.MachineId == machineId)
            .OrderByDescending(e => e.ExposedAt)
            .Take(20)
            .ToListAsync();

        double? avgOverlay = null;
        bool overlayDegrading = false;
        if (recentExposures.Count >= 5)
        {
            avgOverlay = recentExposures.Average(e =>
                Math.Sqrt(e.OverlayErrorX * e.OverlayErrorX + e.OverlayErrorY * e.OverlayErrorY));
            if (avgOverlay > SystemConstants.OverlayDegradationThresholdNm)
                overlayDegrading = true;
        }

        var activeFaultCount = await _db.MachineFaults
            .Where(f => f.MachineId == machineId && f.ResolvedAt == null)
            .CountAsync();

        return new
        {
            machineId,
            estimatedHoursUntilMaintenance = Math.Round(hoursLeft, 0),
            urgency,
            overlayDegrading,
            activeFaultCount,
            avgOverlayNm = avgOverlay.HasValue ? Math.Round(avgOverlay.Value, 3) : (double?)null,
            note = machine.State == MachineLifecycleState.Maintenance
                ? "currently in maintenance"
                : machine.State == MachineLifecycleState.Faulted
                    ? "faulted — maintenance required before resuming production"
                    : overlayDegrading
                        ? "overlay trending up — consider scheduling maintenance"
                        : null
        };
    }

    /// <summary>
    /// Compares multiple machines side-by-side across key operational metrics.
    /// Generates a recommendation identifying the best candidate for production.
    /// </summary>
    public async Task<MachineComparison> CompareMachinesAsync(List<string> machineIds)
    {
        var comparison = new MachineComparison { MachineCount = machineIds.Count };

        foreach (var id in machineIds)
        {
            var machine = await FindMachineOrThrowAsync(id);
            var faultCount = await _db.MachineFaults
                .Where(f => f.MachineId == id && f.ResolvedAt == null)
                .CountAsync();

            double headroom = machine.MaxOperatingTemp > 0
                ? Math.Round((1.0 - machine.CurrentTemperature / machine.MaxOperatingTemp) * 100, 1)
                : 0;

            comparison.Entries.Add(new MachineComparisonEntry
            {
                MachineId = id,
                State = machine.State.ToString(),
                CurrentTemperature = machine.CurrentTemperature,
                MaxOperatingTemp = machine.MaxOperatingTemp,
                TemperatureHeadroomPercent = headroom,
                ThroughputFactor = machine.ThroughputFactor,
                UptimeHours = machine.UptimeHours,
                ExposureCount = machine.ExposureCount,
                ActiveFaultCount = faultCount
            });
        }

        // Recommend the Running machine with most thermal headroom and no faults
        var bestCandidate = comparison.Entries
            .Where(e => e.State == MachineLifecycleState.Running.ToString() && e.ActiveFaultCount == 0)
            .OrderByDescending(e => e.TemperatureHeadroomPercent)
            .FirstOrDefault();

        comparison.Recommendation = bestCandidate != null
            ? $"{bestCandidate.MachineId} — best thermal headroom ({bestCandidate.TemperatureHeadroomPercent}%) with no active faults"
            : "No fault-free Running machines available for production";

        return comparison;
    }


    private static double ComputeTemperatureScore(Machine m)
    {
        double ratio = m.CurrentTemperature / m.MaxOperatingTemp;
        if (ratio < 0.7) return 100;
        if (ratio > 1.0) return 0;
        return (1.0 - ratio) / 0.3 * 100;
    }

    private static double ComputeUptimeScore(Machine m)
    {
        if (m.UptimeHours < 500) return 100;
        if (m.UptimeHours > 3000) return 20;
        return 100 - (m.UptimeHours - 500) / 2500 * 80;
    }

    internal async Task<Machine> FindMachineOrThrowAsync(string machineId)
        => await _db.Machines.FindAsync(machineId)
            ?? throw new KeyNotFoundException($"No machine found with id '{machineId}'");
}
