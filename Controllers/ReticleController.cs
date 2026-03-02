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

    // injecting DbContext directly — reticle logic is simple enough
    // that wrapping it in a service felt pointless
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

    /// simulates a reticle inspection. contamination goes up a bit each time
    /// because handling always introduces particles. in reality you'd track
    /// pellicle transmission degradation too, but that's a whole other thing
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
            warning = !ret.IsUsable ? "reticle no longer meets usability criteria — consider replacement" : (string?)null
        });
    }

    // maybe add a cleaning endpoint later? reticle cleaning is a real process
    // where you can sometimes bring contamination back down. but not always
}
