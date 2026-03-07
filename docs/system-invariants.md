# System Invariants

These are the rules that must always hold true in the system. They are enforced in code and documented here for clarity.

Invariants are not aspirational. They are structural constraints — if any of these are violated, the system is in an invalid state.

---

## Machine Lifecycle Invariants

**INV-1:** A machine's state can only change through the `MachineStateMachine`. Direct assignment of `machine.State` bypassing the FSM is prohibited.

**INV-2:** A machine in `Faulted` state cannot transition to `Running`. The only valid exit from Faulted is `Maintenance`.

**INV-3:** A machine in `Faulted` state cannot transition to `Idle`. Unresolved faults cannot be ignored.

**INV-4:** A machine in `Running` state cannot transition directly to `Idle`. It must go through `Maintenance` or `Calibrating`.

**INV-5:** A machine in `Maintenance` cannot transition directly to `Running`. It must pass through `Calibrating` first (recalibration after repair).

**INV-6:** Every state transition produces an immutable `StateTransition` audit record with `fromState`, `toState`, `reason`, and `timestamp`.

---

## Fault Invariants

**INV-7:** A fault injected on a `Running` machine automatically transitions that machine to `Faulted`. Faults are never silent.

**INV-8:** Active faults persist until explicitly resolved. Faults do not auto-clear on state transitions.

**INV-9:** Fault resolution is only permitted when the machine is in `Maintenance` state. Attempting to resolve faults in any other state produces an error.

**INV-10:** Each fault type has a documented, deterministic effect on system behavior:
- `ThermalOverload` → temperature spikes in telemetry
- `LaserDegradation` → reduced throughput factor
- `SensorFailure` → noisy sensor readings

**INV-11:** After fault resolution, throughput factor is restored to 1.0 (nominal).

---

## Telemetry Invariants

**INV-12:** Telemetry ingestion is rejected for machines in `Maintenance` state.

**INV-13:** Sensor readings outside the plausible range `[-10°C, 80°C]` are rejected as sensor garbage.

**INV-14:** Telemetry output is a function of `machine_state + active_faults`. The formula `drift = base_drift(state) + fault_effects(faults)` ensures all telemetry is causally explainable.

**INV-15:** A `SensorFailure` fault injects noise into recorded temperature values. The recorded value may differ from the actual sensor input.

---

## Exposure Invariants

**INV-16:** Exposures are only permitted on machines in `Running` state. Attempting an exposure on any other state produces an error.

**INV-17:** Overlay error is computed deterministically from `temperature`, `focus_offset`, and `throughput_factor`. The formula:
```
overlay = (thermal_factor + focus_penalty) × fault_penalty + noise
```

**INV-18:** An exposure that exceeds the overlay spec limit (1.5nm) generates a `Warning` alert and is marked as failed.

---

## Routing Invariants

**INV-19:** Wafer batches are routed to the coldest `Running` machine (maximum thermal headroom). Only machines in `Running` state are eligible.

**INV-20:** If no machines are in `Running` state, the batch is rerouted and a system-level `Warning` alert is generated.

---

## Audit Invariants

**INV-21:** Every state transition, fault injection, and fault resolution is persisted. The system maintains a complete history of machine lifecycle events.

**INV-22:** Alerts cannot be deleted — only acknowledged. The alert history is an append-only log.
