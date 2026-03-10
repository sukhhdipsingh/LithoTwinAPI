# API Changelog

Tracks notable additions and improvements to the LithoTwin API.

---

## 2026-03-10

### Added

- **`GET /api/factory/machines/compare?ids=`** — Side-by-side comparison of multiple machines including thermal headroom, throughput, fault status, and an automatic production recommendation.
- **`FailureReason` field on `ExposureResult`** — Human-readable diagnostic string populated when an exposure fails quality checks. Null on success.
- **`ReticleContaminationReplacementThreshold`** — Domain constant (0.85) governing reticle usability; replaces previous hardcoded value.
- **`AmbientCoolingRatePerTick`** — Domain constant for passive thermal drift modeling.

### Improved

- Swagger metadata now includes contact info and MIT license.
- `ExposureController.RunExposure` annotated with `[ProducesResponseType]` for cleaner API docs.
- XML documentation expanded across models (`ExposureRequest`, `TelemetryReading`, `WaferBatch`).
- Simulation engine comments refined with physics terminology (Newton's cooling approximation).
