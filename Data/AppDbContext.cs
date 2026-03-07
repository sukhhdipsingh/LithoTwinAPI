using Microsoft.EntityFrameworkCore;
using LithoTwinAPI.Domain;
using LithoTwinAPI.Models;

namespace LithoTwinAPI.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Machine> Machines { get; set; }
    public DbSet<WaferBatch> WaferBatches { get; set; }
    public DbSet<Alert> Alerts { get; set; }
    public DbSet<TelemetryReading> TelemetryReadings { get; set; }
    public DbSet<ExposureResult> ExposureResults { get; set; }
    public DbSet<Reticle> Reticles { get; set; }
    public DbSet<MachineFault> MachineFaults { get; set; }
    public DbSet<StateTransition> StateTransitions { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MachineFault>()
            .Property(f => f.FaultType)
            .HasConversion<string>();

        modelBuilder.Entity<StateTransition>(e =>
        {
            e.Property(s => s.FromState).HasConversion<string>();
            e.Property(s => s.ToState).HasConversion<string>();
        });

        modelBuilder.Entity<Machine>()
            .Property(m => m.State)
            .HasConversion<string>();

        // Seed machines — names match real ASML product lines
        modelBuilder.Entity<Machine>().HasData(
            new Machine
            {
                Id = "NXE-3400B",
                CurrentTemperature = 21.3,
                MaxOperatingTemp = 24.0,
                State = MachineLifecycleState.Running,
                UptimeHours = 1247.5,
                ExposureCount = 48210,
                TotalWafersProcessed = 1928,
                ThroughputFactor = 1.0
            },
            new Machine
            {
                Id = "NXE-3600D",
                CurrentTemperature = 22.8,
                MaxOperatingTemp = 23.5,
                State = MachineLifecycleState.Running,
                UptimeHours = 340.0,
                ExposureCount = 5100,
                TotalWafersProcessed = 204,
                ThroughputFactor = 1.0
            },
            new Machine
            {
                Id = "TWINSCAN-EXE",
                CurrentTemperature = 19.0,
                MaxOperatingTemp = 24.0,
                State = MachineLifecycleState.Maintenance,
                UptimeHours = 5020.0,
                ExposureCount = 122400,
                TotalWafersProcessed = 4896,
                ThroughputFactor = 1.0
            }
        );

        // Seed reticles for different layers
        modelBuilder.Entity<Reticle>().HasData(
            new Reticle { Id = "RET-001", Name = "MASK-M1-v3", LayerId = "M1", UsageCount = 1230, ContaminationLevel = 0.12 },
            new Reticle { Id = "RET-002", Name = "MASK-VIA1-v1", LayerId = "VIA1", UsageCount = 870, ContaminationLevel = 0.31 },
            new Reticle { Id = "RET-003", Name = "MASK-POLY-v2", LayerId = "POLY", UsageCount = 4100, MaxUsages = 4500, ContaminationLevel = 0.65 }
        );

        // Seed initial state transitions to establish audit trail baseline
        modelBuilder.Entity<StateTransition>().HasData(
            new StateTransition
            {
                Id = Guid.Parse("a1b2c3d4-0001-0001-0001-000000000001"),
                MachineId = "NXE-3400B",
                FromState = MachineLifecycleState.Idle,
                ToState = MachineLifecycleState.Running,
                Reason = "Initial production startup",
                TransitionedAt = DateTime.UtcNow.AddDays(-52)
            },
            new StateTransition
            {
                Id = Guid.Parse("a1b2c3d4-0001-0001-0001-000000000002"),
                MachineId = "TWINSCAN-EXE",
                FromState = MachineLifecycleState.Running,
                ToState = MachineLifecycleState.Maintenance,
                Reason = "Scheduled maintenance after 5000h uptime cycle",
                TransitionedAt = DateTime.UtcNow.AddDays(-3)
            }
        );
    }
}