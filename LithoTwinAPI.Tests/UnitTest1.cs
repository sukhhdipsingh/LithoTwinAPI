using LithoTwinAPI.Data;
using LithoTwinAPI.Domain;
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
        var faultSvc = new FaultService(db);
        var svc = new TelemetryService(db, faultSvc);

        await svc.IngestReadingAsync("NXE-3400B", 22.5);

        var machine = await db.Machines.FindAsync("NXE-3400B");
        Assert.Equal(22.5, machine!.CurrentTemperature);
    }

    [Fact]
    public async Task telemetry_rejects_maintenance_machine()
    {
        var db = CreateDb("telemetry_maintenance");
        var faultSvc = new FaultService(db);
        var svc = new TelemetryService(db, faultSvc);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.IngestReadingAsync("TWINSCAN-EXE", 20.0));
    }

    [Fact]
    public async Task telemetry_rejects_out_of_range_values()
    {
        var db = CreateDb("telemetry_range");
        var faultSvc = new FaultService(db);
        var svc = new TelemetryService(db, faultSvc);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => svc.IngestReadingAsync("NXE-3400B", 999.0));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => svc.IngestReadingAsync("NXE-3400B", -50.0));
    }

    [Fact]
    public async Task telemetry_saves_reading_to_history()
    {
        var db = CreateDb("telemetry_history");
        var faultSvc = new FaultService(db);
        var svc = new TelemetryService(db, faultSvc);

        await svc.IngestReadingAsync("NXE-3400B", 22.0);
        await svc.IngestReadingAsync("NXE-3400B", 22.5);

        var history = await svc.GetHistoryAsync("NXE-3400B");
        Assert.Equal(2, history.Count);
    }

    // ---- wafer routing ----

    [Fact]
    public async Task routing_picks_coldest_running_machine()
    {
        var db = CreateDb("routing_coldest");
        var svc = new ExposureService(db);

        var batch = await svc.RouteWaferBatchAsync();

        Assert.Equal("NXE-3400B", batch.AssignedMachineId);
        Assert.Equal(BatchStatus.Processing, batch.Status);
    }

    [Fact]
    public async Task routing_reroutes_when_no_running_machines()
    {
        var db = CreateDb("routing_none");
        var faultSvc = new FaultService(db);

        await faultSvc.InjectFaultAsync("NXE-3400B", FaultType.ThermalOverload, "test");
        await faultSvc.InjectFaultAsync("NXE-3600D", FaultType.ThermalOverload, "test");

        var exposureSvc = new ExposureService(db);
        var batch = await exposureSvc.RouteWaferBatchAsync();

        Assert.Equal(BatchStatus.Rerouted, batch.Status);
    }

    // ---- exposure ----

    [Fact]
    public async Task exposure_produces_overlay_result()
    {
        var db = CreateDb("exposure_basic");
        var svc = new ExposureService(db);

        var result = await svc.RunExposureAsync(new ExposureRequest
        {
            MachineId = "NXE-3400B",
            DoseEnergy = 30.0,
            FocusOffset = 0,
            LayerId = "M1"
        });

        Assert.Equal("NXE-3400B", result.MachineId);
        Assert.False(double.IsNaN(result.OverlayErrorX));
        Assert.False(double.IsNaN(result.OverlayErrorY));
    }

    [Fact]
    public async Task exposure_heats_up_machine()
    {
        var db = CreateDb("exposure_heat");
        var svc = new ExposureService(db);

        var before = (await db.Machines.FindAsync("NXE-3400B"))!.CurrentTemperature;
        await svc.RunExposureAsync(new ExposureRequest { MachineId = "NXE-3400B" });
        var after = (await db.Machines.FindAsync("NXE-3400B"))!.CurrentTemperature;

        Assert.True(after > before, "Temperature should increase after exposure");
    }

    [Fact]
    public async Task exposure_rejects_non_running_machine()
    {
        var db = CreateDb("exposure_inactive");
        var svc = new ExposureService(db);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.RunExposureAsync(new ExposureRequest { MachineId = "TWINSCAN-EXE" }));
    }

    [Fact]
    public async Task exposure_increments_counters()
    {
        var db = CreateDb("exposure_counters");
        var svc = new ExposureService(db);

        var countBefore = (await db.Machines.FindAsync("NXE-3400B"))!.ExposureCount;
        await svc.RunExposureAsync(new ExposureRequest { MachineId = "NXE-3400B" });
        var countAfter = (await db.Machines.FindAsync("NXE-3400B"))!.ExposureCount;

        Assert.Equal(countBefore + 1, countAfter);
    }

    // ---- health ----

    [Fact]
    public async Task health_returns_structured_response()
    {
        var db = CreateDb("health_range");
        var svc = new MachineLifecycleService(db);

        var result = await svc.ComputeHealthScoreAsync("NXE-3400B");
        var json = System.Text.Json.JsonSerializer.Serialize(result);

        Assert.Contains("overallScore", json);
        Assert.Contains("activeFaultCount", json);
        Assert.Contains("throughputFactor", json);
    }

    // ---- trend ----

    [Fact]
    public async Task trend_returns_insufficient_with_few_readings()
    {
        var db = CreateDb("trend_few");
        var faultSvc = new FaultService(db);
        var svc = new TelemetryService(db, faultSvc);

        await svc.IngestReadingAsync("NXE-3400B", 21.0);
        await svc.IngestReadingAsync("NXE-3400B", 21.5);

        var trend = await svc.ComputeTrendAsync("NXE-3400B");
        Assert.Equal("insufficient_data", trend);
    }

    // ---- state transitions ----

    [Fact]
    public async Task state_transition_records_audit_entry()
    {
        var db = CreateDb("transition_audit");
        var svc = new MachineLifecycleService(db);

        var transition = await svc.TransitionStateAsync(
            "NXE-3400B", MachineLifecycleState.Maintenance, "Planned maintenance");

        Assert.Equal(MachineLifecycleState.Running, transition.FromState);
        Assert.Equal(MachineLifecycleState.Maintenance, transition.ToState);

        var history = await svc.GetTransitionHistoryAsync("NXE-3400B");
        Assert.Contains(history, t => t.ToState == MachineLifecycleState.Maintenance);
    }

    [Fact]
    public async Task invalid_state_transition_throws_domain_error()
    {
        var db = CreateDb("transition_invalid");
        var svc = new MachineLifecycleService(db);

        await Assert.ThrowsAsync<InvalidStateTransitionException>(
            () => svc.TransitionStateAsync(
                "NXE-3400B", MachineLifecycleState.Idle, "Attempt direct shutdown"));
    }

    // ---- alerts ----

    [Fact]
    public async Task acknowledge_marks_alert_as_read()
    {
        var db = CreateDb("ack_alert");
        var faultSvc = new FaultService(db);
        var alertSvc = new AlertService(db);

        await faultSvc.InjectFaultAsync("NXE-3400B", FaultType.ThermalOverload, "Test");

        var alert = await db.Alerts.FirstAsync();
        Assert.False(alert.IsAcknowledged);

        await alertSvc.AcknowledgeAsync(alert.Id);
        Assert.True(alert.IsAcknowledged);
    }
}