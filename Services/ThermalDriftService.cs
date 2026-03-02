using LithoTwinAPI.Data;
using LithoTwinAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace LithoTwinAPI.Services;

/// <summary>
/// Simulates thermal drift on machines so the system doesn't just sit idle.
/// In a real factory the BMS handles this, but for a demo it makes things interesting.
/// </summary>
public class ThermalDriftService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ThermalDriftService> _logger;
    private static readonly Random _rng = new();

    public ThermalDriftService(IServiceScopeFactory scopeFactory, ILogger<ThermalDriftService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // wait a bit before starting so the app has time to boot
        await Task.Delay(2000, stoppingToken);

        _logger.LogInformation("Thermal drift simulation started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SimulateTick(stoppingToken);
            }
            catch (Exception ex)
            {
                // don't crash the whole service if one tick fails
                _logger.LogWarning(ex, "Thermal drift tick failed, will retry");
            }

            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
    }

    private async Task SimulateTick(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var machines = await db.Machines.ToListAsync(ct);

        foreach (var m in machines)
        {
            if (m.State == MachineState.Maintenance)
                continue; // nothing to simulate

            double drift;

            if (m.State == MachineState.Active)
            {
                // active machines slowly heat up — not linear, varies a bit
                drift = 0.05 + _rng.NextDouble() * 0.1;
            }
            else // Cooling
            {
                // cooling is faster than heating (fans + chillers)
                drift = -(0.2 + _rng.NextDouble() * 0.15);
            }

            m.CurrentTemperature = Math.Round(m.CurrentTemperature + drift, 2);
            m.LastUpdated = DateTime.UtcNow;

            // auto-manage state transitions
            if (m.State == MachineState.Active && m.CurrentTemperature >= m.MaxOperatingTemp)
            {
                m.State = MachineState.Cooling;
                db.Alerts.Add(new Alert
                {
                    MachineId = m.Id,
                    Severity = AlertSeverity.Critical,
                    Message = $"Auto-cooling triggered at {m.CurrentTemperature:F1}°C"
                });
                _logger.LogWarning("{MachineId} hit thermal limit, switching to Cooling", m.Id);
            }
            else if (m.State == MachineState.Cooling && m.CurrentTemperature < m.MaxOperatingTemp - 2.0)
            {
                m.State = MachineState.Active;
                db.Alerts.Add(new Alert
                {
                    MachineId = m.Id,
                    Severity = AlertSeverity.Info,
                    Message = $"Cooled down to {m.CurrentTemperature:F1}°C, back online"
                });
                _logger.LogInformation("{MachineId} cooled down, reactivated", m.Id);
            }

            // log a reading
            db.TelemetryReadings.Add(new TelemetryReading
            {
                MachineId = m.Id,
                Temperature = m.CurrentTemperature
            });
        }

        await db.SaveChangesAsync(ct);
    }
}
