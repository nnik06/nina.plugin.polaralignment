# Direct Full-Travel Qualification Protocol

## Purpose

Validate the deployed direct TPPA-to-UPAS route under real sky feedback. The
controller uses a measured 2x2 actuator response, damped corrections, bounded
per-move commands, fresh post-move determinations, and a finite 12-move ceiling.

The imaging-ready target is 3 arcminutes total, with an internal aim near 1.5
arcminutes. The tripod-free acquisition target is 24 arcminutes total from an
admitted starting error no larger than 300 arcminutes on either axis. A
24-arcminute result is deliberately labelled coarse and is not an imaging-ready
completion.

## Runtime And Move Limit

The 300-second value is a measured performance target, not an authorization
deadline. It does not revoke a safe, freshly measured correction that is still
converging. Physical travel, response/regression checks, settling,
cancellation, and the 12-move ceiling remain authoritative. Report elapsed time
and move count for every attempt; do not prematurely stop a valid run merely
because a conservative cadence estimate crosses 300 seconds.

## Operational Contract

Keep one measured 2x2 controller, with two separately named admission and exit
policies. Never let a broad-travel result claim fine convergence.

- **Fine closure:** may claim `FINE_CONFIRMED` only after two independent fresh
  determinations are within 3 arcminutes. It is a five-minute operation.
- **Bulk acquisition:** may claim `COARSE_AT_24` only after a fresh determination
  is at or below 24 arcminutes. It must be labelled `not imaging ready` and
  may not emit a fine-alignment claim or operational imaging qualification.
- **Bulk-to-fine handoff:** requires a new fresh determination and a new fine
  admission. It is not an inherited authorization from the bulk phase.
- **Unqualified:** returns the exact failed predicate and stops. It must never
  present a projected residual as a measured result.

The current 0.65 controller leaves only a narrow mathematical margin at the
24-arcminute handoff. Until response repeatability and backlash have been
measured, use an internal aim below 15 arcminutes rather than treating 24
arcminutes as a comfortable fine-route entry.

## Preconditions

- The rig is stationary after transport and fresh signed AZ/ALT positions are
  entered in the travel guard with comfortable margin to the +/-5.4 degree
  envelope.
- COM30 has one qualified owner and the persistent `com2tcp` bridge is healthy.
- Refraction state, camera settings, solver, and telescope field remain fixed
  for each response pair.
- Capture a fresh TPPA determination before and after every individual command.
- Stop immediately on a failed solve, inconsistent fresh determination, marker
  ambiguity, unexpected sign, non-reducing error, or bridge-health failure.

No sealed campaign, covariance authority, physical-zero receipt, or optical
witness is required for the attended direct route. P20 images may be retained
as useful evidence, but are not a motion-admission prerequisite.

## Calibration Block

The latest isolated full probes produced a well-conditioned matrix whose
dominant terms match the persisted controller seed, but only one accepted
sample per axis exists. Before enabling clamp-limited recovery:

1. Start from a normal sub-degree error with the mechanism loaded by the normal
   AZ pre-seat.
2. Obtain an independent fresh baseline.
3. Execute one isolated bounded X command and obtain fresh three-point
   feedback.
4. Execute one isolated bounded Y command and obtain fresh three-point
   feedback.
5. Repeat until at least three accepted samples exist for each axis. Preserve
   rejected probes as deadband evidence; do not average them into the matrix.
6. Run `tools/summarize_tppa_upas_response.ps1`. Require direction consistency,
   matrix condition number no greater than 5, and maximum relative vector
   deviation no greater than 10 percent before enabling clamp-limited recovery.

Each accepted record includes the 2x2 prediction, command, fresh measured
azimuth/altitude delta, residual vector, elapsed time, and reversal/backlash
state.

## Operational Run

1. Acquire the initial fresh three-point determination and admit the selected
   3- or 24-arcminute tier.
2. Require a fresh correction vector, available travel, a finite bounded plan,
   bridge ownership, and cancellation clearance before each command.
3. Execute one bounded plan, wait for controller Idle and settling, then obtain
   independent fresh three-point feedback.
4. Stop motion on material regression, inconsistent response, failed solving,
   travel denial, or cancellation. Never authorize the next command from the
   continuous correction estimator.
5. Feed accepted response evidence into the controller and repeat while it is
   converging, up to 12 moves.
6. At tolerance, keep UPAS stationary and obtain one more independent fresh
   three-point determination. Declare completion only when both fresh vectors
   agree and are within tolerance.
7. Report wall-clock duration, move count, initial/final vectors, rejected
   probes, response matrix, and whether the 300-second target was met.

## Acceptance Evidence

Do not qualify response uncertainty or operational success from a single
average. Require all of the following:

- Correct sign and stable response on both axes.
- At least three accepted isolated samples per axis qualify the response
  matrix under the calibration limits above.
- Predicted and fresh measured residual vectors agree across the set without a
  systematic sign reversal, growth, or unmodelled deadband.
- Every physical marker remains inside the configured envelope; controller
  MPos is evidence only, never a physical-position substitute after reset.
- Capture the predicted-versus-measured residual after every move. A
  projection is diagnostic only and cannot authorize the next command or a
  terminal result.

## Decision

- If any calibration item fails, preserve the logs and keep clamp-limited
  recovery disabled; do not raise gain, command maximum, or travel authority.
- If calibration passes, enable the measured full-travel profile and run the
  attended operational trial against the same 2x2, gain, clamp, move-count,
  response, and terminal-confirmation rules.
- A <=3 arcminute completion remains two independent fresh determinations, not
  the continuous estimator and not the controller simulation.
- Imaging-ready acceptance additionally requires one real guided 900-second
  sub with round stars across the usable sensor, or a qualified raw Dec-drift
  rate no greater than 0.79 arcseconds/minute when a science frame is
  unavailable.
- The production claim requires at least four successful attempts in five
  eligible starts, with no travel violation and no false completion.
