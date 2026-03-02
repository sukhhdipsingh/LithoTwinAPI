using Microsoft.EntityFrameworkCore;
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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // seed machines — names match real ASML product lines
        modelBuilder.Entity<Machine>().HasData(
            new Machine
            {
                Id = "NXE-3400B",
                CurrentTemperature = 21.3,
                MaxOperatingTemp = 24.0,
                State = MachineState.Active,
                UptimeHours = 1247.5,
                ExposureCount = 48210,
                TotalWafersProcessed = 1928
            },
            new Machine
            {
                Id = "NXE-3600D",
                CurrentTemperature = 22.8,
                MaxOperatingTemp = 23.5,
                State = MachineState.Active,
                UptimeHours = 340.0,
                ExposureCount = 5100,
                TotalWafersProcessed = 204
            },
            new Machine
            {
                Id = "TWINSCAN-EXE",
                CurrentTemperature = 19.0,
                MaxOperatingTemp = 24.0,
                State = MachineState.Maintenance,
                UptimeHours = 5020.0,
                ExposureCount = 122400,
                TotalWafersProcessed = 4896
            }
        );

        // seed reticles for different layers
        modelBuilder.Entity<Reticle>().HasData(
            new Reticle { Id = "RET-001", Name = "MASK-M1-v3", LayerId = "M1", UsageCount = 1230, ContaminationLevel = 0.12 },
            new Reticle { Id = "RET-002", Name = "MASK-VIA1-v1", LayerId = "VIA1", UsageCount = 870, ContaminationLevel = 0.31 },
            new Reticle { Id = "RET-003", Name = "MASK-POLY-v2", LayerId = "POLY", UsageCount = 4100, MaxUsages = 4500, ContaminationLevel = 0.65 }
        );
    }
}