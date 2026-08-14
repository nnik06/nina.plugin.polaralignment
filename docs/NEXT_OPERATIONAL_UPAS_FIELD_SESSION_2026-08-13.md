# Next Operational UPAS Field Session

Superseded for direct attended alignment by
`NEXT_OPERATIONAL_UPAS_FIELD_SESSION_2026-08-14.md`. Do not use this packet's
historical runtime, exposure, or move-count limits for TPPA 2.2.6.123 or later.

Status: field execution packet. The operational result is unproven until this
packet produces on-sky evidence.

This replaces the historical `FAST_POLAR_ALIGNMENT_FIELD_PACKET_2026-07-31.md`
for direct TPPA-to-UPAS alignment. That packet describes a supervisor/iPolar
shadow campaign and must not be used as the direct-route start checklist.

## Objective

Use TPPA fresh three-point determinations as the correction feedback source.

- `<=24'`: tripod-free coarse result from within the signed UPAS +/-5.4 degree
  working envelope. It is adequate for short exposures only.
- `<=3'`: imaging-critical result, confirmed by two independent fresh
  determinations inside 300 seconds. This is the only alignment result that
  may proceed to the 900-second imaging bracket.

Do not make an absolute sub-arcminute claim. The field objective is reliable
alignment for the configured optical train, not a metrology campaign.

## Before Dark

1. Build and hash-verify the intended plugin. Record repository HEAD and DLL
   SHA-256 in the session log.
2. Select Avalon UPAS and enable automated adjustments in its live NINA panel.
   `DoAutomatedAdjustments` is the persisted Avalon setting; verify the visible
   control, not merely an old configuration file.
3. Set `RefractionAdjustment=True`, `AutoPause=False`, an exposure no longer
   than three seconds, and point/sequence settle so the resolved direct-field
   value is five seconds. Do not use the historic 30-second metrology settle
   for this operational route.
4. Confirm the direct route and both physical travel guards are enabled. Enter
   the observed marker position and signed envelope for both axes: minimum
   `-5.4`, maximum `+5.4` degrees. Confirm a nonzero physical degrees-per-unit
   value for each axis. These are motion guards, not paperwork.
5. Leave the external-supervisor requirement disabled. No campaign ID,
   covariance authority, cadence authority, receipt, or physical-zero witness
   is required for the attended direct TPPA route.
6. Confirm mount, main camera, solver, COM30 bridge, and UPAS connection are
   healthy. Do not connect Weather, Safety Monitor, or the flat panel.

If the physical marker position is uncertain, do not move. Establish it by an
attended scale observation first; controller MPos after a reset is not a
physical-position source.

## Block 1: Response Calibration

This block creates the measured response the controller needs. It is not a
timed performance attempt.

1. Use one safe, qualified three-point arc and obtain two agreeing fresh
   baseline determinations.
2. Take the configured AZ pre-seat in its established direction, then an
   X-only attended command inside signed marker headroom. Take a new fresh
   determination. Preserve the command, pre/post marker positions, and both
   TPPA-axis changes per X unit.
3. Repeat the same measured procedure for Y/ALT. Record physical ALT
   degrees-per-unit, the signed TPPA Y response and cross-axis response, plus
   the observed direction/deadband behavior.
4. Populate the four calibrated 2x2 settings only from accepted samples:
   `AzimuthDeltaPerX`, `AzimuthDeltaPerY`, `AltitudeDeltaPerX`, and
   `AltitudeDeltaPerY`. Record conservative maximum X/Y command magnitudes.
5. A failed, ambiguous, inconsistent, or regressing probe ends this block. Do
   not issue an opposite command merely to make the data look symmetric.

## Block 2: Tripod-Free Coarse Alignment

1. Start a new run with tolerance `24'`. Two fresh baseline determinations
   must agree before a correction.
2. Permit the calibrated direct full-travel route to use the signed +/-5.4
   degree envelope. It may use no more than three bounded fresh-feedback moves
   inside the 300-second budget.
3. After each move, wait for motion completion and the direct-field settle,
   then obtain the mandatory fresh three-point feedback before another move.
4. Stop on bridge loss, bad solve agreement, uncertain physical position,
   travel denial, cancellation, or material regression. The shipped controller
   does not issue a blind inverse command.
5. A passing `<=24'` result is explicitly a handoff, not imaging acceptance.

## Block 3: Imaging-Critical Fine Alignment

1. Start a separate new run with tolerance `3'` from a new fresh baseline
   pair. Do not reuse the coarse run's result as confirmation.
2. Require two independent stationary fresh TPPA determinations at or below
   `3'` within 300 seconds. The continuous estimator cannot close the run.
3. Preserve all fresh result timestamps, commands, TPPA components, travel
   intervals, and the final pair.

## Block 4: Imaging Outcome

Run one of the following with the same mechanical state and optical train:

- unguided DEC drift at or below `0.79 arcsec/min`; or
- a real guided 900-second narrowband sub whose stars are acceptably round
  across the sensor.

This outcome, not a smaller printed TPPA number, is the acceptance evidence.

## Reliability Evidence

The operational claim requires five comparable field attempts from within the
working envelope, at least four successful `<=3'` completions, no safety-gate
violation, and no mount knowingly left worse after a failed attempt. Until all
five exist, report individual successful runs only, not reliability.

## Regression Handling

Current release behavior is intentionally simple: a material regression stops
the active run and preserves the state for attended recovery. It does not send
an inverse command. A future automatic retreat remains default-off until the
same session demonstrates bidirectional response and measured reversal
clearance, and two fresh determinations confirm the regression.
