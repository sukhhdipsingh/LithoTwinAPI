# Architecture Overview

This system models the operational behavior of EUV lithography machines under industrial constraints.

The architecture separates concerns into four layers. Each layer has a single responsibility and communicates through well-defined boundaries.

---

## Layer Responsibilities

### Domain Layer (`Domain/`)

Represents the core rules of the system. Has **no dependencies** on infrastructure, persistence, or HTTP.

| Component | Responsibility |
|---|---|
| `MachineLifecycleState` | Defines the five lifecycle states: Idle, Calibrating, Running, Faulted, Maintenance |
| `MachineStateMachine` | Enforces valid state transitions. Rejects illegal transitions with domain errors |
| `InvalidStateTransitionException` | Domain-specific error carrying source and target state |
| `FaultType` | Categorizes equipment faults with documented behavioral effects |
| `SystemConstants` | Named physical and operational constants — eliminates magic numbers |

**Design principle:** Domain objects enforce invariants. They do not perform I/O, query databases, or manage side effects.

### Service Layer (`Services/`)

Implements system behavior. Services coordinate domain rules with persistence and produce side effects (alerts, telemetry records, state transitions).

| Service | Responsibility |
|---|---|
| `MachineLifecycleService` | State transitions via FSM, health scoring, maintenance prediction |
| `FaultService` | Fault injection with causal state effects, fault resolution during maintenance |
| `TelemetryService` | Sensor ingestion with fault-aware noise injection, trend analysis, history |
| `ExposureService` | Overlay error computation, wafer routing, batch lifecycle |

**Design principle:** Each service owns one behavioral domain. A reviewer can open one file and understand that subsystem completely.

### Simulation Layer (`Simulation/`)

Drives the system forward in time. Contains the background simulation loop that generates thermal drift, detects overheat conditions, and produces telemetry readings.

| Component | Responsibility |
|---|---|
| `SimulationEngine` | Per-tick thermal computation and fault detection. Pure function of machine state + active faults |
| `ThermalSimulationService` | ASP.NET `BackgroundService` that runs the simulation loop on a 15-second interval |

**Design principle:** Simulation is an infrastructure concern, not a domain concern. The engine computes drift deterministically; the service manages the execution lifecycle.

### API Layer (`Controllers/`)

Thin HTTP surface. Controllers validate input, delegate to services, and map domain errors to HTTP status codes.

| Controller | Responsibility |
|---|---|
| `FactoryController` | State transitions, faults, telemetry, alerts, machine status |
| `ExposureController` | Exposure simulation, overlay results |
| `ReticleController` | Reticle CRUD, contamination tracking |
| `LiveController` | Server-Sent Events for real-time alert streaming |

**Design principle:** Controllers contain zero business logic. All domain rules live in services and domain objects.

---

## Data Flow

```
┌─────────────────────┐
│   API Controllers    │    ← HTTP requests
└──────────┬──────────┘
           │
           ▼
┌─────────────────────┐
│     Services         │    ← Business logic, FSM delegation
│  (Lifecycle, Fault,  │
│   Telemetry, etc.)   │
└──────────┬──────────┘
           │
     ┌─────┴──────┐
     ▼            ▼
┌─────────┐  ┌─────────────┐
│ Domain  │  │ Persistence │
│  (FSM,  │  │  (EF Core)  │
│  Rules) │  │             │
└─────────┘  └─────────────┘

Background:
┌─────────────────────┐
│  Simulation Engine   │    ← Runs on timer, produces telemetry
│  (ThermalDrift)      │       and detects fault conditions
└─────────────────────┘
```

---

## Simulation Loop

The `ThermalSimulationService` runs as a `BackgroundService`, executing a tick every 15 seconds.

Each tick:

1. Loads all machines and their active faults
2. Computes thermal drift as a function of `(state, faults)`:
   - **Running:** gradual heat accumulation (+0.05 to +0.15°C)
   - **Calibrating:** moderate heat from alignment lasers (+0.02 to +0.06°C)
   - **Idle:** slow drift toward ambient (±0.01°C)
   - **Faulted:** depends on fault type
   - **Maintenance:** no simulation
3. Applies fault-specific effects:
   - `ThermalOverload` → additional +0.5°C spike
   - `SensorFailure` → ±2°C noise
4. Checks overheat threshold → injects `ThermalOverload` fault if exceeded
5. Records telemetry reading
6. Persists all changes

The key property: **telemetry output is always explainable as `f(state, faults)`**. There is no hidden randomness without a causal source.
