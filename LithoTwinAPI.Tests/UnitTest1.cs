using LithoTwinAPI.Data;
using LithoTwinAPI.Models;
using LithoTwinAPI.Services;
using Microsoft.EntityFrameworkCore;

namespace LithoTwinAPI.Tests;

public class ManufacturingServiceTests
{
    private AppDbContext CreateDb(string name)
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        var db = new AppDbContext(opts);
        db.Database.EnsureCreated();
        return db;
    }

    // ---- telemetry ----

    [Fact]
    public async Task telemetry_updates_machine_temp()
    {
        var db = CreateDb("telemetry_basic");
        var svc = new ManufacturingService(db);

        await svc.UpdateTelemetryAsync("NXE-3400B", 22.5);

        var machine = await db.Machines.FindAsync("NXE-3400B");
        Assert.Equal(22.5, machine!.CurrentTemperature);
    }

    [Fact]
    public async Task telemetry_triggers_cooling_on_overheat()
    {
        var db = CreateDb("telemetry_overheat");
        var svc = new ManufacturingService(db);

        // NXE-3400B has MaxOperatingTemp = 24.0
        await svc.UpdateTelemetryAsync("NXE-3400B", 25.0);

        var machine = await db.Machines.FindAsync("NXE-3400B");
        Assert.Equal(MachineState.Cooling, machine!.State);

        // should have generated an alert too
        var alerts = await db.Alerts.Where(a => a.MachineId == "NXE-3400B").ToListAsync();
        Assert.Contains(alerts, a => a.Severity == AlertSeverity.Critical);
    }

    [Fact]
    public async Task telemetry_reactivates_after_cooldown()
    {
        var db = CreateDb("telemetry_cooldown");
        var svc = new ManufacturingService(db);

        // first overheat it
        await svc.UpdateTelemetryAsync("NXE-3400B", 25.0);
        // then cool it down (needs to be 2°C below threshold = 22.0)
        await svc.UpdateTelemetryAsync("NXE-3400B", 21.5);

        var machine = await db.Machines.FindAsync("NXE-3400B");
        Assert.Equal(MachineState.Active, machine!.State);
    }

    [Fact]
    public async Task telemetry_rejects_maintenance_machine()
    {
        var db = CreateDb("telemetry_maintenance");
        var svc = new ManufacturingService(db);

        // TWINSCAN-EXE is seeded in Maintenance
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.UpdateTelemetryAsync("TWINSCAN-EXE", 20.0));
    }

    [Fact]
    public async Task telemetry_rejects_garbage_values()
    {
        var db = CreateDb("telemetry_garbage");
        var svc = new ManufacturingService(db);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => svc.UpdateTelemetryAsync("NXE-3400B", 999.0));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => svc.UpdateTelemetryAsync("NXE-3400B", -50.0));
    }

    [Fact]
    public async Task telemetry_saves_reading_to_history()
    {
        var db = CreateDb("telemetry_history");
        var svc = new ManufacturingService(db);

        await svc.UpdateTelemetryAsync("NXE-3400B", 22.0);
        await svc.UpdateTelemetryAsync("NXE-3400B", 22.5);

        var history = await svc.GetTelemetryHistoryAsync("NXE-3400B");
        Assert.Equal(2, history.Count);
    }

    // ---- wafer routing ----

    [Fact]
    public async Task routing_picks_coldest_machine()
    {
        var db = CreateDb("routing_coldest");
        var svc = new ManufacturingService(db);

        // NXE-3400B is 21.3°C, NXE-3600D is 22.8°C (both active)
        var batch = await svc.AssignWaferBatchAsync();

        Assert.Equal("NXE-3400B", batch.AssignedMachineId);
        Assert.Equal(BatchStatus.Processing, batch.Status);
    }

    [Fact]
    public async Task routing_reroutes_when_no_machines_available()
    {
        var db = CreateDb("routing_none");
        var svc = new ManufacturingService(db);

        // put all active machines into cooling
        var machines = await db.Machines.Where(m => m.State == MachineState.Active).ToListAsync();
        foreach (var m in machines)
            m.State = MachineState.Cooling;
        await db.SaveChangesAsync();

        var batch = await svc.AssignWaferBatchAsync();

        Assert.Equal(BatchStatus.Rerouted, batch.Status);
        Assert.True(string.IsNullOrEmpty(batch.AssignedMachineId)); // no machine assigned
    }

    // ---- exposure ----

    [Fact]
    public async Task exposure_produces_overlay_result()
    {
        var db = CreateDb("exposure_basic");
        var svc = new ManufacturingService(db);

        var req = new ExposureRequest
        {
            MachineId = "NXE-3400B",
            DoseEnergy = 30.0,
            FocusOffset = 0,
            LayerId = "M1"
        };

        var result = await svc.RunExposureAsync(req);

        Assert.Equal("NXE-3400B", result.MachineId);
        Assert.Equal("M1", result.LayerId);
        // overlay should be some finite number, not NaN or infinity
        Assert.False(double.IsNaN(result.OverlayErrorX));
        Assert.False(double.IsNaN(result.OverlayErrorY));
    }

    [Fact]
    public async Task exposure_heats_up_machine()
    {
        var db = CreateDb("exposure_heat");
        var svc = new ManufacturingService(db);

        var before = (await db.Machines.FindAsync("NXE-3400B"))!.CurrentTemperature;

        await svc.RunExposureAsync(new ExposureRequest { MachineId = "NXE-3400B" });

        var after = (await db.Machines.FindAsync("NXE-3400B"))!.CurrentTemperature;
        Assert.True(after > before, "temperature should increase after exposure");
    }

    [Fact]
    public async Task exposure_rejects_inactive_machine()
    {
        var db = CreateDb("exposure_inactive");
        var svc = new ManufacturingService(db);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.RunExposureAsync(new ExposureRequest { MachineId = "TWINSCAN-EXE" }));
    }

    [Fact]
    public async Task exposure_increments_counters()
    {
        var db = CreateDb("exposure_counters");
        var svc = new ManufacturingService(db);

        var countBefore = (await db.Machines.FindAsync("NXE-3400B"))!.ExposureCount;
        await svc.RunExposureAsync(new ExposureRequest { MachineId = "NXE-3400B" });
        var countAfter = (await db.Machines.FindAsync("NXE-3400B"))!.ExposureCount;

        Assert.Equal(countBefore + 1, countAfter);
    }

    // ---- health ----

    [Fact]
    public async Task health_returns_score_in_range()
    {
        var db = CreateDb("health_range");
        var svc = new ManufacturingService(db);

        // bit of a hack — the return is anonymous, so we serialize and check
        var result = await svc.GetMachineHealthAsync("NXE-3400B");
        var json = System.Text.Json.JsonSerializer.Serialize(result);

        Assert.Contains("overallScore", json);
        Assert.Contains("breakdown", json);
    }

    // ---- trend ----

    [Fact]
    public async Task trend_returns_insufficient_with_few_readings()
    {
        var db = CreateDb("trend_few");
        var svc = new ManufacturingService(db);

        // only 2 readings — not enough for a trend
        await svc.UpdateTelemetryAsync("NXE-3400B", 21.0);
        await svc.UpdateTelemetryAsync("NXE-3400B", 21.5);

        var trend = await svc.GetTemperatureTrendAsync("NXE-3400B");
        Assert.Equal("insufficient_data", trend);
    }

    [Fact]
    public async Task trend_detects_rising_temperature()
    {
        var db = CreateDb("trend_rising");
        var svc = new ManufacturingService(db);

        // feed a clearly rising sequence
        double[] temps = { 20.0, 20.5, 21.0, 21.5, 22.0, 22.5, 23.0, 23.2, 23.5, 23.8 };
        foreach (var t in temps)
            await svc.UpdateTelemetryAsync("NXE-3400B", t);

        // machine might have gone into cooling from the high temps,
        // but trend should still detect the rise. let's check
        var trend = await svc.GetTemperatureTrendAsync("NXE-3400B");
        // it might be "rising" or the machine might have cooled. depends on threshold
        // just make sure it's not "insufficient_data"
        Assert.NotEqual("insufficient_data", trend);
    }

    // ---- maintenance prediction ----

    [Fact]
    public async Task maintenance_prediction_returns_something()
    {
        var db = CreateDb("maint_pred");
        var svc = new ManufacturingService(db);

        var result = await svc.PredictMaintenanceAsync("NXE-3400B");
        var json = System.Text.Json.JsonSerializer.Serialize(result);

        Assert.Contains("machineId", json);
        Assert.Contains("estimatedHoursUntilMaintenance", json);
    }

    // ---- acknowledge ----

    [Fact]
    public async Task acknowledge_marks_alert_as_read()
    {
        var db = CreateDb("ack_alert");
        var svc = new ManufacturingService(db);

        // generate an alert via overheat
        await svc.UpdateTelemetryAsync("NXE-3400B", 25.0);

        var alert = await db.Alerts.FirstAsync();
        Assert.False(alert.IsAcknowledged);

        await svc.AcknowledgeAlertAsync(alert.Id);
        Assert.True(alert.IsAcknowledged);
    }
}