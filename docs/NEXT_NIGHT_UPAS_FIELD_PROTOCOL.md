# Next-Night UPAS Field Protocol

## Build
- Build the current committed branch and record its DLL SHA-256.
- Run `tools/validate_tppa_plugin_install.ps1` before opening a diagnostic sequence. The live plugin tree must contain exactly one TPPA assembly and its hash must match the tested build.
- Keep prior DLLs in `Documents\TPPA-PHD2-tests` or another directory outside NINA's live plugin tree. A rollback DLL below the live plugin directory can be discovered as another plugin assembly.
- Do not begin unattended. The first actuator trial requires an observer at the rig.

## Starting State
- Put both UPAS markers near their engraved zero positions and photograph them.
- Confirm GRBL reports Idle and record MPos X/Y. Treat MPos as commanded position, not encoder feedback.
- Focus successfully, plate solve reliably, and begin between 20 and 60 arcminutes total PA error.
- Disable pre-seat for the first trial. Keep configured azimuth travel guards enabled.
- Record reversal settings and do not change them during a run.

## Trial
1. Run `tools/run_guarded_tppa_verification.ps1` with the tested DLL hash and a VerificationOnly sequence whose target has at least 2 degrees of altitude margin inside the measured balcony opening.
2. Require the forward and repeated-forward determinations to agree within the selected tolerance. Treat the centered reciprocal comparison as a required internal-consistency gate, not an absolute-accuracy proof.
3. Enable automated correction. Permit exactly one bounded UPAS move.
4. Verify the plugin performs an independent fresh three-point measurement before planning another move.
5. Compare signed fresh azimuth/altitude changes with the command. Continue one move at a time only while the response is plausible.
6. Require two consecutive independent fresh results below the selected tolerance before accepting completion.
7. Optionally collect a passive PHD2 drift capture for research. Do not authorize UPAS movement from PHD2: the tested PDA captures were nonstationary and disagreed in direction across runs.

## Immediate Stop Criteria
- Any move is planned from a continuous-overlay value rather than the last fresh three-point result.
- Fresh total error worsens by more than 25 percent and more than 2 arcminutes after a move.
- Either axis changes by more than 15 arcminutes per commanded unit.
- The plugin requests another move before completing the fresh post-move measurement.
- X/Y travel approaches the configured guard, the controller direction becomes contradictory, or the UPAS bridge loses status synchronization.
- A fresh solve fails repeatedly, clouds invalidate solves, or physical marker travel becomes unsafe.
- The verification launcher reports a duplicate assembly, DLL hash mismatch, insufficient target margin, or settled pointing outside the balcony guard.

## Evidence to Preserve
- NINA log, TPPA fresh-result lines, every UPAS command/status frame, PHD2 debug log, controller MPos before/after, marker photographs, reversal settings, gear ratios, tolerance, refraction setting, and DLL SHA-256.
