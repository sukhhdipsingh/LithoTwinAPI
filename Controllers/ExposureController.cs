using LithoTwinAPI.Services;
using LithoTwinAPI.Models;
using Microsoft.AspNetCore.Mvc;

namespace LithoTwinAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ExposureController : ControllerBase
{
    private readonly IManufacturingService _mfg;

    public ExposureController(IManufacturingService mfg) => _mfg = mfg;

    [HttpPost("run")]
    public async Task<IActionResult> RunExposure([FromBody] ExposureRequest req)
    {
        try
        {
            var result = await _mfg.RunExposureAsync(req);
            return Ok(result);
        }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
    }

    [HttpGet("history")]
    public async Task<IActionResult> GetHistory([FromQuery] string machineId)
    {
        if (string.IsNullOrEmpty(machineId))
            return BadRequest(new { error = "machineId is required" });

        var results = await _mfg.GetExposureHistoryAsync(machineId);
        return Ok(results);
    }
}
