using LithoTwinAPI.Data;
using LithoTwinAPI.Models;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace LithoTwinAPI.Services;

public class ManufacturingService : IManufacturingService
{
    private readonly AppDbContext _db;
    private static readonly Random _rng = new();

    // these should probably live in appsettings but whatever, it's fine here for now
    const double OVERLAY_LIMIT = 1.5;     // nm
    const double HYSTERESIS_MARGIN = 2.0; // °C below max before reactivation
    const double MAINTENANCE_INTERVAL = 2000.0; // hours, rough estimate

    public ManufacturingService(AppDbContext db)
    {
        _db = db;
        _db.Database.EnsureCreated();
    }

    // ========== TELEMETRY ==========

    public async Task UpdateTelemetryAsync(string machineId, double temperature)
    {
        var machine = await _db.Machines.FindAsync(machineId)
            ?? throw new KeyNotFoundException($"No machine found with id '{machineId}'");

        if (machine.State == MachineState.Maintenance)
            throw new InvalidOperationException($"Machine '{machineId}' is under maintenance, telemetry rejected");

        // sanity check — sensors sometimes send garbage
        if (temperature < -10 || temperature > 80)
            throw new ArgumentOutOfRangeException(nameof(temperature), "Reading out of plausible range");

        machine.CurrentTemperature = temperature;
        machine.LastUpdated = DateTime.UtcNow;

        // log it
        _db.TelemetryReadings.Add(new TelemetryReading
        {
            MachineId = machineId,
            Temperature = temperature
        });

        // overheat → force cooling
        if (temperature >= machine.MaxOperatingTemp && machine.State == MachineState.Active)
        {
            machine.State = MachineState.Cooling;
            _db.Alerts.Add(new Alert
            {
                MachineId = machine.Id,
                Severity = AlertSeverity.Critical,
                Message = $"Overheat detected ({temperature:F1}°C, limit {machine.MaxOperatingTemp:F1}°C). Switched to Cooling."
            });
        }
        else if (machine.State == MachineState.Cooling && temperature < machine.MaxOperatingTemp - HYSTERESIS_MARGIN)
        {
            // cool enough to go again
            machine.State = MachineState.Active;
            _db.Alerts.Add(new Alert
            {
                MachineId = machine.Id,
                Severity = AlertSeverity.Info,
                Message = $"Temperature back to normal ({temperature:F1}°C). Machine reactivated."
            });
        }

        await _db.SaveChangesAsync();
    }

    public async Task<List<TelemetryReading>> GetTelemetryHistoryAsync(string machineId, int count = 50)
    {
        if (count > 200) count = 200; // don't let anyone pull the entire table

        return await _db.TelemetryReadings
            .Where(t => t.MachineId == machineId)
            .OrderByDescending(t => t.RecordedAt)
            .Take(count)
            .ToListAsync();
    }

    public async Task<string> GetTemperatureTrendAsync(string machineId)
    {
        var readings = await _db.TelemetryReadings
            .Where(t => t.MachineId == machineId)
            .OrderByDescending(t => t.RecordedAt)
            .Take(10)
            .Select(t => t.Temperature)
            .ToListAsync();

        if (readings.Count < 4)
            return "insufficient_data";

        // compare recent half vs older half — simple but does the job
        var recent = readings.Take(4).Average();
        var older = readings.Skip(readings.Count - 4).Average();
        var diff = recent - older;

        if (diff > 0.5) return "rising";
        if (diff < -0.5) return "falling";
        return "stable";
    }

    // ========== WAFER ROUTING ==========

    public async Task<WaferBatch> AssignWaferBatchAsync()
    {
        // pick the coldest active machine — more thermal headroom = better
        var target = await _db.Machines
            .Where(m => m.State == MachineState.Active)
            .OrderBy(m => m.CurrentTemperature)
            .FirstOrDefaultAsync();

        var batch = new WaferBatch();

        if (target != null)
        {
            batch.AssignedMachineId = target.Id;
            batch.Status = BatchStatus.Processing;
        }
        else
        {
            batch.Status = BatchStatus.Rerouted;
            _db.Alerts.Add(new Alert
            {
                MachineId = "SYSTEM",
                Severity = AlertSeverity.Warning,
                Message = "Wafer batch rerouted: no active machines available"
            });
        }

        _db.WaferBatches.Add(batch);
        await _db.SaveChangesAsync();
        return batch;
    }

    public async Task<WaferBatch> CompleteBatchAsync(Guid batchId)
    {
        var batch = await _db.WaferBatches.FindAsync(batchId)
            ?? throw new KeyNotFoundException($"Batch '{batchId}' not found");

        if (batch.Status != BatchStatus.Processing)
            throw new InvalidOperationException($"Can only complete batches that are Processing, this one is {batch.Status}");

        batch.Status = BatchStatus.Completed;

        // update machine wafer count
        var machine = await _db.Machines.FindAsync(batch.AssignedMachineId);
        if (machine != null)
            machine.TotalWafersProcessed += batch.WaferCount;

        await _db.SaveChangesAsync();
        return batch;
    }

    // ========== EXPOSURE ==========

    public async Task<ExposureResult> RunExposureAsync(ExposureRequest req)
    {
        var machine = await _db.Machines.FindAsync(req.MachineId)
            ?? throw new KeyNotFoundException($"Machine '{req.MachineId}' not found");

        if (machine.State != MachineState.Active)
            throw new InvalidOperationException($"Machine '{req.MachineId}' is not active (current state: {machine.State})");

        // --- overlay error model ---
        // higher temp = worse alignment (thermal expansion of stage/lens)
        // 0.15 nm/°C is a rough approximation, reality is way more complex but this
        // gives a reasonable feel for how temperature affects overlay
        double tempFactor = Math.Max(0, machine.CurrentTemperature - 20.0) * 0.08;
        double focusPenalty = Math.Abs(req.FocusOffset) * 0.003;

        var result = new ExposureResult
        {
            MachineId = req.MachineId,
            LayerId = req.LayerId,
            DoseUsed = req.DoseEnergy,
            FocusUsed = req.FocusOffset,
            OverlayErrorX = Math.Round(tempFactor + focusPenalty + (_rng.NextDouble() - 0.5) * 0.4, 3),
            OverlayErrorY = Math.Round(tempFactor + focusPenalty + (_rng.NextDouble() - 0.5) * 0.4, 3)
        };

        double totalOverlay = Math.Sqrt(result.OverlayErrorX * result.OverlayErrorX +
                                        result.OverlayErrorY * result.OverlayErrorY);
        result.Passed = totalOverlay < OVERLAY_LIMIT;

        // each exposure adds a tiny bit of heat
        machine.CurrentTemperature += 0.02 + _rng.NextDouble() * 0.03;
        machine.ExposureCount++;
        machine.TotalWafersProcessed++; // TODO: this should be per-batch, not per-exposure. fix later
        machine.LastUpdated = DateTime.UtcNow;

        if (!result.Passed)
        {
            _db.Alerts.Add(new Alert
            {
                MachineId = machine.Id,
                Severity = AlertSeverity.Warning,
                Message = $"Overlay spec exceeded on {req.LayerId}: X={result.OverlayErrorX:F3}nm Y={result.OverlayErrorY:F3}nm (limit {OVERLAY_LIMIT}nm)"
            });
        }

        _db.ExposureResults.Add(result);
        await _db.SaveChangesAsync();
        return result;
    }

    public async Task<List<ExposureResult>> GetExposureHistoryAsync(string machineId)
    {
        return await _db.ExposureResults
            .Where(e => e.MachineId == machineId)
            .OrderByDescending(e => e.ExposedAt)
            .Take(100)
            .ToListAsync();
    }

    // ========== MACHINE STATUS / HEALTH ==========

    public async Task<List<Machine>> GetSystemStatusAsync()
        => await _db.Machines.ToListAsync();

    public async Task<object> GetMachineHealthAsync(string machineId)
    {
        var machine = await _db.Machines.FindAsync(machineId)
            ?? throw new KeyNotFoundException($"Machine '{machineId}' not found");

        double tempScore = CalcTempScore(machine);
        double uptimeScore = CalcUptimeScore(machine);
        double stateScore = machine.State switch
        {
            MachineState.Active => 100,
            MachineState.Cooling => 50,
            _ => 10
        };

        // weights: temperature matters most for EUV
        double overall = (tempScore * 0.5) + (uptimeScore * 0.2) + (stateScore * 0.3);

        string comment = overall switch
        {
            >= 80 => "healthy, good to go",
            >= 60 => "running warm, keep an eye on it",
            >= 40 => "needs attention soon",
            _ => "critical — consider pulling offline"
        };

        return new
        {
            machineId,
            overallScore = Math.Round(overall, 1),
            comment,
            breakdown = new
            {
                temperature = new { score = Math.Round(tempScore, 1), weight = 0.5, detail = $"{machine.CurrentTemperature:F1}°C / {machine.MaxOperatingTemp:F1}°C" },
                uptime = new { score = Math.Round(uptimeScore, 1), weight = 0.2, detail = $"{machine.UptimeHours:F0}h" },
                state = new { score = stateScore, weight = 0.3, detail = machine.State.ToString() }
            }
        };
    }

    public async Task<object> PredictMaintenanceAsync(string machineId)
    {
        var machine = await _db.Machines.FindAsync(machineId)
            ?? throw new KeyNotFoundException($"Machine '{machineId}' not found");

        // super rough prediction — just based on uptime vs maintenance interval
        // a real system would track component wear, EUV source power degradation, etc
        double hoursLeft = Math.Max(0, MAINTENANCE_INTERVAL - (machine.UptimeHours % MAINTENANCE_INTERVAL));
        string urgency;
        if (hoursLeft < 100) urgency = "imminent";
        else if (hoursLeft < 500) urgency = "upcoming";
        else urgency = "not_due";

        // overlay drift check — if recent exposures show degrading overlay, bump urgency
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

            // if avg overlay is above 1.0nm, that's not great
            if (avgOverlay > 1.0)
                overlayDegrading = true;
        }

        return new
        {
            machineId,
            estimatedHoursUntilMaintenance = Math.Round(hoursLeft, 0),
            urgency,
            overlayDegrading,
            avgOverlayNm = avgOverlay.HasValue ? Math.Round(avgOverlay.Value, 3) : (double?)null,
            note = machine.State == MachineState.Maintenance
                ? "already in maintenance"
                : overlayDegrading
                    ? "overlay trending up — may want to schedule maintenance sooner"
                    : null
        };
    }

    // ========== ALERTS ==========

    public async Task<List<Alert>> GetActiveAlertsAsync()
    {
        return await _db.Alerts
            .Where(a => !a.IsAcknowledged)
            .OrderByDescending(a => a.Timestamp)
            .ToListAsync();
    }

    public async Task AcknowledgeAlertAsync(Guid alertId)
    {
        var alert = await _db.Alerts.FindAsync(alertId)
            ?? throw new KeyNotFoundException($"Alert '{alertId}' not found");
        alert.IsAcknowledged = true;
        await _db.SaveChangesAsync();
    }

    // ========== STATS ==========

    public async Task<object> GetFactoryStatsAsync()
    {
        var machines = await _db.Machines.ToListAsync();
        var alertCounts = await _db.Alerts
            .GroupBy(a => a.Severity)
            .Select(g => new { severity = g.Key.ToString(), count = g.Count() })
            .ToListAsync();

        var activeMachines = machines.Where(m => m.State != MachineState.Maintenance);

        return new
        {
            machines = new
            {
                total = machines.Count,
                active = machines.Count(m => m.State == MachineState.Active),
                cooling = machines.Count(m => m.State == MachineState.Cooling),
                maintenance = machines.Count(m => m.State == MachineState.Maintenance)
            },
            production = new
            {
                totalExposures = machines.Sum(m => m.ExposureCount),
                totalWafersProcessed = machines.Sum(m => m.TotalWafersProcessed),
                avgTemperature = activeMachines.Any()
                    ? Math.Round(activeMachines.Average(m => m.CurrentTemperature), 1)
                    : 0.0
            },
            alerts = alertCounts
        };
    }

    // ========== CSV EXPORT ==========

    public async Task<string> ExportTelemetryCsvAsync(string machineId)
    {
        var readings = await _db.TelemetryReadings
            .Where(t => t.MachineId == machineId)
            .OrderBy(t => t.RecordedAt)
            .ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine("timestamp,machine_id,temperature_c");
        foreach (var r in readings)
            sb.AppendLine($"{r.RecordedAt:yyyy-MM-dd HH:mm:ss},{r.MachineId},{r.Temperature:F2}");

        return sb.ToString();
    }

    // ========== helpers ==========

    private static double CalcTempScore(Machine m)
    {
        double ratio = m.CurrentTemperature / m.MaxOperatingTemp;
        if (ratio < 0.7) return 100;
        if (ratio > 1.0) return 0;
        return (1.0 - ratio) / 0.3 * 100;
    }

    private static double CalcUptimeScore(Machine m)
    {
        // degrades linearly from 500h to 3000h. not sophisticated but gets the point across
        if (m.UptimeHours < 500) return 100;
        if (m.UptimeHours > 3000) return 20;
        return 100 - (m.UptimeHours - 500) / 2500 * 80;
    }
}