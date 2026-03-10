using LithoTwinAPI.Services;
using LithoTwinAPI.Models;
using Microsoft.AspNetCore.Mvc;

namespace LithoTwinAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ExposureController : ControllerBase
{
    private readonly ExposureService _exposure;

    public ExposureController(ExposureService exposure) => _exposure = exposure;

    [HttpPost("run")]
    [ProducesResponseType(typeof(ExposureResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RunExposure([FromBody] ExposureRequest req)
    {
        try
        {
            var result = await _exposure.RunExposureAsync(req);
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

        return Ok(await _exposure.GetHistoryAsync(machineId));
    }
}
