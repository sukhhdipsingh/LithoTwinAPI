using LithoTwinAPI.Data;
using LithoTwinAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LithoTwinAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ReticleController : ControllerBase
{
    private readonly AppDbContext _db;

    public ReticleController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetAll()
        => Ok(await _db.Reticles.ToListAsync());

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        var ret = await _db.Reticles.FindAsync(id);
        return ret == null
            ? NotFound(new { error = $"Reticle '{id}' not found" })
            : Ok(ret);
    }

    /// <summary>
    /// Simulates a reticle inspection. Contamination increases with each handling cycle
    /// due to particle deposition from outgassing and EUV photon exposure.
    /// </summary>
    [HttpPost("{id}/inspect")]
    public async Task<IActionResult> Inspect(string id)
    {
        var ret = await _db.Reticles.FindAsync(id);
        if (ret == null) return NotFound(new { error = $"Reticle '{id}' not found" });

        ret.ContaminationLevel = Math.Round(
            ret.ContaminationLevel + 0.02 + new Random().NextDouble() * 0.03, 3);
        ret.UsageCount++;

        await _db.SaveChangesAsync();

        return Ok(new
        {
            reticle = ret,
            warning = !ret.IsUsable
                ? "Reticle no longer meets usability criteria — schedule replacement"
                : (string?)null
        });
    }
}
