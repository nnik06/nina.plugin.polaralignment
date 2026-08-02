# TPPA Operational Goal Reframe

## Decision

The project objective is operational polar alignment, not absolute
sub-arcminute metrology:

> Starting from an eligible, manually coarse-aligned state, use TPPA and UPAS
> to reach a fresh-confirmed true-pole total error no greater than 3 arcminutes
> within 300 seconds on at least 80 percent of eligible attempts, without
> unsafe motion or degrading an already acceptable alignment. Then demonstrate
> that both supported OAG trains deliver acceptable guided exposures.

The five-minute clock covers TPPA acquisition, UPAS correction, and the final
fresh TPPA confirmation. A 10-12 minute PHD2 Guiding Assistant run is an
acceptance-campaign witness and cannot be included in the production clock.

Eligible production starts must be bounded. Until field evidence expands the
range, use 20-60 arcminutes total starting error, qualified plate solves,
true-pole refraction mode, valid site coordinates, safe telescope geometry,
and verified UPAS physical headroom. Larger errors require coarse manual or
separately qualified coarse correction before starting the five-minute run.

## Evidence adjudication

- The recovered historical PHD2 Guiding Assistant result is reported as
  2.4 arcminutes after 681 seconds, not degrees. It resolves the remembered
  degrees-level claim for that historical epoch, subject to preserving the
  source log and analysis receipt.
- That PHD2 run predates the later rig relocation and cannot arbitrate the
  2026-08-01 TPPA/iPolar disagreement.
- On 2026-08-01, TPPA totals of 101.026, 101.920, and 102.483 arcminutes were
  internally clustered, but same-direction repeatability (2.418 arcminutes)
  and condition number (171.631) failed. The field report permits only a
  gross-misalignment claim, not a correction vector.
- The visually centered iPolar display is not a quantitative truth reference,
  especially without a fresh RA-center calibration after relocation.
- Therefore neither TPPA nor iPolar wins that epoch. One bounded, same-session
  arbitration campaign remains necessary. Multiplying condition number by a
  repeatability statistic is not proof that conditioning created the 101
  arcminute result.
- Reciprocity remains an internal diagnostic. A pass cannot override failed
  same-direction repeatability or geometry.

## Keep, pause, stop

### Keep

- UPAS physical travel envelope, P20/supervisor observations, one-command
  sequencing, explicit idle/settle checks, and fail-closed motion authority.
- Confirmed-response gain, reversal debounce, direction-response memory, and
  independent fresh post-move measurement.
- True-pole refraction mode and recorded site/atmosphere inputs.
- Three-point geometry, closure, trend, repeatability, and solve-quality gates.
- A bounded five-minute execution budget with a non-spendable final
  confirmation reserve.
- Per-train delivered-image qualification. A 900-second bracket is primarily
  a narrowband test; broadband duration must follow measured saturation.

### Pause

- New absolute-metrology infrastructure.
- New automated iPolar GUI control or PHD2 PDA actuation.
- The pending 900-second readiness gate after preserving its patch. Finish it
  only after the controller path meets its field milestones; it must name the
  exact filter and require a same-session saturation/background scout.

### Stop

- Treating sub-arcminute absolute truth as the production objective.
- Averaging TPPA, iPolar, and PHD2 into a synthetic truth value.
- Treating a continuous correction estimate or reciprocity pass as completion.
- Blindly reversing UPAS to a remembered best state after regression. UPAS is
  open-loop and backlash makes such a revert a new unverified movement.
- Adding claim/evidence machinery that does not improve convergence, safety,
  or delivered-image validation.

## Milestones

### M1 - same-epoch arbitration and cadence characterization

No UPAS movement.

1. Recalibrate the iPolar RA center with the tripod and iPolar attachment
   untouched, then preserve its raw/processed evidence.
2. On one mechanically unchanged epoch, collect at least three qualified
   same-direction TPPA determinations and the required reciprocal diagnostic.
   Require qualified three-point geometry, no trend, and same-direction vector
   spread no greater than 1.5 arcminutes.
3. Run one geometrically qualified 10-12 minute PHD2 Guiding Assistant drift
   witness after the TPPA block. This is external validation, not part of the
   five-minute production time.
4. During the same night, characterize post-slew settling at 3, 5, 8, 10, 15,
   20, and 30 seconds over at least 20 transitions split across directions.
   Compare each early plate-solved center with the 30-second reference.

The arbitration passes only if TPPA is internally qualified and agrees with
the eligible PHD2 witness within 3 arcminutes. iPolar is retained as a
corroborating witness until its repeatability and convention are quantified.

### M2 - shorter settle policy

First qualify a fixed shorter settle; adaptive early-exit is a later
optimization.

- Candidate settle must produce determination vectors within 0.5 arcminute of
  the 30-second reference on every accepted transition.
- No accepted early sample may still be moving relative to the 30-second
  reference.
- Forward and reverse transitions are qualified separately.
- Minimum 20 transitions, zero unsafe or false-stable exits.
- A session ratchet falls back to 30 seconds after repeated instability.

Target: median fresh three-point determination no greater than 40 seconds.
The earlier 25-second suggestion is aspirational until capture and solve
latency are measured.

### M3 - bounded iterative correction

- Default and migrated unset alignment tolerance: 3.0 arcminutes. Retain the
  independent preflight rejection for zero/unset tolerance.
- Permit another correction only when its move, fresh response measurement,
  and the non-spendable final confirmation reserve fit inside 300 seconds.
- Keep gain 0.65 initially and cap correction count from measured timing.
- Stop without further movement on regression, insufficient improvement,
  reversal excess, geometry failure, travel-envelope failure, or budget
  exhaustion. Do not auto-revert.
- Completion requires a fresh stationary three-point determination not used
  to compute the final movement and two-consecutive agreement.

Field acceptance requires at least 8 successes in 10 eligible attempts over at
least three nights, with zero hard-limit violations, zero false-success cases,
and zero unexplained state changes. Qualify EdgeHD first, then repeat for GT81.

## Delivered-imaging acceptance

After M3:

- run a same-session filter and saturation/background scout;
- use the exact filter named by the policy;
- capture one qualified guided bracket for each train;
- require uninterrupted PHD2 state and acceptable center/corner star-shape
  change under the preregistered train policy;
- preserve PHD2, NINA, FITS, WCS, timing, and state evidence.

Passing delivered imaging proves operational sufficiency under the recorded
conditions. It does not prove absolute polar-axis accuracy.

## Numeric contract

| Measure | Requirement |
| --- | --- |
| Production TPPA time | no more than 300 seconds |
| Fresh-confirmed TPPA total | no more than 3.0 arcminutes |
| Eligible-attempt success rate | at least 8 of 10 |
| Same-direction TPPA spread before actuation | no more than 1.5 arcminutes |
| External TPPA/PHD2 agreement during acceptance | no more than 3 arcminutes |
| Unsafe UPAS/hard-limit events | zero |
| False success with external result above 5 arcminutes | zero |
| Automatic regression reversal | prohibited |
| Final confirmation reuse | prohibited |

These thresholds are operational preregistration values. They may be tightened
only after field evidence; they must not be loosened silently to manufacture a
pass.
