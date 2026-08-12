# Direct Full-Travel Qualification Protocol

## Purpose

Establish whether the measured UPAS 2x2 response matrix and the existing
0.65-gain, bounded command controller predict real full-scale response well
enough to consider a future expansion of the direct full-travel route. This is
not an alignment run and it does not authorize a gain, command-limit, or travel
guard change.

The current release fails closed: a direct route is admitted only when its
simulated damped controller reaches the explicitly selected operational target
within the three-move, 300-second contract. The imaging-ready target is 3
arcminutes; the tripod-free bulk target is 24 arcminutes. A 24-arcminute result
is deliberately labelled coarse and is not an imaging-ready completion.

## Move-Count Promotion Rule

For the direct-field 40-second fresh-determination reservation and 15-second
move reservation, a run with `N` moves and a separate terminal confirmation
requires `40(N + 1) + 15N` seconds: 150 seconds for two moves and 205 seconds
for three. Three moves fit the 300-second contract with 95 seconds remaining.
They are required by the nominal 0.65-gain model to bring a 300 arcminute
(5 degree) residual below the 24 arcminute bulk target; two leave 36.75
arcminutes. The absolute ceiling remains three moves, including any Y bootstrap
probe; do not stack the bootstrap exception into a fourth move.

## Operational Contract After Qualification

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

- The existing visual-marker, bridge, COM30, NINA, and fresh-determination
  safety gates pass.
- Start from a documented signed physical-marker position with comfortable
  margin to the +/-5.4 degree configured envelope.
- Refraction state, camera settings, solver, and telescope field remain fixed
  for each response pair.
- Capture a fresh TPPA determination before and after every individual command.
- Stop immediately on a failed solve, inconsistent fresh determination, marker
  ambiguity, unexpected sign, non-reducing error, or bridge-health failure.

## Bounded Response Set

Perform one command at a time, always wait for the configured settle interval,
and retain the pre-command, post-command, and physical-marker evidence.

1. X positive at 25%, 50%, and 100% of the existing maximum command.
2. X negative at 25%, 50%, and 100% after the documented backlash approach.
3. Repeat the equivalent six commands for Y.
4. Perform one bounded diagonal command at the existing maximum limits only if
   the single-axis records stayed monotonic and inside their predicted marker
   envelope.

Each record must include the 2x2 prediction, commanded X/Y, visual-marker
position before and after, fresh measured azimuth/altitude delta, residual
vector, elapsed time, and any reversal/backlash state.

## Acceptance Evidence

Do not expand authority from a single average. Require all of the following:

- Correct sign and monotonic response in both directions for both axes.
- Measured response at 100% remains within a predeclared tolerance of the
  low-amplitude calibration, including the two cross-axis terms.
- The diagonal result agrees with the sum of the two measured columns within
  that same tolerance.
- Predicted and fresh measured residual vectors agree across the set without a
  systematic sign reversal, growth, or unmodelled deadband.
- Every physical marker remains inside the configured envelope; controller
  MPos is evidence only, never a physical-position substitute after reset.
- Capture the predicted-versus-measured residual after every move. A
  projection is diagnostic only and cannot authorize the next command or a
  terminal result.

## Decision

- If any acceptance item fails, preserve the logs and tighten the route or
  calibration range; do not raise gain, command maximum, or travel authority.
- If all items pass, review the signed evidence and separately propose a
  measured full-travel profile. The proposal must be tested offline against the
  same 2x2, gain, clamp, move-count, and terminal-confirmation math before any
  attended field trial.
- A <=3 arcminute completion remains two independent fresh determinations, not
  the continuous estimator and not the controller simulation.
