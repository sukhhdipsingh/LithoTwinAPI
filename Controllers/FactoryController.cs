using LithoTwinAPI.Domain;
using LithoTwinAPI.Services;
using LithoTwinAPI.Models;
using Microsoft.AspNetCore.Mvc;

namespace LithoTwinAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FactoryController : ControllerBase
{
    private readonly MachineLifecycleService _lifecycle;
    private readonly FaultService _faults;
    private readonly TelemetryService _telemetry;
    private readonly ExposureService _exposure;
    private readonly AlertService _alerts;

    public FactoryController(
        MachineLifecycleService lifecycle,
        FaultService faults,
        TelemetryService telemetry,
        ExposureService exposure,
        AlertService alerts)
    {
        _lifecycle = lifecycle;
        _faults = faults;
        _telemetry = telemetry;
        _exposure = exposure;
        _alerts = alerts;
    }

    // ---- telemetry ----

    [HttpPost("telemetry")]
    public async Task<IActionResult> PostTelemetry(
        [FromQuery] string machineId, [FromQuery] double temperature)
    {
        try
        {
            await _telemetry.IngestReadingAsync(machineId, temperature);
            return Ok(new { message = $"Telemetry recorded for {machineId}" });
        }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
        catch (ArgumentOutOfRangeException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpGet("telemetry/{machineId}/history")]
    public async Task<IActionResult> GetTelemetryHistory(string machineId, [FromQuery] int count = 50)
        => Ok(await _telemetry.GetHistoryAsync(machineId, count));

    [HttpGet("telemetry/{machineId}/trend")]
    public async Task<IActionResult> GetTrend(string machineId)
        => Ok(new { machineId, trend = await _telemetry.ComputeTrendAsync(machineId) });

    [HttpGet("telemetry/{machineId}/export")]
    public async Task<IActionResult> ExportCsv(string machineId)
    {
        var csv = await _telemetry.ExportCsvAsync(machineId);
        return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", $"telemetry_{machineId}.csv");
    }

    // ---- state transitions ----

    [HttpPost("machines/{machineId}/transition")]
    public async Task<IActionResult> TransitionState(
        string machineId,
        [FromQuery] MachineLifecycleState targetState,
        [FromQuery] string reason = "Manual transition")
    {
        try
        {
            var transition = await _lifecycle.TransitionStateAsync(machineId, targetState, reason);
            return Ok(transition);
        }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
        catch (InvalidStateTransitionException ex) { return Conflict(new { error = ex.Message }); }
    }

    [HttpGet("machines/{machineId}/transitions")]
    public async Task<IActionResult> GetTransitionHistory(string machineId)
        => Ok(await _lifecycle.GetTransitionHistoryAsync(machineId));

    // ---- fault management ----

    [HttpPost("machines/{machineId}/fault")]
    public async Task<IActionResult> InjectFault(
        string machineId,
        [FromQuery] FaultType faultType,
        [FromQuery] string description = "")
    {
        try
        {
            var fault = await _faults.InjectFaultAsync(machineId, faultType, description);
            return Ok(fault);
        }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
    }

    [HttpPost("machines/{machineId}/resolve-faults")]
    public async Task<IActionResult> ResolveFaults(string machineId)
    {
        try
        {
            var resolved = await _faults.ResolveFaultsAsync(machineId);
            return Ok(new { resolvedCount = resolved.Count, faults = resolved });
        }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
    }

    [HttpGet("machines/{machineId}/faults")]
    public async Task<IActionResult> GetActiveFaults(string machineId)
        => Ok(await _faults.GetActiveFaultsAsync(machineId));

    // ---- wafer routing ----

    [HttpPost("route-wafer")]
    public async Task<IActionResult> RouteWafer()
        => Ok(await _exposure.RouteWaferBatchAsync());

    [HttpPost("batches/{id}/complete")]
    public async Task<IActionResult> CompleteBatch(Guid id)
    {
        try { return Ok(await _exposure.CompleteBatchAsync(id)); }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
    }

    // ---- machines ----

    [HttpGet("system-status")]
    public async Task<IActionResult> GetStatus()
        => Ok(await _lifecycle.GetAllMachinesAsync());

    [HttpGet("machines/{machineId}/health")]
    public async Task<IActionResult> GetHealth(string machineId)
    {
        try { return Ok(await _lifecycle.ComputeHealthScoreAsync(machineId)); }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
    }

    [HttpGet("machines/compare")]
    public async Task<IActionResult> GetComparison([FromQuery] string ids)
    {
        var machineIds = ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        if (machineIds.Count < 2)
            return BadRequest(new { error = "Provide at least 2 comma-separated machine IDs" });

        try { return Ok(await _lifecycle.CompareMachinesAsync(machineIds)); }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
    }

    [HttpGet("machines/{machineId}/maintenance-prediction")]
    public async Task<IActionResult> PredictMaintenance(string machineId)
    {
        try { return Ok(await _lifecycle.PredictMaintenanceAsync(machineId)); }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
    }

    // ---- alerts & stats ----

    [HttpGet("alerts")]
    public async Task<IActionResult> GetAlerts()
        => Ok(await _alerts.GetActiveAlertsAsync());

    [HttpPost("alerts/{id}/acknowledge")]
    public async Task<IActionResult> AcknowledgeAlert(Guid id)
    {
        try
        {
            await _alerts.AcknowledgeAsync(id);
            return Ok(new { message = "Alert acknowledged" });
        }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
    }

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
        => Ok(await _alerts.GetFactoryStatsAsync());
}