# iPolar / UPAS Field Handoff - 2026-07-31

## Outcome

- iPolar 2.92 was brought to visually coincident red circle/crosshair in Zoom
  mode using a final positive-X approach.
- Two fresh no-motion iPolar captures at least five seconds apart remained
  visually coincident.
- The immediately following fresh NINA TPPA result was:
  - Azimuth: +1 deg 05 min 40 sec
  - Altitude: -1 deg 38 min 56 sec
  - Total: 1 deg 58 min 45 sec
- TPPA used refraction adjustment, targeted the true celestial pole, and used
  the standard-atmosphere fallback. Continuous solves remained near
  1 deg 58 min 40 sec.
- iPolar convergence therefore did not establish polar alignment. No further
  UPAS actuation is justified until iPolar's RA-axis center is recalibrated and
  cross-validated.

## Motion Record

The relevant command chronology was:

1. An accidental Y+250 caused by wrapper composition.
2. Correct Y-200 recovery.
3. X-60.
4. X+20: no visible response.
5. X+60: approximately 3 pixels.
6. X+300: large overshoot.
7. X-280.
8. X-60.
9. X+110: final positive-X approach and visual iPolar convergence.

The X+20 and X+60 observations occurred after reversal and mostly measured
backlash/deadband take-up. Treating that response as loaded gain caused the
X+300 overshoot. This was a calibration-selection failure, not a GRBL runaway.

The factory AZ and ALT scales remained near their engraved zeros and far from
the operational +/-5 degree limits. The P20 scale images are suitable for
physical-limit and gross-motion witnessing, not sub-degree PA metrology.

## Operational Findings

- Never calculate loaded gain from a move that crossed or took up backlash.
- Preserve the final approach direction and use same-direction corrections.
- Ordinary and Zoom iPolar coordinates have different scales. A mode change
  invalidates the active servo loop; do not transform or mix coordinates during
  that loop.
- Two stable frames do not prove liveness. Require increasing capture
  timestamps, different frame hashes, a healthy star count, and a successful
  solve state.
- Unknown iPolar center provenance is invalid provenance. Tripod movement alone
  should not geometrically invalidate a rigid camera-to-RA calibration, but the
  observed two-degree contradiction requires recalibration before reuse.
- P20 frames must remain mandatory before and after every individual command.
  They establish physical headroom and detect gross unexpected movement.
- The UPAS command wrapper must be a single validated gateway. A wrapper must
  never execute another movement wrapper as an import side effect.

## Connectivity Workarounds

Mele SSH and SCP were intermittently responsive and intermittently hung.
Use IPv4 and bounded keepalive options:

```powershell
ssh -4 -o ConnectTimeout=5 -o ServerAliveInterval=2 `
  -o ServerAliveCountMax=1 nnik0@10.147.17.165 <command>
```

Retry one bounded operation rather than leaving an unbounded session open.

Do not use the bare host token `meleq4c` unless the current Windows account has
an explicitly verified SSH config entry. In the Codex sandbox it can inherit the
wrong local username and fail authentication. Use the explicit target
`nnik0@10.147.17.165` and verify it first with a bounded `hostname` command.

Do not place PowerShell pipelines, semicolons, or query-string ampersands inside
an inline nested SSH command. Quoting can terminate at the wrong shell and run
the remaining pipeline locally. For nontrivial remote PowerShell, encode the
script with UTF-16LE and invoke `powershell -NoProfile -EncodedCommand <base64>`;
run the resulting `ssh` command as a separate command so the approved SSH
identity remains available. The 2026-08-01 recovery verified this sequence:

1. `ssh ... nnik0@10.147.17.165 hostname`
2. generate the encoded PowerShell payload locally;
3. `ssh ... nnik0@10.147.17.165 powershell -NoProfile -EncodedCommand <base64>`

On 2026-08-01, an inline `| Out-Null` again escaped the intended remote
PowerShell parse and was interpreted by the remote command shell, producing
`'Out-Null' is not recognized`. This confirms that even apparently simple
directory creation, inventory, and hash commands must use the encoded-command
path when they contain a pipeline.

Also on 2026-08-01, an encoded recursive inventory of the whole
`TPPA-PHD2-tests` tree produced no output and hit the 30-second client timeout.
A fail-fast `ping`, batch-mode SSH `echo` sentinel, and narrowly scoped encoded
query all completed in about one second, proving the transport was healthy and
the recursive query was the fault. Do not diagnose a silent broad inventory as
an SSH outage. Enumerate a known campaign root without recursion first, select
specific run directories, and only then inspect or hash their bounded file
sets.

PowerShell 5 on Mele also rejected inline `try { ... } catch { ... }`
expressions used as hashtable values. Populate explicit variables first, then
construct the status object.

The NINA Advanced API was directly reachable at:

```text
http://10.147.17.165:1888/v2/api
```

P20 USB capture used `C:\Tools\capture_p20_usb.ps1`. Captures took roughly
16-20 seconds because the ADB daemon was restarted repeatedly. Keep one
persistent ADB server alive for the full alignment block and verify the device
serial before the first capture.

Native iPolar capture used:

```text
C:\Tools\CaptureIPolar.exe
Scheduled task: CodexCaptureIPolarNative
Result: C:\Tools\ipolar-native-result.txt
Image: C:\Tools\ipolar-native-latest.png
```

After restarting iPolar, explicitly select the previous valid dark frame.

## Council Verdict

Claude Opus 5 High and Gemini 3.1 Pro High independently concluded that TPPA is
currently more credible than the visually centered iPolar display. Both rank
invalid/stale iPolar RA-center calibration first. Claude additionally identified
the possibility of a live-looking but stale overlay and recommended treating
iPolar only as a fast relative sensor. Gemini recommended re-running the iPolar
RA-center calibration as the shortest discriminator.

The two-degree gap cannot plausibly be explained by seeing, refraction, or the
standard-atmosphere fallback. TPPA's stable total error is internally coherent.
Its Az/Alt decomposition remains configuration-sensitive, but the total error
does not depend on longitude or clock in the same way.

## Reconciliation Campaign

Perform this campaign without UPAS actuation:

1. Restart iPolar, load the valid dark, and prove capture liveness with at least
   three fresh frames: increasing timestamps, distinct hashes, solve success,
   and star count above the session baseline floor.
2. Re-run the manufacturer's RA-center calibration by imaging at three
   well-separated RA positions while leaving tripod AZ/ALT untouched.
3. Fit and preserve the observed star-field rotation center. Record the old and
   new center displacement in ordinary-image pixels.
4. Return the mount to Home. Run a fresh iPolar solve and two no-motion TPPA
   determinations with refraction state, atmosphere source, site coordinates,
   UTC timestamps, and raw solves preserved.
5. Decision:
   - If recalibrated iPolar reports approximately the TPPA vector, the old
     iPolar center was invalid.
   - If iPolar remains near zero while two TPPA runs agree near two degrees,
     quarantine iPolar absolute PA and test its solve/overlay and mechanical
     registration.
   - If TPPA totals disagree materially between fresh runs or reciprocal arcs,
     quarantine TPPA actuation and investigate geometry/configuration.
6. Apply one known, bounded physical adjustment only after the instruments have
   a traceable relative registration. Both instruments must report the same
   signed delta even if their absolute zeros still differ.

## Refined 3-5 Minute Method

The 3-5 minute goal applies only to a qualified fast path. Recalibration and
independent TPPA qualification are recovery/verification work outside that
budget.

### Fast-path prerequisites

- iPolar RA-center calibration has current, recorded provenance.
- iPolar mode and dark-frame identity match the calibration record.
- Persistent SSH and ADB are already warm.
- P20 and native iPolar capture each pass a fresh-frame check.
- A recent TPPA determination supplies the absolute PA vector.
- UPAS calibration matches axis, direction, iPolar mode, attachment epoch, and
  temperature envelope.
- Physical scale reading leaves at least 0.5 degree reserve inside the +/-5
  degree operational limit after accounting for P20 reading uncertainty.

### Deterministic loop

1. Capture synchronized iPolar and P20 baselines.
2. Prove liveness using three incrementing iPolar frames.
3. Convert the recent TPPA signed vector into a relative iPolar target using a
   provenance-qualified registration matrix. Do not use iPolar's cached
   absolute zero as the truth target.
4. If required, preload once into the chosen final direction. Mark that move as
   backlash take-up and exclude it from gain fitting.
5. Send one bounded probe in the loaded direction. Require correct response sign
   and a magnitude within the calibrated confidence envelope. Abort otherwise.
6. Send a coarse command no larger than 60% of the predicted correction. Also
   cap it using the upper gain bound and remaining physical margin. Never use a
   universal raw-unit cap until field calibration establishes one per axis.
7. After explicit GRBL Idle, wait at least two seconds, capture fresh P20 and
   iPolar frames, and update loaded gain only from this same-direction response.
8. Send at most one correction and one fine trim. Abort on reversal, mode
   change, growing error, gain change above 40%, missing fresh evidence, GRBL
   Alarm/Hold, or physical-margin failure.
9. Require two live final iPolar frames separated by at least five seconds,
   residual at most two ordinary-equivalent pixels, and frame-to-frame change
   at most one ordinary-equivalent pixel.
10. Run two fresh no-motion TPPA determinations as separate qualification.
    iPolar convergence is rejected if TPPA remains above 15 arcminutes. Do not
    advertise sub-arcminute success until independent absolute validation exists.

### Timing budget

- Warm-state and liveness check: 30 seconds.
- Baseline and signed target: 30 seconds.
- Preload/probe: 40 seconds.
- Coarse move and evidence: 50 seconds.
- Correction and evidence: 50 seconds.
- Fine trim and two-frame stability: 40 seconds.
- Artifact finalization: 20 seconds.

Total fast loop target: approximately 3 minutes 40 seconds. TPPA anchoring and
qualification remain outside the fast-loop claim.

## Supervisor Requirements

- One typed command gateway with axis, signed units, calibration ID, approach
  direction, predicted response, physical margin, and evidence IDs.
- Separate states for backlash take-up and loaded motion.
- Separate calibration matrices for ordinary/Zoom and each loaded direction.
- A mode transition is an abort, not a live calibration switch.
- Explicit states: PREFLIGHT, LIVENESS, ANCHOR, PRELOAD, PROBE, COARSE,
  CORRECT, TRIM, VERIFY, DIAGNOSE, ABORT.
- Replay this session as a regression fixture and assert that the historical
  X+300 command is rejected.
- Persist append-only JSONL events, content hashes and UTC timestamps for every
  P20/iPolar frame, raw TPPA solves, calibration records, software versions,
  site/refraction metadata, and the exact command ledger.

## Next Session Priority

Do not resume iPolar-driven alignment first. Recalibrate the iPolar RA center,
prove overlay liveness, then perform the no-actuation iPolar-versus-TPPA
comparison. Only after signed deltas agree should the fast relative-control loop
be field-qualified.
