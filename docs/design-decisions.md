# Design Decisions

This document explains the major architectural decisions in the system and the reasoning behind each one.

---

## 1. Finite State Machine for Machine Lifecycle

**Decision:** Model the machine lifecycle as a finite state machine with five explicit states and a centralized transition validator.

**Alternatives considered:**
- Boolean flags (`isRunning`, `isFaulted`) — leads to invalid combinations, no transition validation
- Enum-only approach without transition rules — allows illegal state jumps

**Why FSM:**

Industrial machines follow strict operational sequences. A machine cannot go from Faulted to Running without maintenance and recalibration. A boolean-flag approach cannot enforce this ordering — it can only describe the _current_ state, not the _allowed next_ states.

The FSM makes the lifecycle rules explicit, testable, and centralized. Any attempt to violate the lifecycle produces a typed domain error (`InvalidStateTransitionException`) rather than a silent corruption.

**Trade-off:** The FSM adds a layer of indirection. Every state change goes through `MachineStateMachine.TransitionTo()` rather than directly setting `machine.State`. This is intentional — it prevents state changes from bypassing validation.

---

## 2. Domain Layer Has No Infrastructure Dependencies

**Decision:** The `Domain/` folder contains only pure C# classes with no references to Entity Framework, ASP.NET, or any I/O framework.

**Why:**

Domain rules should be testable without a database, HTTP stack, or dependency injection container. The `MachineStateMachine` can be instantiated directly in a unit test with `new MachineStateMachine(initialState)` — no mocking, no setup.

This also makes the domain portable. If the system were rehosted from ASP.NET to a console worker or an Azure Function, the domain layer would require zero changes.

---

## 3. Causal Fault Propagation

**Decision:** Each fault type has a specific, documented effect on system behavior. Faults are not decorative — they causally alter telemetry output and throughput.

| Fault | Effect |
|---|---|
| `ThermalOverload` | Increases temperature readings by +0.5°C per simulation tick |
| `LaserDegradation` | Reduces throughput factor by 30%, increases overlay error |
| `SensorFailure` | Injects ±2°C random noise into recorded sensor values |

**Why:**

Random telemetry is meaningless. If temperature values just fluctuate without cause, the system communicates nothing about equipment behavior. By tying telemetry output to `f(machine_state, active_faults)`, the system becomes explainable — a reviewer (or operator) can look at degraded telemetry and trace it back to a specific fault.

**Trade-off:** The fault effects are simplified approximations of real physics. This is intentional. The goal is to demonstrate causal modeling, not to build a physics engine.

---

## 4. Separated Service Responsibilities

**Decision:** Split business logic across focused services rather than a single monolithic service class.

| Service | Scope |
|---|---|
| `MachineLifecycleService` | State transitions, health, maintenance prediction |
| `FaultService` | Fault injection, resolution, degradation effects |
| `TelemetryService` | Sensor ingestion, history, trend analysis |
| `ExposureService` | Overlay computation, wafer routing, batch lifecycle |

**Why:**

A single `ManufacturingService` handling everything forces a reviewer to scan 500+ lines to understand one behavior. Splitting by domain concern means each file is self-contained — opening `FaultService.cs` reveals everything about how faults work.

This also enables independent testing. Fault tests don't need to reason about exposure logic, and exposure tests don't need to understand fault resolution.

---

## 5. Deterministic Simulation Over Random Generation

**Decision:** Telemetry values are derived from machine state and active faults, not randomly generated.

**Formula:** `telemetry_output = base_drift(state) + fault_effects(active_faults)`

**Why:**

Random simulation is easy but communicates nothing. A system that generates random temperatures between 20°C and 25°C looks like a tutorial exercise. A system where temperature rises _because_ the machine is running, and spikes _because_ a thermal overload fault is active, demonstrates understanding of causal modeling.

Each simulation tick produces output that can be traced to a specific cause. This is the same principle used in real industrial monitoring systems: anomaly detection requires understanding what _should_ happen before you can detect what _shouldn't_.

---

## 6. State Transition Audit Log

**Decision:** Every state transition is recorded as an immutable `StateTransition` entity with `fromState`, `toState`, `reason`, and `timestamp`.

**Why:**

In industrial systems, knowing that a machine is in Maintenance is less useful than knowing _when_ it entered Maintenance, _from which state_, and _why_. The transition log enables full lifecycle reconstruction and supports compliance requirements.

This also serves as a debugging tool — if the system reaches an unexpected state, the transition history provides a complete trace.

---

## 7. InMemory Persistence as Default

**Decision:** Use EF Core InMemory provider by default, with SQLite available as an opt-in configuration.

**Why:**

The primary purpose of this project is demonstrating system modeling, not database engineering. InMemory keeps the demo zero-dependency — `dotnet run` works immediately without database setup.

SQLite is available (`UseSqlite: true` in appsettings) to prove that the persistence layer is real and not just an in-memory trick. But making it the default would add friction without adding engineering signal for the domain modeling this project emphasizes.

---

## 8. Named Constants Over Configuration

**Decision:** Physical and operational constants live in `SystemConstants`, not in `appsettings.json`.

**Why:**

These values represent physical properties of the simulated system (thermal expansion coefficient, overlay spec limit, sensor range). They are not deployment configuration — they're domain knowledge. Putting them in appsettings would conflate "how the system is deployed" with "how the system behaves."

Named constants also make the codebase self-documenting. A reviewer seeing `SystemConstants.ThermalExpansionCoefficientNmPerC` immediately understands what `0.08` means — without needing to find a config file.
