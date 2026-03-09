# LithoTwin API

**A state-driven digital twin for EUV lithography machines.**

Models the full machine lifecycle, from idle through calibration, production, fault detection, and maintenance — with explicit state constraints, deterministic fault propagation, and causal telemetry simulation.

Built with .NET 7. Not a tutorial; an exercise in domain modeling under industrial constraints.

---

## Engineering Motivation

Since EUV lithography machines are very complex production equipment in semiconductor manufacturing, infact their operational lifecycle involves strict sequencing: a machine cannot jump from faulted to running, maintenance requires recalibration before production resumes, and faults must propagate causally into telemetry and throughput.

This project models those constraints as a **finite state machine** enforced at the domain layer.

**Design goals:**
- Explicit state transitions — invalid transitions throw domain errors, not silent no-ops
- Causal fault propagation — faults have documented, deterministic effects on system behavior
- Deterministic simulation — telemetry output = f(machine_state, active_faults)
- Auditable lifecycle — every state change is recorded with reason and timestamp

---

## System Overview

```
Domain/                          Core constraints (no infrastructure dependencies)
├── MachineLifecycleState        5-state lifecycle enum
├── MachineStateMachine          Centralized FSM with explicit transition rules
├── InvalidStateTransitionException   Typed domain error
├── FaultType                    ThermalOverload, LaserDegradation, SensorFailure
└── SystemConstants              Named physical + operational constants

Services/                        Business logic — each service owns one behavioral domain
├── MachineLifecycleService      State transitions via FSM, health scoring, maintenance prediction
├── FaultService                 Fault injection, causal propagation, resolution during maintenance
├── TelemetryService             Sensor ingestion with fault-aware noise, trend analysis
├── ExposureService              Overlay error computation, wafer routing, batch lifecycle
└── AlertService                 System alerts, factory statistics

Simulation/                      Background simulation loop
├── SimulationEngine             Pure thermal drift computation: f(state, faults) → drift
└── ThermalSimulationService     BackgroundService executing the simulation tick

Controllers/                     Thin HTTP surface — zero business logic
├── FactoryController            State transitions, faults, telemetry, alerts
├── ExposureController           Exposure simulation
├── ReticleController            Reticle CRUD + contamination
└── LiveController               Server-Sent Events for real-time alerts

docs/                            Engineering documentation
├── architecture.md              Layer responsibilities, data flow, simulation loop
├── design-decisions.md          8 architectural decisions with reasoning and trade-offs
└── system-invariants.md         22 named system invariants
```

---

## Machine Lifecycle (Finite State Machine)

```
         ┌─────────────────────────────────────────┐
         │                                         │
    ┌────▼───┐    ┌────────────┐    ┌─────────┐   │
    │  Idle  ├───►│ Calibrating├───►│ Running │   │
    └────┬───┘    └─────┬──────┘    └──┬──┬───┘   │
         │              │ (abort)      │  │       │
         │              ▼              │  │       │
         │           ┌──────┐          │  │       │
         │           │ Idle │          │  │       │
         │           └──────┘          │  │       │
         │                     ┌───────┘  │       │
         │                     ▼          │       │
    ┌────▼────────┐    ┌───────────┐      │       │
    │ Maintenance │◄───┤  Faulted  │◄─────┘       │
    └──────┬──────┘    └───────────┘              │
           │                                      │
           └──────► Calibrating ──────► Running ──┘
```

**Transition rules** — enforced by `MachineStateMachine`, not scattered conditionals:

| From | Allowed Targets |
|---|---|
| Idle | Calibrating, Maintenance |
| Calibrating | Running, Idle (abort) |
| Running | Faulted, Maintenance (planned), Calibrating (recalibration) |
| Faulted | Maintenance (mandatory — only exit path) |
| Maintenance | Calibrating (recalibrate), Idle (shutdown) |

**Explicitly forbidden:** Running → Idle, Faulted → Running, Faulted → Idle, Faulted → Calibrating.

Invalid transitions throw `InvalidStateTransitionException` with source/target state.

---

## System Invariants

The system enforces 22 named invariants (see [docs/system-invariants.md](docs/system-invariants.md)). Key examples:

- **INV-2:** A machine in `Faulted` state cannot transition to `Running`
- **INV-7:** Fault injection on a `Running` machine automatically transitions it to `Faulted`
- **INV-9:** Fault resolution is only permitted in `Maintenance` state
- **INV-14:** Telemetry output = `f(machine_state, active_faults)` — always causally explainable
- **INV-16:** Exposures are only permitted on machines in `Running` state
- **INV-19:** Wafer routing selects the coldest Running machine (maximum thermal headroom)

---

## Fault Propagation Model

Faults are not decorative. Each type has a documented, causal effect:

| Fault Type | Telemetry Effect | Throughput Effect | State Effect |
|---|---|---|---|
| `ThermalOverload` | +0.5°C/tick temperature spike | None | Running → Faulted |
| `LaserDegradation` | Overlay error increases | −30% per fault | Running → Faulted |
| `SensorFailure` | ±2°C noise injected | None | Running → Faulted |

**Persistence:** Faults remain active until explicitly resolved during Maintenance. After resolution, the machine must recalibrate before resuming production.

**Determinism:** System behavior is always `f(machine_state, active_faults)`. There is no unexplained randomness.

---

## Example System Behavior

**Normal production cycle:**
```
Idle → Calibrating → Running → [exposures] → Maintenance → Calibrating → Running
```

**Fault scenario:**
```
Running → [ThermalOverload detected] → Faulted
Faulted → Maintenance → [resolve faults] → Calibrating → Running
```

**Forbidden path (produces domain error):**
```
Faulted → Running  ✗  InvalidStateTransitionException
Running → Idle     ✗  InvalidStateTransitionException
```

---

## Architecture Overview

The system separates concerns into four layers:

| Layer | Responsibility | Example |
|---|---|---|
| **Domain** | Rules and constraints (no I/O) | `MachineStateMachine` validates transitions |
| **Services** | Business logic and side effects | `FaultService.InjectFaultAsync()` applies degradation |
| **Simulation** | Time-driven background computation | `SimulationEngine.ComputeThermalDrift()` |
| **API** | HTTP surface, input validation | Controllers delegate to services |

See [docs/architecture.md](docs/architecture.md) for data flow diagrams and simulation loop details.

---

## Technology Stack

| Component | Technology |
|---|---|
| Runtime | .NET 7 / ASP.NET Core |
| Persistence | EF Core (InMemory default, SQLite opt-in) |
| Background processing | `BackgroundService` for thermal simulation |
| Real-time | Server-Sent Events |
| Testing | xUnit (32 tests: FSM rules, fault propagation, service behavior) |
| API docs | Swagger / OpenAPI |

---

## Design Philosophy

- **Explicit constraints over feature breadth.** The system has 5 states and 22 invariants, not 50 endpoints. Constraints are the feature.
- **Deterministic behavior over randomness.** Every telemetry value traces back to a cause. Random noise exists only where it models a real physical phenomenon (sensor jitter, thermal fluctuation).
- **Domain modeling as a reasoning tool.** The FSM isn't decorative — it prevents invalid states at compile-time-equivalent strength. A machine cannot silently skip maintenance.
- **Systems should communicate their rules.** Rules live in `MachineStateMachine`, not scattered across controllers. A reviewer opening one file sees the complete lifecycle.

See [docs/design-decisions.md](docs/design-decisions.md) for 8 architectural decisions with reasoning and trade-offs.

---

## Intentional Simplifications

| What's simplified | Why |
|---|---|
| No persistence by default | Focus is system modeling, not database engineering. SQLite available via config. |
| No UI | Backend system design exercise, not full-stack application |
| Linear overlay model | Real EUV overlay depends on dozens of variables. Linear approximation demonstrates causal relationships without pretending to be a physics engine. |
| No authentication | Out of scope for system modeling |
| Single process | Complexity budget is spent on domain modeling, not infrastructure plumbing |
| No real hardware interface | System simulates machine behavior based on domain rules |

---

## Possible Production Evolution

If this were a production system, the next engineering investments would be:

- **Event sourcing** — reconstruct machine state from the transition log
- **Rule engine** — replace hardcoded thermal thresholds with configurable fault detection rules
- **Multi-machine dependency graphs** — model how a fault in one stage affects downstream routing
- **Histogram-based overlay analysis** — replace scalar overlay with wafer-level spatial maps
- **Distributed simulation** — separate the simulation engine from the API for independent scaling
