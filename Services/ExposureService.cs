using LithoTwinAPI.Data;
using LithoTwinAPI.Domain;
using LithoTwinAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace LithoTwinAPI.Services;

/// <summary>
/// Handles EUV exposure simulation and wafer batch routing.
/// 
/// Overlay error is computed deterministically from:
///   temperature (thermal expansion) + focus offset (penalty) + fault degradation
/// 
/// Wafer routing selects the coldest Running machine to maximize thermal headroom.
/// </summary>
public class ExposureService
{
    private readonly AppDbContext _db;
    private static readonly Random _rng = new();

    public ExposureService(AppDbContext db) => _db = db;

    /// <summary>
    /// Runs an EUV exposure. Machine must be in Running state.
    /// Overlay error = f(temperature, focus_offset, throughput_factor).
    /// </summary>
    public async Task<ExposureResult> RunExposureAsync(ExposureRequest req)
    {
        var machine = await _db.Machines.FindAsync(req.MachineId)
            ?? throw new KeyNotFoundException($"Machine '{req.MachineId}' not found");

        if (machine.State != MachineLifecycleState.Running)
            throw new InvalidOperationException(
                $"Exposures require Running state. Machine '{req.MachineId}' is in {machine.State}.");

        var result = ComputeOverlayError(machine, req);

        // Each exposure deposits energy into the wafer stage
        machine.CurrentTemperature += SystemConstants.ExposureHeatContributionBaseC
            + _rng.NextDouble() * SystemConstants.ExposureHeatContributionVarianceC;
        machine.ExposureCount++;
        machine.LastUpdated = DateTime.UtcNow;

        if (!result.Passed)
        {
            _db.Alerts.Add(new Alert
            {
                MachineId = machine.Id,
                Severity = AlertSeverity.Warning,
                Message = $"Overlay spec exceeded on {req.LayerId}: " +
                          $"X={result.OverlayErrorX:F3}nm Y={result.OverlayErrorY:F3}nm " +
                          $"(limit {SystemConstants.OverlaySpecLimitNm}nm)"
            });
        }

        _db.ExposureResults.Add(result);
        await _db.SaveChangesAsync();
        return result;
    }

    public async Task<List<ExposureResult>> GetHistoryAsync(string machineId)
    {
        return await _db.ExposureResults
            .Where(e => e.MachineId == machineId)
            .OrderByDescending(e => e.ExposedAt)
            .Take(100)
            .ToListAsync();
    }

    /// <summary>
    /// Routes a wafer batch to the coldest Running machine (maximum thermal headroom).
    /// Only Running machines are eligible for production.
    /// </summary>
    public async Task<WaferBatch> RouteWaferBatchAsync()
    {
        var target = await _db.Machines
            .Where(m => m.State == MachineLifecycleState.Running)
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
                Message = "Wafer batch rerouted: no machines in Running state available"
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
            throw new InvalidOperationException(
                $"Can only complete batches in Processing state (current: {batch.Status}).");

        batch.Status = BatchStatus.Completed;

        var machine = await _db.Machines.FindAsync(batch.AssignedMachineId);
        if (machine != null)
            machine.TotalWafersProcessed += batch.WaferCount;

        await _db.SaveChangesAsync();
        return batch;
    }

    /// <summary>
    /// Deterministic overlay error model.
    /// overlay = (thermal_expansion + focus_penalty) × fault_degradation + measurement_noise
    /// </summary>
    private static ExposureResult ComputeOverlayError(Machine machine, ExposureRequest req)
    {
        double tempAboveBaseline = Math.Max(0, machine.CurrentTemperature - SystemConstants.AmbientBaselineC);
        double thermalFactor = tempAboveBaseline * SystemConstants.ThermalExpansionCoefficientNmPerC;
        double focusPenalty = Math.Abs(req.FocusOffset) * SystemConstants.FocusPenaltyCoefficientNmPerNm;

        // LaserDegradation increases overlay error via throughput degradation
        double faultPenalty = 1.0 + (1.0 - machine.ThroughputFactor) * 0.5;

        var result = new ExposureResult
        {
            MachineId = req.MachineId,
            LayerId = req.LayerId,
            DoseUsed = req.DoseEnergy,
            FocusUsed = req.FocusOffset,
            OverlayErrorX = Math.Round(
                (thermalFactor + focusPenalty) * faultPenalty + (_rng.NextDouble() - 0.5) * 0.4, 3),
            OverlayErrorY = Math.Round(
                (thermalFactor + focusPenalty) * faultPenalty + (_rng.NextDouble() - 0.5) * 0.4, 3)
        };

        double totalOverlay = Math.Sqrt(
            result.OverlayErrorX * result.OverlayErrorX +
            result.OverlayErrorY * result.OverlayErrorY);
        result.Passed = totalOverlay < SystemConstants.OverlaySpecLimitNm;

        return result;
    }
}
