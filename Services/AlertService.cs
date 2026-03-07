using LithoTwinAPI.Data;
using LithoTwinAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace LithoTwinAPI.Services;

/// <summary>
/// Manages system alerts and factory-level statistics.
/// Alerts are an append-only log; they can be acknowledged but not deleted.
/// </summary>
public class AlertService
{
    private readonly AppDbContext _db;

    public AlertService(AppDbContext db) => _db = db;

    public async Task<List<Alert>> GetActiveAlertsAsync()
    {
        return await _db.Alerts
            .Where(a => !a.IsAcknowledged)
            .OrderByDescending(a => a.Timestamp)
            .ToListAsync();
    }

    public async Task AcknowledgeAsync(Guid alertId)
    {
        var alert = await _db.Alerts.FindAsync(alertId)
            ?? throw new KeyNotFoundException($"Alert '{alertId}' not found");
        alert.IsAcknowledged = true;
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// Aggregates factory-level statistics across all machines.
    /// </summary>
    public async Task<object> GetFactoryStatsAsync()
    {
        var machines = await _db.Machines.ToListAsync();
        var alertCounts = await _db.Alerts
            .GroupBy(a => a.Severity)
            .Select(g => new { severity = g.Key.ToString(), count = g.Count() })
            .ToListAsync();

        var activeFaultCount = await _db.MachineFaults
            .Where(f => f.ResolvedAt == null)
            .CountAsync();

        var running = machines.Where(m => m.State == Domain.MachineLifecycleState.Running);

        return new
        {
            machines = new
            {
                total = machines.Count,
                idle = machines.Count(m => m.State == Domain.MachineLifecycleState.Idle),
                calibrating = machines.Count(m => m.State == Domain.MachineLifecycleState.Calibrating),
                running = machines.Count(m => m.State == Domain.MachineLifecycleState.Running),
                faulted = machines.Count(m => m.State == Domain.MachineLifecycleState.Faulted),
                maintenance = machines.Count(m => m.State == Domain.MachineLifecycleState.Maintenance)
            },
            production = new
            {
                totalExposures = machines.Sum(m => m.ExposureCount),
                totalWafersProcessed = machines.Sum(m => m.TotalWafersProcessed),
                avgTemperature = running.Any()
                    ? Math.Round(running.Average(m => m.CurrentTemperature), 1)
                    : 0.0
            },
            activeFaultCount,
            alerts = alertCounts
        };
    }
}
