using LithoTwinAPI.Domain;
using LithoTwinAPI.Models;

namespace LithoTwinAPI.Tests;

public class StateMachineTests
{
    [Fact]
    public void idle_to_calibrating_is_valid()
    {
        var fsm = new MachineStateMachine(MachineLifecycleState.Idle);
        var transition = fsm.TransitionTo(MachineLifecycleState.Calibrating, "TEST-01", "Starting calibration");

        Assert.Equal(MachineLifecycleState.Calibrating, fsm.CurrentState);
        Assert.Equal(MachineLifecycleState.Idle, transition.FromState);
        Assert.Equal(MachineLifecycleState.Calibrating, transition.ToState);
    }

    [Fact]
    public void calibrating_to_running_is_valid()
    {
        var fsm = new MachineStateMachine(MachineLifecycleState.Calibrating);
        fsm.TransitionTo(MachineLifecycleState.Running, "TEST-01", "Calibration complete");

        Assert.Equal(MachineLifecycleState.Running, fsm.CurrentState);
    }

    [Fact]
    public void running_to_idle_is_forbidden()
    {
        var fsm = new MachineStateMachine(MachineLifecycleState.Running);

        var ex = Assert.Throws<InvalidStateTransitionException>(
            () => fsm.TransitionTo(MachineLifecycleState.Idle, "TEST-01", "Attempt shutdown"));

        Assert.Equal(MachineLifecycleState.Running, ex.FromState);
        Assert.Equal(MachineLifecycleState.Idle, ex.ToState);
        // State must not have changed
        Assert.Equal(MachineLifecycleState.Running, fsm.CurrentState);
    }

    [Fact]
    public void faulted_to_running_is_forbidden()
    {
        var fsm = new MachineStateMachine(MachineLifecycleState.Faulted);

        Assert.Throws<InvalidStateTransitionException>(
            () => fsm.TransitionTo(MachineLifecycleState.Running, "TEST-01", "Skip maintenance"));

        Assert.Equal(MachineLifecycleState.Faulted, fsm.CurrentState);
    }

    [Fact]
    public void faulted_to_maintenance_is_valid()
    {
        var fsm = new MachineStateMachine(MachineLifecycleState.Faulted);
        fsm.TransitionTo(MachineLifecycleState.Maintenance, "TEST-01", "Begin fault resolution");

        Assert.Equal(MachineLifecycleState.Maintenance, fsm.CurrentState);
    }

    [Fact]
    public void maintenance_to_running_is_forbidden_must_recalibrate()
    {
        var fsm = new MachineStateMachine(MachineLifecycleState.Maintenance);

        Assert.Throws<InvalidStateTransitionException>(
            () => fsm.TransitionTo(MachineLifecycleState.Running, "TEST-01", "Skip calibration"));
    }

    [Fact]
    public void full_recovery_path_faulted_to_running_via_maintenance_and_calibrating()
    {
        var fsm = new MachineStateMachine(MachineLifecycleState.Faulted);

        fsm.TransitionTo(MachineLifecycleState.Maintenance, "TEST-01", "Begin maintenance");
        fsm.TransitionTo(MachineLifecycleState.Calibrating, "TEST-01", "Recalibrating after repair");
        fsm.TransitionTo(MachineLifecycleState.Running, "TEST-01", "Calibration passed, resuming production");

        Assert.Equal(MachineLifecycleState.Running, fsm.CurrentState);
    }

    [Fact]
    public void transition_to_same_state_is_rejected()
    {
        var fsm = new MachineStateMachine(MachineLifecycleState.Running);

        Assert.Throws<InvalidStateTransitionException>(
            () => fsm.TransitionTo(MachineLifecycleState.Running, "TEST-01", "No change"));
    }

    [Fact]
    public void can_transition_to_reports_correctly()
    {
        var fsm = new MachineStateMachine(MachineLifecycleState.Running);

        Assert.True(fsm.CanTransitionTo(MachineLifecycleState.Faulted));
        Assert.True(fsm.CanTransitionTo(MachineLifecycleState.Maintenance));
        Assert.False(fsm.CanTransitionTo(MachineLifecycleState.Idle));
        Assert.False(fsm.CanTransitionTo(MachineLifecycleState.Running));
    }

    [Fact]
    public void get_allowed_transitions_returns_correct_set()
    {
        var fsm = new MachineStateMachine(MachineLifecycleState.Faulted);
        var allowed = fsm.GetAllowedTransitions();

        Assert.Single(allowed);
        Assert.Contains(MachineLifecycleState.Maintenance, allowed);
    }
}
