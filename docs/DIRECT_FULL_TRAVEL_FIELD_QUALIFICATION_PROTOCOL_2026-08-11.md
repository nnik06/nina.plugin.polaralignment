# Direct Full-Travel Qualification Protocol

## Purpose

Establish whether the measured UPAS 2x2 response matrix and the existing
0.65-gain, bounded command controller predict real full-scale response well
enough to consider a future expansion of the direct full-travel route. This is
not an alignment run and it does not authorize a gain, command-limit, or travel
guard change.

The current release (`76c2569`) fails closed: a direct route is admitted only
when its simulated damped controller reaches the 3 arcminute terminal target
within the two-move contract. The configured full-envelope corner is therefore
expected to be rejected until field evidence supports a different qualified
route.

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

## Decision

- If any acceptance item fails, preserve the logs and tighten the route or
  calibration range; do not raise gain, command maximum, or travel authority.
- If all items pass, review the signed evidence and separately propose a
  measured full-travel profile. The proposal must be tested offline against the
  same 2x2, gain, clamp, move-count, and terminal-confirmation math before any
  attended field trial.
- A <=3 arcminute completion remains two independent fresh determinations, not
  the continuous estimator and not the controller simulation.
