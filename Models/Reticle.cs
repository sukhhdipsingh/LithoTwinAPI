namespace LithoTwinAPI.Models;

public class Reticle
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string LayerId { get; set; } = string.Empty;
    public int UsageCount { get; set; }
    public int MaxUsages { get; set; } = 5000;

    // contamination builds up over time from outgassing and EUV photons
    // 0.0 = clean, 1.0 = needs pellicle replacement
    public double ContaminationLevel { get; set; }

    public bool IsUsable => ContaminationLevel < 0.8 && UsageCount < MaxUsages;
}
