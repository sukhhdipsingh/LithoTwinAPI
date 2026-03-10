namespace LithoTwinAPI.Models;

/// <summary>
/// Parameters for a single EUV exposure shot.
/// </summary>
public class ExposureRequest
{
    public string MachineId { get; set; } = string.Empty;

    /// <summary>EUV dose energy in mJ/cm². Typical EUV dose is 30 mJ/cm².</summary>
    public double DoseEnergy { get; set; } = 30.0;

    /// <summary>Focus offset from ideal plane in nm. Zero means nominally focused.</summary>
    public double FocusOffset { get; set; }

    public string LayerId { get; set; } = "M1";
}
