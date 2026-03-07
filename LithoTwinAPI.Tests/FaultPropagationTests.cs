using LithoTwinAPI.Data;
using LithoTwinAPI.Domain;
using LithoTwinAPI.Models;
using LithoTwinAPI.Services;
using Microsoft.EntityFrameworkCore;

namespace LithoTwinAPI.Tests;

public class FaultPropagationTests
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

    [Fact]
    public async Task fault_injection_transitions_running_machine_to_faulted()
    {
        var db = CreateDb("fault_to_faulted");
        var svc = new FaultService(db);

        var fault = await svc.InjectFaultAsync("NXE-3400B", FaultType.ThermalOverload, "Test fault");

        var machine = await db.Machines.FindAsync("NXE-3400B");
        Assert.Equal(MachineLifecycleState.Faulted, machine!.State);
        Assert.True(fault.IsActive);
    }

    [Fact]
    public async Task faulted_machine_rejects_exposure()
    {
        var db = CreateDb("fault_no_exposure");
        var faultSvc = new FaultService(db);
        var exposureSvc = new ExposureService(db);

        await faultSvc.InjectFaultAsync("NXE-3400B", FaultType.LaserDegradation, "Laser issue");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => exposureSvc.RunExposureAsync(new ExposureRequest { MachineId = "NXE-3400B" }));
    }

    [Fact]
    public async Task faults_persist_until_resolved_in_maintenance()
    {
        var db = CreateDb("fault_persist");
        var faultSvc = new FaultService(db);
        var lifecycleSvc = new MachineLifecycleService(db);

        await faultSvc.InjectFaultAsync("NXE-3400B", FaultType.SensorFailure, "Sensor drift");

        // Faults cannot be resolved outside Maintenance state
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => faultSvc.ResolveFaultsAsync("NXE-3400B"));

        // Transition to Maintenance
        await lifecycleSvc.TransitionStateAsync(
            "NXE-3400B", MachineLifecycleState.Maintenance, "Begin repair");

        var resolved = await faultSvc.ResolveFaultsAsync("NXE-3400B");
        Assert.Single(resolved);
        Assert.NotNull(resolved[0].ResolvedAt);
    }

    [Fact]
    public async Task laser_degradation_reduces_throughput_factor()
    {
        var db = CreateDb("fault_throughput");
        var svc = new FaultService(db);

        var before = (await db.Machines.FindAsync("NXE-3400B"))!.ThroughputFactor;
        await svc.InjectFaultAsync("NXE-3400B", FaultType.LaserDegradation, "Power drop");
        var after = (await db.Machines.FindAsync("NXE-3400B"))!.ThroughputFactor;

        Assert.True(after < before, "ThroughputFactor should decrease with LaserDegradation");
    }

    [Fact]
    public async Task fault_resolution_restores_throughput()
    {
        var db = CreateDb("fault_restore");
        var faultSvc = new FaultService(db);
        var lifecycleSvc = new MachineLifecycleService(db);

        await faultSvc.InjectFaultAsync("NXE-3400B", FaultType.LaserDegradation, "Power drop");
        await lifecycleSvc.TransitionStateAsync(
            "NXE-3400B", MachineLifecycleState.Maintenance, "Repair");
        await faultSvc.ResolveFaultsAsync("NXE-3400B");

        var machine = await db.Machines.FindAsync("NXE-3400B");
        Assert.Equal(1.0, machine!.ThroughputFactor);
    }

    [Fact]
    public async Task fault_generates_alert()
    {
        var db = CreateDb("fault_alert");
        var svc = new FaultService(db);

        await svc.InjectFaultAsync("NXE-3400B", FaultType.ThermalOverload, "Overheating");

        var alerts = await db.Alerts
            .Where(a => a.MachineId == "NXE-3400B" && a.Severity == AlertSeverity.Critical)
            .ToListAsync();

        Assert.NotEmpty(alerts);
        Assert.Contains(alerts, a => a.Message.Contains("ThermalOverload"));
    }

    [Fact]
    public async Task fault_creates_state_transition_record()
    {
        var db = CreateDb("fault_transition_log");
        var svc = new FaultService(db);

        await svc.InjectFaultAsync("NXE-3400B", FaultType.SensorFailure, "Sensor broken");

        var transitions = await db.StateTransitions
            .Where(t => t.MachineId == "NXE-3400B" && t.ToState == MachineLifecycleState.Faulted)
            .ToListAsync();

        Assert.NotEmpty(transitions);
    }
}
