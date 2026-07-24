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

## Measurement Qualification
1. Run three no-motion fresh TPPA determinations on safe arc A with fixed settings and refraction state.
2. Require the robust set gate to pass: component MAD at most 20 arcseconds and total-error MAD at most 10 arcseconds.
3. Move only the telescope to safe alternate arc B and collect three no-motion fresh determinations. Do not move UPAS.
4. Return to the exact arc-A geometry and collect three more fresh determinations.
5. Require both arc-A blocks to pass their within-block gates.
6. Require A1 versus A2 consistency: each signed component delta at most 30 arcseconds, total-error delta at most 20 arcseconds, and polar-error-vector phase delta at most 2.5 degrees.
7. Treat an arc-B disagreement as a geometry/refraction diagnostic. It must not authorize a correction unless a later calibrated model explains the bias.
8. Treat a failed A1-B-A2 return gate as an environmental or geometry-dependent systematic. Keep automatic UPAS correction disabled.

## Actuator Trial
1. Only after measurement qualification passes, run `tools/run_guarded_tppa_verification.ps1` with the tested DLL hash and a VerificationOnly sequence whose target has at least 2 degrees of altitude margin inside the measured balcony opening.
2. Require the forward and repeated-forward determinations to agree within the selected tolerance. Treat the centered reciprocal comparison as a required internal-consistency gate, not an absolute-accuracy proof.
3. Enable automated correction and permit exactly one bounded UPAS move.
4. Verify the plugin performs an independent fresh three-point measurement before planning another move.
5. Compare signed fresh azimuth/altitude changes with the command. Continue one move at a time only while the response is plausible.
6. Require two consecutive independent fresh results below the selected tolerance before accepting completion.
7. Optionally collect a passive PHD2 drift capture for research. Do not authorize UPAS movement from PHD2: the tested PDA captures were nonstationary and disagreed in direction across runs.

## Immediate Stop Criteria
- Either within-block repeatability or the same-arc A1-B-A2 return gate fails.
- Any move is planned from a continuous-overlay value rather than the last fresh three-point result.
- Fresh total error worsens by more than 25 percent and more than 2 arcminutes after a move.
- Either axis changes by more than 15 arcminutes per commanded unit.
- The plugin requests another move before completing the fresh post-move measurement.
- X/Y travel approaches the configured guard, the controller direction becomes contradictory, or the UPAS bridge loses status synchronization.
- A fresh solve fails repeatedly, clouds invalidate solves, or physical marker travel becomes unsafe.
- The verification launcher reports a duplicate assembly, DLL hash mismatch, insufficient target margin, or settled pointing outside the balcony guard.

## Evidence to Preserve
- NINA log, structured TPPA vector/phase lines, robust set-gate results, A1-B-A2 consistency result, every UPAS command/status frame, PHD2 debug log, controller MPos before/after, marker photographs, reversal settings, gear ratios, tolerance, refraction setting, exact arc coordinates, and DLL SHA-256.
