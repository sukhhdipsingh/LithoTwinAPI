using LithoTwinAPI.Models;

namespace LithoTwinAPI.Services;

public interface IManufacturingService
{
    // telemetry
    Task UpdateTelemetryAsync(string machineId, double temperature);
    Task<List<TelemetryReading>> GetTelemetryHistoryAsync(string machineId, int count = 50);
    Task<string> GetTemperatureTrendAsync(string machineId);

    // wafer routing
    Task<WaferBatch> AssignWaferBatchAsync();
    Task<WaferBatch> CompleteBatchAsync(Guid batchId);

    // machine status
    Task<List<Machine>> GetSystemStatusAsync();
    Task<object> GetMachineHealthAsync(string machineId);
    Task<object> PredictMaintenanceAsync(string machineId);

    // alerts
    Task<List<Alert>> GetActiveAlertsAsync();
    Task AcknowledgeAlertAsync(Guid alertId);

    // exposure
    Task<ExposureResult> RunExposureAsync(ExposureRequest req);
    Task<List<ExposureResult>> GetExposureHistoryAsync(string machineId);

    // aggregate
    Task<object> GetFactoryStatsAsync();

    // export
    Task<string> ExportTelemetryCsvAsync(string machineId);
}
