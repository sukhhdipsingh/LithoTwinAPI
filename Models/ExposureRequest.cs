namespace LithoTwinAPI.Models;

// kept it simple — just what the caller needs to provide
public class ExposureRequest
{
    public string MachineId { get; set; } = string.Empty;
    public double DoseEnergy { get; set; } = 30.0;  // mJ/cm², typical for EUV
    public double FocusOffset { get; set; }          // nm, 0 = nominal
    public string LayerId { get; set; } = "M1";
}
