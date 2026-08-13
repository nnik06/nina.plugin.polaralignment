# First-Run Fast UPAS Protocol

## Purpose

This route obtains a local two-axis sky-response model during the same TPPA
session. It does not use a static gain table to size its first correction.

## Admission

Before starting, the operator must set and confirm all of the following:

1. `AvalonDirectFullTravelRouteEnabled` and
   `AvalonDirectFullTravelRouteConfirmed` are true.
2. Both signed travel guards are enabled and confirmed.
3. The visually confirmed AZ and ALT marker positions are entered as the
   starting positions, with a valid `-5.4..+5.4` degree physical envelope.
4. Physical degrees-per-unit bounds are finite and conservative.
5. `AvalonPreSeatAzimuthBeforeMeasurement` is true and
   `AvalonAzimuthPreSeatUnits` is at least 24.
6. All four static response terms are zero. A qualified existing response model
   uses the calibrated route instead.

The signed guards account for every pre-seat, identification, successful, and
conservatively charged failed move. A controller coordinate is never treated
as a physical position after reset.

## Motion Sequence

1. Pre-seat AZ by at least 24 units in the configured direction.
2. Take a fresh three-point TPPA determination.
3. Consume that initial, geometry-qualified determination once as the baseline
   for an X move of 20 units in the same seated direction. Settle and take a
   fresh determination. The resulting X-only response must pass the response
   gates; it cannot authorize a correction.
4. Consume the accepted X-response determination once as the baseline for a Y
   move of 20 units. Settle and take a fresh determination. The resulting
   Y-only response must pass the response and conditioning gates.
5. Only after both independent response columns qualify may the controller
   calculate a damped correction. An accepted post-move fresh response may
   authorize only the immediately next bounded correction when its total
   improvement and component cross-axis regression gates passed. That authority is
   consumable and is revoked by a pause, timeout, reconfiguration, a different
   motion epoch, a new intervening determination, expiry, or any state that
   cannot be positively validated. Every proposed move remains subject to the
   signed physical envelope, regression, response, settle, cancellation, and
   runtime guards.
6. Complete with independent fresh stationary verification. A failed response,
   invalid model, unsafe envelope, worsening move, or missing verification
   revokes motion authority for the run.

## Runtime Contract

The direct-field reservation is 40 seconds for a fresh three-point
determination and 15 seconds for a bounded UPAS action. The nominal first-run
path can reserve six fresh determinations and four actions: initial, X
feedback, Y feedback, two correction feedbacks, and stationary confirmation.
That is exactly 300 seconds. The fourth action is available only after the
fresh X/Y identification completion and a positively validated, one-shot
dynamic authority token. If any observed cadence or action exceeds its
reservation, the runtime planner declines the next action rather than borrowing
time. A pause, slow solve, failed response gate, or unsafe command is not
waived to meet five minutes; it stops the route without declaring alignment.

## Field Evidence To Record

- Marker readings and the entered signed start positions.
- Pre-seat direction and command; X and Y identification commands.
- Each fresh TPPA result, response sample, model condition, and guard decision.
- The final two stationary TPPA determinations and the elapsed runtime.

The first-run route is a field calibration path. It is not an authority to
claim sub-arcminute absolute accuracy; it is intended to reach the operational
imaging target only when the final independent TPPA verification agrees.
