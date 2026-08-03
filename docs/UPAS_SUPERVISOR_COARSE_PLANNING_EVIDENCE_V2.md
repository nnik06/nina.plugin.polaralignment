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

Parsing uses a positive schema allowlist. Only integer schema version `2` is
eligible, and that check occurs before any other field is interpreted. Missing,
V1, malformed, or future versions are denied without a compatibility fallback.

## Request Binding And Freshness

TPPA generates a cryptographically random request nonce and records request and
response times on its own monotonic clock. The supervisor response must echo the
nonce byte-for-byte and contain:

- `supervisorSessionId`: current non-empty supervisor session UUID;
- `supervisorBootId`: immutable identity of the current supervisor boot;
- `evidenceId`: SHA-256 identity of the RFC 8785 canonical response payload,
  excluding `evidenceId` itself;
- `capturedUtc`: UTC timestamp used only for forensic correlation;
- `capturedMonotonicNs`: capture time on the supervisor clock;
- `serverProcessingMilliseconds`: supervisor-monotonic request-to-response
  processing duration;
- `validForMilliseconds`: bounded relative lifetime; and
- `oldestEvidenceAgeMilliseconds`: age of the oldest included witness or
  calibration when the response was emitted.

TPPA never compares its monotonic clock value directly with a supervisor value.
It checks its own round-trip time and requires both round-trip time and
`oldestEvidenceAgeMilliseconds + roundTripMilliseconds` to fit compiled
freshness limits and `validForMilliseconds`. Freshness is checked at parse time
and again immediately before plan emission. Witness times must be bracketed by
the nonce-bound supervisor request and response, and their maximum age spread
must fit a compiled coherence window.

## Required Evidence

A separately versioned, authenticated response has exact property sets and
contains:

- `schemaVersion`: integer `2` exactly;
- the request-binding and freshness fields above;
- `coordinateConvention`: exactly
  `azEastPositive_altUpPositive`;
- `hardLimitDegrees`: a required cross-check exactly equal to the compiled
  TPPA limits `[-5.4,+5.4]` for each axis; payload data cannot change them;
- `operationalReserveDegrees`: a non-negative shrink-only policy cross-check;
- `operationalLimitDegrees`: endpoints derived from the compiled hard limit
  and reserve, also used only as a mismatch-rejecting cross-check;
- `axes.az` and `axes.alt`, each containing:
  - signed physical position in degrees;
  - conservative one-standard-deviation position uncertainty in degrees and
    covariance entries in square degrees;
  - engagement state (`positive`, `negative`, or `unknown`);
  - witness ID, artifact SHA-256, age at response, source identity,
    source-correlation group, and sensor-calibration ID; and
  - agreement state when Hall and P20 are both required. Disagreement rejects;
    it is never averaged into a position;
- `responseCalibration`, containing:
  - immutable calibration ID and artifact SHA-256;
  - fixed differential equation
    `tppaErrorAfter=tppaErrorBefore+R*physicalDelta`;
  - explicit degree units and the required coordinate convention on the
    calibration, matrix, and every witness;
  - full 2x2 signed response matrix, not independent scalar gains;
  - conservative matrix-element covariance;
  - fixed command uncertainty and directional reversal/deadband bounds;
  - applicable position, direction, temperature, load, and validity ranges;
  - capture UTC, boot identity, age at response, and relative lifetime;
- `remainingTravelBudgetDegrees`, with the exact nested shape:

  ```json
  {
    "signConvention": "adjusterIncreasing",
    "az": { "positiveDegrees": 0.0, "negativeDegrees": 0.0 },
    "alt": { "positiveDegrees": 0.0, "negativeDegrees": 0.0 },
    "cumulativeSessionDegrees": 0.0
  }
  ```

  All five budgets are finite non-negative degree magnitudes. Direction is
  represented only by the selected key. The directional cost is the absolute
  requested physical delta plus fixed command uncertainty and applicable
  deadband/pre-seat overhead. The cumulative cost is the sum of both axis
  costs. Exact `cost <= budget` admits. These values are advisory and do not
  reserve travel;
- `activeLeaseId`, `activeTransactionId`, and `lockedReason`. IDs are explicit
  JSON null or lowercase canonical non-nil UUIDs. Any mismatch, transaction,
  or non-null lock rejects; and
- `capabilities`, with exact booleans `planningEvidence`,
  `physicalMotionAvailable`, `atomicBudgetReservationAvailable`, and
  `motionAuthorityIncluded`. The first three must be true and the last false.

The evidence and plan contain no motion authority. A future transaction must
atomically revalidate and reserve before movement.

All numbers must be finite. NaN, infinity, negative zero, negative variance,
non-symmetric covariance, and non-positive-semidefinite covariance are denied.
Property sets are exact for every declared schema. Unknown, missing, duplicate,
stale, contradictory, or out-of-range evidence is a denial, never a request to
fall back to local or legacy motion.

## Admission Rules

Coarse planning is denied unless all of the following hold in one immutable
snapshot:

1. Schema version `2` is positively admitted before any nested parsing.
2. The echoed nonce and all request-binding/freshness checks pass.
3. Both axes have fresh signed physical positions and conservative covariance.
4. Required Hall/P20 sources agree within a preregistered threshold. Correlated
   sources are not counted as independent evidence, and disagreement rejects.
5. The response calibration covers current position, direction, temperature,
   load, and use time.
6. TPPA recomputes the matrix condition number against a compiled maximum.
   Matrix covariance is propagated with command magnitude. Fixed command
   uncertainty and directional deadband are added to residual and travel
   bounds; deadband never reduces required margin. Unknown engagement state
   applies the worst directional bound.
7. The full uncertainty-expanded path, including every intermediate point and
   the convex hull of every execution ordering permitted by the future client,
   remains strictly inside compiled hard and shrink-only operational limits.
8. Per-axis and cumulative remaining travel budgets cover the expanded path.
   A future commissioned transaction must atomically revalidate them.
9. No lock or active conflicting transaction exists.
10. Supervisor session, boot, and lease remain unchanged through plan emission.
11. The TPPA determination passes independent geometry, quality, repeatability,
    true-pole refraction, and freshness gates.

A total-error scalar cannot prove travel feasibility. Admission uses the
signed azimuth/altitude error vector, current signed axis positions, full 2x2
response model, and uncertainty-expanded path. Limit comparisons use integer
millidegrees with uncertainty rounded outward and strict endpoint containment.
Any condition not explicitly admitted is denied.

## Motion Separation

Evidence readiness and physical-motion authority are separate states.
Accepting evidence may produce a non-actuating plan only. The evidence parser
returns frozen inert values and has no dependency path to a motion client.
Lease, lock, and transaction identities are opaque and cannot be dereferenced
through the evidence API. CI enforces that structural separation.

A future motion client must additionally implement the commissioned transaction
protocol from `UPAS_SUPERVISOR_CLIENT_BOUNDARY.md`, including:

- durable idempotency and canonical request hashing;
- one-axis transactions executed sequentially in the planned order;
- lease ownership, atomic budget validation, and restart recovery;
- fresh pre- and post-move witnesses;
- terminal `VERIFIED` polling and immutable receipts;
- ambiguous-outcome poisoning with no blind retry or rollback; and
- independent fresh TPPA remeasurement after every correction step.

## Safety Limits

The software hard limit is the compiled TPPA constant +/-5.4 degrees because
reported physical stops are near +/-6 degrees. Payload values are
mismatch-rejecting cross-checks only and can never change the effective bound.

The guarded operational endpoint is derived from a separately compiled,
non-negative reserve and can only shrink the admissible region. Snapshot values
must exactly cross-check that policy. This document does not approve reducing
the current one-degree reserve or admitting the final 4-5 degree starting
stratum to physical motion. Changing reserve is a reviewed policy change, not a
schema or payload change.

## Qualification

Implementation requires fixtures shared between repositories: one canonical
valid response and one-field mutation cases for every rejection rule. Playback
tests prove that status V1, controller `MPos`, unknown schemas, nonce replay,
cross-boot data, stale or incoherent witnesses, correlated sources,
scalar-only gains, arbitrary equations, mismatched conventions,
ill-conditioned matrices, invalid covariance, intermediate path crossings,
uncertainty-rounded endpoint equality, and depleted budgets all fail closed
without contacting a motion endpoint.

Tests also prove that hard limits and reserve cannot be read from configuration,
that no status version other than exact integer `2` is admitted, and that the
evidence module cannot reach a motion transaction client.
