# Next-Night UPAS Field Protocol

When the objective is to separate tripod flexure, arc geometry, and refraction,
use docs/FLEXURE_GEOMETRY_REFRACTION_CAMPAIGN.md. Its no-motion phases
supersede the actuator trial below; UPAS remains disabled for that campaign.

## Build
- Build the current committed branch and record its DLL SHA-256.
- Run `tools/validate_tppa_plugin_install.ps1` before opening a diagnostic sequence. The live plugin tree must contain exactly one TPPA assembly and its hash must match the tested build.
- Keep prior DLLs in `Documents\TPPA-PHD2-tests` or another directory outside NINA's live plugin tree. A rollback DLL below the live plugin directory can be discovered as another plugin assembly.
- Run `tools/test_next_session_readiness.ps1` under PowerShell 7 before darkness.
  For the Pi bridge on Mele, use the following shape after replacing the hash and
  confirming the exact commissioned USB ADB serial:

  ```powershell
  pwsh -NoProfile -File tools/test_next_session_readiness.ps1 `
    -PluginDirectory "$env:LOCALAPPDATA\NINA\Plugins\3.0.0\Three Point Polar Alignment" `
    -ExpectedRuntimeManifestSha256 '<tested-runtime-manifest-sha256>' `
    -RequireNinaClosed `
    -RequireIPolar `
    -RequireMainCamera `
    -RequireGuideCamera `
    -RequireFilterWheel `
    -RequireUpas `
    -ExpectedUpasComPort 'COM30' `
    -UpasPattern 'com0com' `
    -RequireUpasBridge `
    -UpasBridgeHost '192.168.137.50' `
    -UpasBridgePort 4001 `
    -UpasBridgeClientProcessName 'com2tcp' `
    -RequireAdb `
    -AdbTarget 'WCRFL19B06000037' `
    -AdbExecutable 'C:\Tools\platform-tools\adb.exe'
  ```

  Control evidence permits only the exact P20 USB ADB serial
  `WCRFL19B06000037`. Wi-Fi ADB, including `192.168.1.66:5555`, must not
  satisfy readiness or supply before/after motion evidence.
  `-UpasBridgeClientProcessName com2tcp` is a safety gate, not optional
  decoration: it requires Mele's existing `ESTABLISHED` transport session and
  prevents the readiness check from opening a second connection to the
  serial-over-TCP listener. The resulting pass verifies the TCP transport
  session only. It does not verify that GRBL is attached, powered, configured,
  or responsive.
  In the qualified Mele pairing, NINA opens `COM30` and `com2tcp` owns the
  paired `COM31`; the live `com2tcp` command line must confirm that relationship.
  Re-discover both ports after reboot and replace the example value if the
  pairing changed. Neither virtual COM number is physical-position evidence.
- For a deliberate direct-USB session, omit `-RequireUpasBridge`,
  `-UpasBridgeHost`, `-UpasBridgePort`, and
  `-UpasBridgeClientProcessName`; replace `COM30` with the physically verified
  direct-UPAS COM port observed after that reboot. Never let a stale COM number
  select the transport.
- Treat a missing imaging-train device, iPolar, P20, or UPAS transport as a
  hardware-preflight failure, not as a reason to weaken a later field gate.
- Do not begin unattended. The first actuator trial requires an observer at the rig.

## Runtime Installation
- With NINA closed, install the manifest-bound package using
  `tools/install_tppa_runtime_package.ps1`. Keep the package and archive roots
  outside the live plugin tree. The installer must validate the package first,
  archive the previous TPPA assemblies and stale deployment suffix files,
  validate the completed live installation, and report the archive path.
- Start NINA only after the live validator returns the expected source commit,
  plugin version, manifest hash, plugin hash, qualification-core hash, and
  assembly count two.
- After startup, require the first schema-v6 qualification run artifact to
  record absolute loaded paths, four-part assembly versions, informational
  versions, lowercase SHA-256 hashes, and MVIDs for both the plugin and
  qualification core. Require its pipeline digest to equal the loaded plugin
  SHA-256 and both loaded hashes to match the admitted runtime manifest. Stop
  the campaign on any mismatch; package admission alone does not prove what
  NINA loaded.

## Starting State
- Put both UPAS markers near their engraved zero positions and photograph them.
- Confirm GRBL reports Idle and record MPos X/Y. Treat MPos as commanded position, not encoder feedback.
- Focus successfully, plate solve reliably, and begin at no more than 24 arcminutes total PA error for the fixed-gain, two-move five-minute campaign. Easier starts remain eligible and may complete without movement. Starts above this window are retained as admission rejections and receive no UPAS movement. A wider window requires a separately qualified gain schedule.
- Disable pre-seat for the first trial. Keep configured azimuth travel guards enabled.
- Record reversal settings and do not change them during a run.
- After Home, a major slew, cable handling, or physical contact, hold the mount undisturbed for at least five minutes before the first qualified VerificationOnly determination. Restart the dwell after any new disturbance.
- When UPAS is connected directly to Mele, record the actual serial port after every reboot. A COM number is discovery metadata, never physical-position evidence.
- When UPAS is connected through the Pi, require the Pi address to resolve at the expected wired interface, raw GRBL TCP to accept a connection, and Mele `com2tcp` to reach `ESTABLISHED`. `SYN_SENT`, `CLOSE_WAIT`, an empty GRBL read, or a missing ARP neighbor fails the bridge gate.
- Select exactly one UPAS transport for a session. Do not let a stale direct-USB COM port satisfy a Pi-bridge preflight or vice versa.
- Confirm iPolar is enumerated and acquire its dark frame before a campaign; after restarting iPolar, explicitly select the preserved previous dark frame before collecting witness evidence.

## Production Direct Five-Minute Route

This is the operational path for a normal attended imaging night. It supersedes
the diagnostic measurement-qualification blocks below for the purpose of
authorizing direct TPPA correction. Those blocks remain useful for investigating
geometry or flexure, but they are not a paperwork admission gate.

1. Confirm the present signed AZ and ALT marker readings, the +/-5.4 degree
   hard bounds, and the selected travel scale. A power reset, manual movement,
   or uncertain command clears those readings.
2. Require a current, directional X and Y TPPA response calibration. The ALT/Y
   response must be measured independently; never substitute the X response.
3. Collect two fresh three-point TPPA determinations. They must agree closely
   enough to steer a correction and must fit the five-minute feasibility policy.
4. Permit at most two headroom-clamped corrective moves. Each needs explicit
   command completion, configured settle, and a new fresh three-point result
   before another command can be considered.
5. Finish only after two independent fresh determinations are at or below 3
   arcminutes total. If any response is implausible, inconsistent, or worse,
   stop automatic correction with the last fresh residual preserved.

The P20, iPolar, supervisor receipts, covariance campaign, alternate-arc
diagnostics, and PHD2 drift studies are supplementary evidence. None is a
precondition for this attended direct route. Travel limits, oversized-command
refusal, settle, cancellation, and fresh feedback remain mandatory.

## Measurement Qualification
1. After the required settling dwell, run three no-motion fresh TPPA determinations on safe arc A with fixed settings and refraction state.
2. Require the robust set gate to pass: component MAD at most 20 arcseconds and total-error MAD at most 10 arcseconds.
3. Move only the telescope to safe alternate arc B and collect three no-motion fresh determinations. Do not move UPAS.
4. Return to the exact arc-A geometry and collect three more fresh determinations.
5. Require both arc-A blocks to pass their within-block gates.
6. Require A1 versus A2 consistency: each signed component delta at most 30 arcseconds, total-error delta at most 20 arcseconds, and polar-error-vector phase delta at most 2.5 degrees.
7. Treat an arc-B disagreement as a geometry/refraction diagnostic. It must not authorize a correction unless a later calibrated model explains the bias.
8. Treat a failed A1-B-A2 return gate as an environmental or geometry-dependent systematic. Keep automatic UPAS correction disabled.

## Verification Runtime and Geometry Admission

1. Retire the Az 305 / Alt 30 Arc C sequence from qualification use. Its
   apparent endpoints fell to approximately 29.2 and 27.2 degrees and failed the
   30-degree refraction floor; it also could not complete inside either 600 or
   900 seconds under measured field solve latency.
2. Before NINA sequence load, compute and preserve `TPPA_RUNTIME_ADMISSION`.
   For the current nine-point run, require at least 120 seconds per point plus a
   180-second cancellation/cleanup reserve. A smaller cap is a pre-start denial,
   not permission to collect a partial run.
3. Treat any solve slower than `max(2 * rolling median, 90 seconds)` as a
   degraded-field event. Do not immediately restart a complete nine-point run.
   Requalify focus, clouds, field obstruction, and solver readiness first.
4. Plan every qualification arc from its complete predicted trajectory. Require
   minimum apparent altitude at least 35 degrees, prefer 40-50 degrees, keep the
   balcony maximum-altitude margin, and reject the plan before motion if any
   endpoint crosses the floor.
5. Compute planned geometry conditioning before motion and reject a plan whose
   expected design is ill-conditioned. Prefer fewer widely separated points to
   more points on a narrow or degenerate arc; preserve the predicted and realized
   conditioning metrics in the receipt.
6. Use an A-B-A revisit as the next field discriminator. If the return A differs
   from initial A, classify the effect as time-correlated until thermal,
   atmosphere, timestamp, and mechanical witnesses resolve it. If A closes while
   B differs, classify it as sky-position/geometry dependent.
7. On cancellation, observe sequence/plugin terminality before issuing the final
   tracking stop. Then require a sustained idle/tracking-off hold and an
   independent delayed mount-state check. A timer-only hold cannot replace
   terminality.
## Five-Position Shadow Model Check

1. After the ordinary VerificationOnly run passes repeatability and reciprocity,
   enable `5-position shadow model check` for one diagnostic run on the same
   safe arc. It adds reciprocal half-leg samples but leaves every legacy
   three-point result unchanged.
2. Preserve the `TPPA_OVERDETERMINED_5POSITION_SHADOW` JSON log record.
3. Require five distinct positions, qualified complete-fit geometry, residual
   RMS at most 30 arcseconds, maximum residual at most 60 arcseconds,
   legacy-to-five-point axis separation at most 0.5 arcminute, and maximum
   leave-one-out axis movement at most 0.5 arcminute.
4. A pass is model-fidelity evidence only. It never authorizes UPAS movement,
   completion, or an absolute sub-arcminute claim. A failure quarantines the
   arc and is the primary discriminator for field-dependent geometry bias.
5. This diagnostic adds two solves and two qualified point settles, nominally
   about one minute. Keep it outside the eventual five-minute production path
   until field evidence supports a shorter independently verified design.

## Telescope Trajectory Safety

- Preflight the complete route, not only its endpoint. A safe endpoint does not
  imply a safe balcony slew.
- Never perform a direct pier flip on the balcony. Return through mount Home,
  verify Home and idle, then begin a separately guarded slew to the new side.
- Treat NINA's `/v2/api/equipment/mount/slew/stop` as a best-effort brake, not a
  safety interlock. The 2026-08-01 campaign observed about 1.8 degrees of
  altitude travel after the first unsafe sample/stop decision.
- Keep at least 2 degrees of dynamic margin from every hard envelope boundary,
  use short prequalified waypoints for large same-pier moves, and require two
  continuous idle seconds before accepting final telemetry.
- Preserve the trajectory watchdog log even when the final endpoint is safe;
  any unsafe intermediate sample fails the route.

## iPolar Pier-Side Stability Block

1. Run only after astronomical darkness and after iPolar has loaded the
   preserved valid dark frame. The interactive recorder must see the console
   iPolar window; an SSH service session is not sufficient.
2. Start each leg with `tools/run_guarded_ipolar_slew_stability.ps1`. It must
   record at least five fresh frames with at least five stable neutral stellar
   centroids before it launches any slew.
3. The runner owns telescope motion only. It cannot contact or authorize UPAS.
   It rejects a pier-side change and polls the complete route against the
   balcony envelope before accepting two seconds of idle closure.
4. West-pier reciprocal cycle: approximately Az 0.6/Alt 35 to Az 1.2/Alt 45
   and back, with live RA/Dec recomputed by the guarded launcher.
5. Return through mount Home. Verify `AtHome=true`, tracking off, and idle.
   Re-establish tracking and separately preflight the east-pier start; never
   command a direct pier flip on the balcony.
6. East-pier reciprocal cycle: approximately Az 329.7/Alt 49.6 to
   Az 349.0/Alt 36.7 and back.
7. Reject the block without movement if the star gate fails. Preserve
   `samples.jsonl`, frame hashes, `baseline-gate.json`, `trajectory.jsonl`, and
   the generated `axis-evaluation.json` for every accepted or rejected leg.
8. After all four reciprocal legs, create a JSON manifest with one `CampaignId`
   and exactly four unique `Legs`: `west-outbound`, `west-return`,
   `east-outbound`, and `east-return`, each carrying its `ResultPath`. Run
   `tools/ipolar_pier_side_campaign_evaluator.ps1`. It rejects duplicate paths
   or run IDs, wrong pier sides, any leg outside the 15-arcsecond RMS /
   30-arcsecond maximum residual gates, or fitted-axis centers separated by
   more than 30 arcseconds. Passing qualifies only differential iPolar-axis
   stability; it never certifies absolute PA accuracy.
9. Use `tools/run_guarded_ipolar_pier_side_campaign.ps1 -Stage west` and
   `-Stage east` to execute each reciprocal pair. The launcher deliberately
   cannot transition between pier sides: after the west receipt, return through
   Home, verify Home/idle/tracking-off, then separately place and preflight the
   east start. Run `-Stage finalize` only after both immutable phase receipts
   exist. This keeps the dangerous pier transition outside the automated
   reciprocal runner while preserving one four-leg campaign identity.

## Separately Timed Solve Pre-Warm

1. Place the mount at the validated VerificationOnly target, confirm it is
   settled, inside the balcony envelope, and within one degree of the sequence
   target. The pre-warm path is deliberately no-slew.
2. Run `tools/run_guarded_tppa_verification.ps1` with `-PrewarmSolve` and the
   tested plugin hash. The launcher requires a connected idle camera, performs
   one supported snapshot plate solve, and rejects pier-side or pointing change.
3. Preserve the `TPPA_PREWARM` JSON log record. Its elapsed time is a separate
   preflight metric and must not be added to or hidden inside the under-five-
   minute VerificationOnly runtime claim.
4. A failed or slow pre-warm blocks the operational timing run. It is evidence
   of capture/solver readiness, not polar-alignment accuracy.
5. Report both numbers: pre-warm duration and sequence duration. Qualification
   requires five consecutive warm sequence runs below 300 seconds; it does not
   excuse failed reciprocity, repeatability, geometry, or absolute-truth gates.

## Actuator Trial
1. Only after measurement qualification passes, run `tools/run_guarded_tppa_verification.ps1` with the tested DLL hash and a VerificationOnly sequence whose target has at least 2 degrees of altitude margin inside the measured balcony opening.
2. Require the forward and repeated-forward determinations to agree within the selected tolerance. Treat the centered reciprocal comparison as a required internal-consistency gate, not an absolute-accuracy proof.
3. Enable automated correction and permit one or two bounded UPAS moves only while the measured cadence leaves time for a final independent confirmation.
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
- iPolar, UPAS, P20, or another required witness disappears after reboot. Re-run the read-only readiness check and repair enumeration before continuing.

## Evidence to Preserve
- NINA log, structured TPPA vector/phase lines, robust set-gate results, A1-B-A2 consistency result, every UPAS command/status frame, PHD2 debug log, controller MPos before/after, marker photographs, reversal settings, gear ratios, tolerance, refraction setting, exact arc coordinates, and DLL SHA-256.
