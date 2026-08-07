# TPPA And UPAS Supervisor Integration Status

## Decision

TPPA must not implement a second source of truth for UPAS physical position,
travel headroom, optical scale interpretation, or actuator response.  The
canonical owner is `C:\Dev\upas-polar-align` and its authenticated
`upas-supervisord` API.

The direct TPPA route remains intentionally separate.  It is an attended,
human-guarded route with the existing 120 arcminute initial-error admission.
It must not claim the full physical travel envelope from controller coordinates
or sky error alone.

## Existing Integration Surface

The TPPA plugin already has production-oriented clients and parsers for:

- `GET /v1/coarse-planning-evidence`, including fresh P20-derived axis evidence
  and calibration applicability.
- `POST /v1/coarse/physical-zero`, which returns a witnessed physical-zero
  transaction rather than relying on GRBL `MPos` after a reset.
- `POST /v1/coarse/tppa-correction`, which carries two independent TPPA fresh
  determinations and receives a completed supervisor transaction.
- TPPA observation lease/close/abort calls for a coarse campaign.

The supervisor owns the corresponding information required for a full-field
route: signed axis position and uncertainty, calibrated 2x2 response matrix,
matrix covariance, backlash/deadband, directional travel reservation, endpoint
verification, non-moved-axis stability, and a durable receipt.

## Commissioning Gap

The code exists but is not field-commissioned for physical coarse motion.  In
particular, the supervisor must demonstrate all of the following with the
actual P20, UPAS, load profile, and bridge:

1. Fresh, signed AZ and ALT optical positions that are valid for control, with
   uncertainty safely inside the +/-5.4 degree software envelope.
2. A field-qualified 2x2 TPPA-error-to-physical-motion response calibration,
   including a bounded condition number, hold-out residuals, per-direction
   deadband/backlash, and the current hardware/load/temperature applicability.
3. Same-connection GRBL acknowledgement and idle evidence, followed by a
   settled post-move P20 witness that proves the commanded axis moved and the
   other axis remained stable.
4. A physical-zero transaction after any reset, power cycle, or uncertain
   motion.  `MPos` is never a physical position witness.
5. A live HTTPS/auth/lease deployment on Mele.  Dry-run status or a valid API
   schema alone is not motion authority.

## Cross-System Contract Required Before Opt-In

The full-field route must bind each TPPA correction to one immutable physical
epoch.  The supervisor-issued evidence needs a position sequence/epoch and
physical-zero transaction identity.  Both fresh determinations, the mount
pointing/side-of-pier context, and the submitted correction must name that
same epoch.  A physical-zero return, hand adjustment, P20 witness change,
mount slew/flip, or another reservation invalidates the epoch and requires a
new pair.

The supervisor is authoritative for endpoint and cumulative travel limits.  A
TPPA setting may request the full-field capability but cannot grant it.  The
lease/evidence returned by the supervisor must explicitly authorize the
calibration, load profile, temperature range, position range, and mount
geometry for the requested correction.

Every terminal outcome needs a bounded and consumable result: completed with
the observed physical delta, denied before movement, or uncertain/partial
movement.  The latter two immediately end the TPPA automatic run, release its
resources, invalidate the correction epoch as appropriate, and require a
fresh determination before any retry.  No network wait may silently consume
the five-minute budget.

The calibrated response is local to its commissioned domain.  The supervisor
must either qualify the response and held-out residual over all positions,
temperatures, sides of pier, and directions it admits, or force a staged
correction within the validated local radius with a new fresh TPPA pair after
each step.  The controller must have an explicit iteration limit and require
monotonic residual improvement; divergence de-escalates to attended handling.

Until these conditions are met, `RequireExternalUpasSupervisorForAutomatedMoves`
must remain opt-in and TPPA must fail closed when that mode is selected.

## Field Sequence For Full-Envelope Commissioning

1. Establish and record an optical physical-zero baseline on both axes.
2. At several signed positions inside the envelope, execute individually
   bounded X and Y perturbations.  Capture fresh TPPA pairs before and after
   each perturbation.
3. Fit and hold out the 2x2 response model in the supervisor repository; do
   not hard-code a scalar conversion in TPPA.
4. Demonstrate post-move optical displacement, cross-axis stability, and
   repeatability at the intended speed/feed.
5. Commission the HTTPS identity, credential, lease, and evidence paths on
   Mele without granting TPPA any serial or raw bridge ownership.
6. Exercise denial, timeout, serial-fault, partial-move, reset, manual-touch,
   and meridian-flip invalidation paths, confirming each yields a prompt
   terminal receipt and a released TPPA run.
7. Enable the supervisor route for a small, attended on-sky correction first;
   preserve the direct route as the fallback when the supervisor is unavailable.

Only after this sequence can TPPA safely raise automated initial-error
admission toward the physical +/-5.4 degree envelope.  The full-envelope
decision is made from signed physical headroom plus the response model, never
from TPPA sky error or a nominal travel limit alone.
