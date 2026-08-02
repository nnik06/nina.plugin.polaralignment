# UPAS ALT Axis Safety and Calibration

Source: physical-motion handoff from the UPAS camera-shroud project, 2026-07-24.

## Confirmed Motion Model

- Factory physical center is the engraved `0` ALT mark.
- Raw positive Y moves the physical ALT marker positive; raw negative Y moves it negative.
- Raw scale is approximately 1000 controller units per physical degree.
- TPPA keeps `YGearRatio=22`, so one logical Y unit sends 22 raw units and moves approximately 0.022 degrees. One physical degree is approximately 45.45 logical Y units.
- Feed 700 remains the tested operating value.
- Do not set TPPA gear ratio to 1000; that would confuse the raw physical scale with TPPA's command multiplier.

## Physical Envelope

- Routine operating limits are -5 to +5 physical degrees.
- Never seek the engraved +/-6 degree hard stops. A prior hard-stop contact produced rattle/strike behavior.
- The observed +5-to-0 reversal requiring 6000 raw units includes an unexplained 1000-unit residual. It is an upper bound on combined residual effects, not a clean backlash measurement and not an automatic-compensation value.

## Position Truth

- UPAS has no encoder.
- GRBL MPos is an open-loop command counter, not physical feedback.
- MPos may reset on reconnect or power-cycle without physical movement.
- Never infer physical position from MPos, issue a logical-zero return based on MPos, or use MPos to prove that the marker is at zero.
- Physical-marker observation is the only available absolute reference.

## TPPA Guard

TPPA 2.2.6.29 adds a fail-closed altitude envelope guard:

1. The observer visually reads the ALT marker and enters that physical start value.
2. The observer confirms the marker only after the current UPAS connection is established.
3. Successful Y commands expand a conservative interval in the commanded physical direction.
4. Failed or timed-out Y commands also expand the interval because partial movement may have occurred.
5. Reversals widen the interval rather than pretending prior travel was recovered.
6. A command is refused when the predicted interval would cross the configured -5 to +5 degree envelope.
7. Reconnects, manual UPAS moves, reversal changes, gear-ratio changes, scale changes, and envelope edits clear confirmation.

## Supervised Sign Check

After a power or control-path change, and only while safely away from either endpoint:

1. Confirm the physical marker position and disable automatic correction.
2. Command raw Y+100, equivalent to about +4.545 logical Y units at gear ratio 22.
3. Verify approximately +0.1 degree physical marker motion.
4. Stop immediately if motion is opposite, unclear, or noisy.
5. Re-enter the observed start and reconfirm the guard before automation.
## Cross-axis coordinate contract

- Supervisor requests use the physical frame `azEastPositive_altUpPositive`.
- The verified ALT hardware fact is raw `Y+` moving the factory marker in its
  positive scale direction. `AvalonReverseAltitude` maps the logical command to
  that physical direction and changing it invalidates the marker confirmation.
- No universal raw-X-to-east/west invariant is claimed. Cabling, bridge, or
  controller changes require a fresh witnessed X probe. Runtime response
  learning may then determine the sign, but it cannot replace the P20/factory-
  scale witness required by the external supervisor.
- A remembered response is valid only for the recorded reversal setting and
  control-path epoch. It must be discarded after power, transport, or polarity
  changes.
