# LithoTwin API

REST API that simulates telemetry monitoring, EUV exposure, and wafer routing for lithography machines. Built to explore .NET backend patterns applied to semiconductor manufacturing.

## What it does

- **Telemetry ingestion** — receives temperature readings, triggers auto-cooling on overheat with 2°C hysteresis
- **Exposure simulation** — runs simulated EUV exposures with dose/focus, computes overlay error based on thermal state
- **Wafer routing** — assigns batches to the coldest active machine for max thermal headroom
- **Batch lifecycle** — track batches from creation through processing to completion
- **Reticle management** — tracks mask contamination and usage, flags when replacement is needed
- **Machine health scoring** — weighted score (0-100) factoring temperature, uptime, and state
- **Maintenance prediction** — estimates when a machine needs maintenance, monitors overlay drift
- **Live alert stream** — SSE endpoint that pushes new alerts in real time
- **Thermal drift** — background service that simulates temperature changes, the system is "alive"
- **Trend detection** — analyzes recent telemetry to detect rising/falling/stable trends
- **CSV export** — download telemetry history as CSV for offline analysis

## Quick start

```bash
dotnet run
```

Swagger UI at `http://localhost:5159/swagger`

## Run tests

```bash
dotnet test LithoTwinAPI.Tests
```

## Endpoints

### Factory (`/api/factory`)

| Method | Route | What it does |
|--------|-------|--------------|
| `POST` | `/telemetry?machineId=X&temperature=Y` | Push a temperature reading |
| `GET`  | `/telemetry/{machineId}/history?count=50` | Temperature history |
| `GET`  | `/telemetry/{machineId}/trend` | Rising / falling / stable |
| `GET`  | `/telemetry/{machineId}/export` | Download CSV |
| `POST` | `/route-wafer` | Assign a new wafer batch |
| `POST` | `/batches/{id}/complete` | Mark batch as completed |
| `GET`  | `/system-status` | All machines + current state |
| `GET`  | `/machines/{machineId}/health` | Health score + breakdown |
| `GET`  | `/machines/{machineId}/maintenance-prediction` | Maintenance forecast |
| `GET`  | `/alerts` | Unacknowledged alerts |
| `POST` | `/alerts/{id}/acknowledge` | Mark alert as handled |
| `GET`  | `/stats` | Factory-wide statistics |

### Exposure (`/api/exposure`)

| Method | Route | What it does |
|--------|-------|--------------|
| `POST` | `/run` | Run a simulated exposure (body: `{ machineId, doseEnergy, focusOffset, layerId }`) |
| `GET`  | `/history?machineId=X` | Past exposure results with overlay errors |

### Reticles (`/api/reticle`)

| Method | Route | What it does |
|--------|-------|--------------|
| `GET`  | `/` | List all reticles |
| `GET`  | `/{id}` | Single reticle detail |
| `POST` | `/{id}/inspect` | Simulate inspection (increases contamination) |

### Live (`/api/live`)

| Method | Route | What it does |
|--------|-------|--------------|
| `GET`  | `/alerts` | SSE stream of new alerts (open in browser) |

## Architecture

```
Controllers
├── FactoryController     — telemetry, routing, health, stats, maintenance
├── ExposureController    — exposure simulation
├── ReticleController     — reticle CRUD + inspection
└── LiveController        — SSE alert stream

Services
├── ManufacturingService  — core business logic
└── ThermalDriftService   — background thermal simulation (BackgroundService)

Data
└── AppDbContext           — EF Core InMemory, seeded with 3 machines + 3 reticles

Models
├── Machine, WaferBatch, Alert
├── TelemetryReading, ExposureRequest, ExposureResult
└── Reticle

Tests
└── ManufacturingServiceTests — 17 xUnit tests covering core logic
```

Seeded machines: `NXE-3400B`, `NXE-3600D` (active), `TWINSCAN-EXE` (maintenance).
Seeded reticles: `MASK-M1-v3`, `MASK-VIA1-v1`, `MASK-POLY-v2`.

## Stack

- .NET 7 / ASP.NET Core
- Entity Framework Core (InMemory)
- xUnit for testing
- Swashbuckle (Swagger)
- BackgroundService for thermal simulation
- Server-Sent Events for live monitoring
