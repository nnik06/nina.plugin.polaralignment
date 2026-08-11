# Direct UPAS Y-Axis Field Validation

## Purpose

Measure the missing ALT/Y actuator response with photons before enabling a
wide two-axis TPPA correction route. This is an operational calibration block,
not an absolute-metrology campaign.

The current direct X/AZ route is qualified independently. It must not be used
as evidence for Y/ALT. A wide two-axis correction remains unavailable until
this block produces a directional, repeatable Y response.

## Preconditions

1. Run the deployed build from commit `2075dbe213e8c007f99dcd334b78706ab5def42f`
   and verify DLL SHA-256
   `B560D96B8D3F54B8D726281CA40F68555DC050A1FD9C00B840EDA3EF13702BDD`.
2. Use the attended direct TPPA route. The P20 is useful calibration evidence,
   but is not a paperwork precondition for this attended block.
3. Confirm Refraction Adjustment is enabled, the mount/camera/solver are
   healthy, and the selected TPPA arc is inside the balcony envelope with
   adequate altitude margin.
4. Establish a physically safe UPAS starting position. Never infer a physical
   position from post-reset MPos.
5. Keep the existing +/-5.4 degree travel envelope, command completion,
   settle, cancellation, and fresh-solve guards enabled.
6. Start a wall-clock timer immediately before the first fresh TPPA solve.

## Block A: Isolated Y Response

1. Acquire two fresh three-point TPPA determinations without moving UPAS. If
   they disagree enough to make the signed ALT residual uncertain, stop.
2. Make one attended, bounded Y-only probe. Choose the smallest safe command
   expected to create a measurable 15-30 arcminute TPPA change; do not assume
   that controller units are degrees or reuse an X calibration for Y.
3. Wait for explicit controller completion and the configured settle interval.
   Acquire a fresh three-point TPPA result, never a continuous-overlay value.
4. Record the signed probe command and the measured `deltaAzimuth` and
   `deltaAltitude`. A valid Y sample needs a measurable, plausible signed ALT
   response. Material AZ cross-coupling is evidence to retain, not a reason to
   pretend the response is diagonal.
5. Derive at most one damped, headroom-clamped Y-only correction from that
   observed response. Do not command it if the sign is unclear, the response
   is below solve noise, the required travel is unsafe, or the response is
   grossly nonlinear.
6. After that correction, acquire a fresh result and then one independent
   stationary fresh confirmation. Accept the block only if both total errors
   are <=3 arcminutes, or record the measured response and exact rejection
   reason.

## Time and Stop Rules

- The block is designed to fit the 300-second operational budget only when
  the measured fresh-solve cadence leaves enough time for the post-move result
  and stationary confirmation. Otherwise it is calibration-only and must stop
  before an unconfirmable correction.
- Stop immediately after an inconsistent fresh determination, failed solve,
  unknown/contradictory sign, travel denial, cancellation, bridge loss, or a
  material worsening of the fresh total error.
- Do not attempt a second Y probe in the same run merely to force a result.

## Block B: X Cold-Start Route

Run this only after Block A is complete and the mount remains stable. Start
with a deliberately measured X/AZ residual near 300 arcminutes while ALT/Y is
within the normal 120-arcminute envelope. Use two initial fresh determinations,
the direct X route, fresh feedback, and an independent stationary confirmation.

This validates the new wide X route and its real timing. It does not prove
two-axis closure and must not be reported as such.

## Evidence To Preserve

For every run retain the exact fresh-result timestamps, signed TPPA components,
commanded X/Y units, controller completion/settle events, physical-position
evidence available at the time, solver diagnostics, DLL hash, elapsed time,
and the accept/reject decision. These records are the input for enabling a
measured 2x2 response model and for tracking the required four-of-five
operational reliability demonstration.

After the run, use the read-only helper below to produce a signed response
summary. It is diagnostic only; it cannot enable motion or alter settings.

```powershell
pwsh -NoProfile -File tools/summarize_tppa_upas_response.ps1 `
  -LogPath 'C:\Users\nnik0\AppData\Local\NINA\Logs\<nightly-log>.log' `
  -MinimumSamplesPerAxis 1
```
# Calibrated-X Bootstrap Behavior

When the direct full-travel route has a qualified X response column but no
qualified Y column, the controller does not silently fall back to an
X-only correction.  It first issues one normal, bounded Y probe.  The X
column is retained in memory only as an identification seed; the Y column
must still come from the fresh post-probe TPPA determination.  The existing
rank and condition checks must accept the resulting 2x2 response model before
any model-based correction is proposed.

The bootstrap neither persists a Y response across sessions nor relaxes the
normal per-move limits.  If the probe cannot be measured, or the matrix is
singular/ill-conditioned, the route remains unqualified and no two-axis
direct correction is authorized.
# Wide-Envelope Bootstrap Admission

The full +/-5.4-degree input envelope may be admitted for a direct field run
only when the configured X column is plausible and both visual-marker axes
have valid configured positions with two-sided ALT headroom for the 20-unit
bootstrap probe. This entry path authorizes only that observable, bounded Y
probe. A single fresh Y observation must still clear its response and matrix
conditioning gates before any model-based correction, and it does not by
itself authorize a full-travel correction.

## Session-Local Coarse Promotion

After that one bounded Y probe, the controller may use the configured direct
per-move limits only in the current session.  Promotion requires all of the
following: a response of at least 6 arcminutes from the 20-unit probe, a Y
response direction separated from the trusted X column (sine at least 0.15),
and a response-matrix condition number no greater than 8.  The normal
physical headroom and travel guards remain in force.

The first promoted coarse correction is an experiment, not proof.  Its next
independent fresh TPPA determination must reduce total error by at least 20
percent.  Otherwise the controller latches off every automated UPAS movement
path for its lifetime, persists `DoAutomatedAdjustments = false`, retains the
travel accounting, and records the before/after TPPA evidence.  It never
issues an inverse or a bounded follow-up move: backlash and unobserved lost
motion make controller-coordinate rollback unsafe.  Attended recovery must
re-establish the physical datum and re-qualify the response model before a new
automation run.  This remains field-unvalidated until a fresh X/Y photon
campaign confirms the measured response matrix.
