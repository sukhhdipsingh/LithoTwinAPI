using LithoTwinAPI.Data;
using LithoTwinAPI.Domain;
using LithoTwinAPI.Models;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace LithoTwinAPI.Services;

/// <summary>
/// Handles telemetry ingestion, history, and trend analysis.
/// 
/// Telemetry is not passively recorded — active faults modify what gets stored.
/// SensorFailure faults inject noise into recorded values.
/// ThermalOverload faults cause temperature spikes.
/// This ensures telemetry output is always: f(actual_reading, machine_state, active_faults).
/// </summary>
public class TelemetryService
{
    private readonly AppDbContext _db;
    private readonly FaultService _faultService;
    private static readonly Random _rng = new();

    public TelemetryService(AppDbContext db, FaultService faultService)
    {
        _db = db;
        _faultService = faultService;
    }

    /// <summary>
    /// Ingests a sensor reading with fault-aware processing.
    /// Validates bounds, applies SensorFailure noise, detects overheat conditions.
    /// </summary>
    public async Task IngestReadingAsync(string machineId, double temperature)
    {
        var machine = await _db.Machines.FindAsync(machineId)
            ?? throw new KeyNotFoundException($"No machine found with id '{machineId}'");

        if (machine.State == MachineLifecycleState.Maintenance)
            throw new InvalidOperationException(
                $"Machine '{machineId}' is under maintenance — telemetry ingestion rejected.");

        if (temperature < SystemConstants.SensorMinimumPlausibleC ||
            temperature > SystemConstants.SensorMaximumPlausibleC)
            throw new ArgumentOutOfRangeException(nameof(temperature),
                $"Reading {temperature}°C is outside plausible sensor range " +
                $"[{SystemConstants.SensorMinimumPlausibleC}, {SystemConstants.SensorMaximumPlausibleC}]°C.");

        var activeFaultTypes = await _db.MachineFaults
            .Where(f => f.MachineId == machineId && f.ResolvedAt == null)
            .Select(f => f.FaultType)
            .ToListAsync();

        double recordedTemp = ApplySensorFaultNoise(temperature, activeFaultTypes);

        machine.CurrentTemperature = recordedTemp;
        machine.LastUpdated = DateTime.UtcNow;

        _db.TelemetryReadings.Add(new TelemetryReading
        {
            MachineId = machineId,
            Temperature = recordedTemp
        });

        // ThermalOverload causes additional temperature spike
        if (activeFaultTypes.Contains(FaultType.ThermalOverload) &&
            machine.State is MachineLifecycleState.Running or MachineLifecycleState.Calibrating)
        {
            machine.CurrentTemperature += SystemConstants.ThermalOverloadDriftSpikeC;
        }

        // Overheat detection → fault injection via FaultService
        if (recordedTemp >= machine.MaxOperatingTemp &&
            machine.State == MachineLifecycleState.Running &&
            !activeFaultTypes.Contains(FaultType.ThermalOverload))
        {
            await _db.SaveChangesAsync();
            await _faultService.InjectFaultAsync(machineId, FaultType.ThermalOverload,
                $"Overheat detected: {recordedTemp:F1}°C exceeds limit of {machine.MaxOperatingTemp:F1}°C");
            return;
        }

        await _db.SaveChangesAsync();
    }

    public async Task<List<TelemetryReading>> GetHistoryAsync(string machineId, int count = 50)
    {
        if (count > 200) count = 200;

        return await _db.TelemetryReadings
            .Where(t => t.MachineId == machineId)
            .OrderByDescending(t => t.RecordedAt)
            .Take(count)
            .ToListAsync();
    }

    /// <summary>
    /// Analyzes the last 10 readings to detect rising, falling, or stable temperature trends.
    /// </summary>
    public async Task<string> ComputeTrendAsync(string machineId)
    {
        var readings = await _db.TelemetryReadings
            .Where(t => t.MachineId == machineId)
            .OrderByDescending(t => t.RecordedAt)
            .Take(10)
            .Select(t => t.Temperature)
            .ToListAsync();

        if (readings.Count < 4)
            return "insufficient_data";

        var recent = readings.Take(4).Average();
        var older = readings.Skip(readings.Count - 4).Average();
        var diff = recent - older;

        if (diff > 0.5) return "rising";
        if (diff < -0.5) return "falling";
        return "stable";
    }

    public async Task<string> ExportCsvAsync(string machineId)
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

    /// <summary>
    /// SensorFailure faults inject noise into recorded values.
    /// The recorded temperature drifts from the actual reading.
    /// </summary>
    private static double ApplySensorFaultNoise(double actualTemp, List<FaultType> activeFaults)
    {
        if (!activeFaults.Contains(FaultType.SensorFailure))
            return actualTemp;

        double noise = (_rng.NextDouble() - 0.5) * SystemConstants.SensorFailureNoiseAmplitudeC * 2;
        return Math.Round(actualTemp + noise, 2);
    }
}
