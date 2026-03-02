using LithoTwinAPI.Services;
using LithoTwinAPI.Models;
using Microsoft.AspNetCore.Mvc;

namespace LithoTwinAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FactoryController : ControllerBase
{
    private readonly IManufacturingService _mfg;

    public FactoryController(IManufacturingService mfg) => _mfg = mfg;

    // ---- telemetry ----

    [HttpPost("telemetry")]
    public async Task<IActionResult> PostTelemetry(
        [FromQuery] string machineId,
        [FromQuery] double temperature)
    {
        try
        {
            await _mfg.UpdateTelemetryAsync(machineId, temperature);
            return Ok(new { message = $"Telemetry updated for {machineId}" });
        }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
        catch (ArgumentOutOfRangeException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpGet("telemetry/{machineId}/history")]
    public async Task<IActionResult> GetTelemetryHistory(string machineId, [FromQuery] int count = 50)
    {
        var readings = await _mfg.GetTelemetryHistoryAsync(machineId, count);
        return Ok(readings);
    }

    [HttpGet("telemetry/{machineId}/trend")]
    public async Task<IActionResult> GetTrend(string machineId)
    {
        var trend = await _mfg.GetTemperatureTrendAsync(machineId);
        return Ok(new { machineId, trend });
    }

    /// text/csv download of all telemetry readings for a machine
    [HttpGet("telemetry/{machineId}/export")]
    public async Task<IActionResult> ExportCsv(string machineId)
    {
        var csv = await _mfg.ExportTelemetryCsvAsync(machineId);
        var bytes = System.Text.Encoding.UTF8.GetBytes(csv);
        return File(bytes, "text/csv", $"telemetry_{machineId}.csv");
    }

    // ---- wafer routing ----

    [HttpPost("route-wafer")]
    public async Task<IActionResult> RouteWafer()
    {
        var batch = await _mfg.AssignWaferBatchAsync();
        return Ok(batch);
    }

    [HttpPost("batches/{id}/complete")]
    public async Task<IActionResult> CompleteBatch(Guid id)
    {
        try
        {
            var batch = await _mfg.CompleteBatchAsync(id);
            return Ok(batch);
        }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
    }

    // ---- machines ----

    [HttpGet("system-status")]
    public async Task<IActionResult> GetStatus() => Ok(await _mfg.GetSystemStatusAsync());

    [HttpGet("machines/{machineId}/health")]
    public async Task<IActionResult> GetHealth(string machineId)
    {
        try { return Ok(await _mfg.GetMachineHealthAsync(machineId)); }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
    }

    [HttpGet("machines/{machineId}/maintenance-prediction")]
    public async Task<IActionResult> PredictMaintenance(string machineId)
    {
        try { return Ok(await _mfg.PredictMaintenanceAsync(machineId)); }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
    }

    // ---- alerts ----

    [HttpGet("alerts")]
    public async Task<IActionResult> GetAlerts() => Ok(await _mfg.GetActiveAlertsAsync());

    [HttpPost("alerts/{id}/acknowledge")]
    public async Task<IActionResult> AcknowledgeAlert(Guid id)
    {
        try
        {
            await _mfg.AcknowledgeAlertAsync(id);
            return Ok(new { message = "Alert acknowledged" });
        }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
    }

    // ---- stats ----

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats() => Ok(await _mfg.GetFactoryStatsAsync());
}