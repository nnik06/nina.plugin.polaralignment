# UPAS Supervisor Coarse-Planning Evidence V2

Status: proposed cross-project contract; non-actuating.

This contract defines the evidence TPPA needs before it can plan a correction
from a large initial polar error. It does not authorize motion. Transaction
submission remains separately uncommissioned and fail-closed.

## Boundary

TPPA owns sky measurement, correction intent, and independent post-move sky
verification. The UPAS supervisor owns physical position, travel accounting,
witness acquisition, calibration, command execution, and durable receipts.
Hall sensors and P20 images measure UPAS position only. They do not measure
polar alignment or establish absolute sky accuracy.

The frozen `/v1/status` response is insufficient for coarse planning. Its
`witnessedAxes` collection proves only that a baseline exists; it does not
provide signed position, uncertainty, freshness, or calibration evidence.
Those fields must not be inferred from controller `MPos`, command history, or
status-v1 capability flags.

## Required Evidence

A separately versioned, authenticated evidence response must contain:

- `schemaVersion`: exactly `2`.
- `supervisorSessionId`: the current non-empty supervisor session UUID.
- `evidenceId`: immutable content identity for this snapshot.
- `capturedUtc`: UTC timestamp for forensic correlation.
- `capturedMonotonicNs`: supervisor-monotonic capture time.
- `expiresMonotonicNs`: strict freshness deadline.
- `coordinateConvention`: exactly
  `azEastPositive_altUpPositive`.
- `hardLimitDegrees`: signed software limits, currently `[-5.4,+5.4]` for
  each axis.
- `operationalLimitDegrees`: the separately configured guarded endpoints.
- `axes.az` and `axes.alt`, each containing:
  - signed physical position in degrees;
  - conservative position uncertainty in degrees;
  - witness ID, artifact SHA-256, and witness capture monotonic time;
  - sensor source and sensor-calibration ID;
  - agreement state when Hall and P20 are both required.
- `responseCalibration`, containing:
  - immutable calibration ID and artifact SHA-256;
  - exact command-to-position equation and units;
  - the same coordinate convention as the evidence snapshot;
  - full 2x2 signed response matrix, not independent scalar gains;
  - conservative coefficient uncertainty/covariance;
  - fixed command uncertainty and reversal/deadband bounds;
  - applicable direction, temperature, load, and validity ranges;
  - capture UTC and monotonic expiry.
- remaining per-axis session travel and cumulative planned travel.
- active lease and transaction identity visible to the authenticated caller.
- lock state and physical-motion capabilities.

All numbers must be finite. Property sets are exact for the declared schema.
Unknown, missing, duplicate, stale, contradictory, or out-of-range evidence is
a denial, never a request to fall back to local or legacy motion.

## Admission Rules

Coarse planning is denied unless all of the following hold in one immutable
snapshot:

1. The snapshot and every referenced witness/calibration are fresh.
2. Both axes have signed physical positions and conservative uncertainties.
3. Required Hall/P20 sources agree within a preregistered threshold.
4. The response calibration covers the current position, direction,
   temperature, and load.
5. The entire uncertainty-expanded planned path remains inside the configured
   operational endpoints and software hard limits.
6. Per-axis and cumulative session-travel budgets cover the path.
7. No lock or active conflicting transaction exists.
8. The supervisor session and lease remain unchanged through transaction
   admission.
9. The TPPA determination itself passes all independent geometry, quality,
   repeatability, refraction, and freshness gates.

A total-error scalar cannot prove travel feasibility. Admission uses the
signed azimuth/altitude error vector, current signed axis positions, full 2x2
response model, and uncertainty-expanded path.

## Motion Separation

Evidence readiness and physical-motion authority are separate states.
Accepting this evidence may produce a non-actuating plan only. A future motion
client must additionally implement the commissioned transaction protocol from
`UPAS_SUPERVISOR_CLIENT_BOUNDARY.md`, including:

- durable idempotency and canonical request hashing;
- one-axis transactions executed sequentially;
- lease ownership and restart recovery;
- fresh pre- and post-move witnesses;
- terminal `VERIFIED` polling and immutable receipts;
- ambiguous-outcome poisoning with no blind retry or rollback;
- independent fresh TPPA remeasurement after every correction step.

## Safety Limits

The software hard limit is proposed as +/-5.4 degrees because reported
physical stops are near +/-6 degrees. The guarded operational endpoint and
additional reserve remain separate policy values. This document does not
approve reducing that reserve or admitting the final 4-5 degree starting
stratum to physical motion.

## Qualification

Implementation requires contract fixtures shared between repositories,
including canonical valid examples and mutations for every rejection rule.
Playback tests must prove that status V1, controller `MPos`, stale witnesses,
scalar-only gains, mismatched conventions, and uncertainty-crossing paths all
fail closed without contacting a motion endpoint.
