namespace LithoTwinAPI.Models;

public class ExposureResult
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string MachineId { get; set; } = string.Empty;
    public string LayerId { get; set; } = string.Empty;
    public double DoseUsed { get; set; }
    public double FocusUsed { get; set; }

    // overlay is the registration error between layers — the thing ASML obsesses over
    public double OverlayErrorX { get; set; }  // nm
    public double OverlayErrorY { get; set; }  // nm

    public bool Passed { get; set; }
    /// <summary>Human-readable reason when Passed is false; null on success.</summary>
    public string? FailureReason { get; set; }
    public DateTime ExposedAt { get; set; } = DateTime.UtcNow;
}
