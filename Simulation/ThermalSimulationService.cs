using LithoTwinAPI.Data;
using LithoTwinAPI.Domain;
using LithoTwinAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace LithoTwinAPI.Simulation;

/// <summary>
/// BackgroundService that drives the thermal simulation loop.
/// Executes a tick every 15 seconds, computing thermal drift via <see cref="SimulationEngine"/>
/// and detecting overheat conditions that trigger fault injection.
/// 
/// This service is infrastructure — the actual computation lives in SimulationEngine.
/// </summary>
public class ThermalSimulationService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ThermalSimulationService> _logger;

    public ThermalSimulationService(IServiceScopeFactory scopeFactory, ILogger<ThermalSimulationService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(2000, stoppingToken);
        _logger.LogInformation("Thermal simulation started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ExecuteSimulationTick(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Simulation tick failed, will retry next cycle");
            }

            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
    }

    private async Task ExecuteSimulationTick(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var machines = await db.Machines.ToListAsync(ct);

        foreach (var machine in machines)
        {
            if (machine.State == MachineLifecycleState.Maintenance)
                continue;

            var activeFaults = await db.MachineFaults
                .Where(f => f.MachineId == machine.Id && f.ResolvedAt == null)
                .Select(f => f.FaultType)
                .ToListAsync(ct);

            // Compute drift via the pure simulation engine
            double drift = SimulationEngine.ComputeThermalDrift(machine.State, activeFaults);
            machine.CurrentTemperature = Math.Round(machine.CurrentTemperature + drift, 2);
            machine.LastUpdated = DateTime.UtcNow;

            // Overheat detection → fault injection
            if (SimulationEngine.IsOverheatCondition(machine, activeFaults))
            {
                var fault = new MachineFault
                {
                    MachineId = machine.Id,
                    FaultType = FaultType.ThermalOverload,
                    Description = $"Auto-detected: temperature {machine.CurrentTemperature:F1}°C exceeds limit"
                };
                db.MachineFaults.Add(fault);

                var fsm = new MachineStateMachine(machine.State);
                var transition = fsm.TransitionTo(
                    MachineLifecycleState.Faulted, machine.Id,
                    $"ThermalOverload fault: {machine.CurrentTemperature:F1}°C");
                machine.State = fsm.CurrentState;
                db.StateTransitions.Add(transition);

                db.Alerts.Add(new Alert
                {
                    MachineId = machine.Id,
                    Severity = AlertSeverity.Critical,
                    Message = $"Thermal overload at {machine.CurrentTemperature:F1}°C — machine faulted"
                });

                _logger.LogWarning("{MachineId} thermal overload, transitioned to Faulted", machine.Id);
            }

            db.TelemetryReadings.Add(new TelemetryReading
            {
                MachineId = machine.Id,
                Temperature = machine.CurrentTemperature
            });
        }

        await db.SaveChangesAsync(ct);
    }
}
