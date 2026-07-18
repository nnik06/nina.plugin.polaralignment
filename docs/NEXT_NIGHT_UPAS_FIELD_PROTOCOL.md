# Next-Night UPAS Field Protocol

## Build
- Use the 2.2.6.6 fresh-feedback build from this repository only after hash verification.
- Keep the prior deployed DLL available for rollback.
- Do not begin unattended. The first actuator trial requires an observer at the rig.

## Starting State
- Put both UPAS markers near their engraved zero positions and photograph them.
- Confirm GRBL reports Idle and record MPos X/Y. Treat MPos as commanded position, not encoder feedback.
- Focus successfully, plate solve reliably, and begin between 20 and 60 arcminutes total PA error.
- Disable pre-seat for the first trial. Keep configured azimuth travel guards enabled.
- Record reversal settings and do not change them during a run.

## Trial
1. Run one fresh TPPA three-point measurement with automated correction disabled.
2. Repeat once without moving anything. Continue only if the two fresh totals agree within 1 arcminute and neither axis differs by more than 1 arcminute.
3. Enable automated correction. Permit exactly one bounded UPAS move.
4. Verify the plugin performs an independent fresh three-point measurement before planning another move.
5. Compare signed fresh azimuth/altitude changes with the command. Continue one move at a time only while the response is plausible.
6. Require two consecutive independent fresh results below the selected tolerance before accepting completion.
7. Finish with an 8-12 minute passive PHD2 drift capture.

## Immediate Stop Criteria
- Any move is planned from a continuous-overlay value rather than the last fresh three-point result.
- Fresh total error worsens by more than 25 percent and more than 2 arcminutes after a move.
- Either axis changes by more than 15 arcminutes per commanded unit.
- The plugin requests another move before completing the fresh post-move measurement.
- X/Y travel approaches the configured guard, the controller direction becomes contradictory, or the UPAS bridge loses status synchronization.
- A fresh solve fails repeatedly, clouds invalidate solves, or physical marker travel becomes unsafe.

## Evidence to Preserve
- NINA log, TPPA fresh-result lines, every UPAS command/status frame, PHD2 debug log, controller MPos before/after, marker photographs, reversal settings, gear ratios, tolerance, refraction setting, and DLL SHA-256.
