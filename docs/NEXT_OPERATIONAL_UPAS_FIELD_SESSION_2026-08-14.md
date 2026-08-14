# Next Operational UPAS Field Session

Status: photon-backed field execution remains required. This packet supersedes
the 2026-08-13 packet for direct attended TPPA-to-UPAS alignment.

## Runtime

- Source commit: `d631e30e40eecb0a67c6edb27baec708c2c70705`
- Plugin version: `2.2.6.126`
- Plugin SHA-256:
  `3B9C4FA59415AD62BD22BC60245D9C570BC53F828DB33D03B29067F7B6F2B301`
- Qualification core SHA-256:
  `1B2001DB0D72953DF1A6CE762C5CADD7E80610361FFEBBBCD4D98A663A29B91E`
- Runtime manifest SHA-256:
  `F58C072D601787956FEC85B238AA2FCB7A52FD425EFD89FC299238680A48AE3A`
- Automated test suites: `1115/1115` Debug and `1115/1115` Release.

## Objective

Use TPPA fresh three-point determinations to steer UPAS to a total reported
polar error at or below 3 arcminutes, confirmed by two consecutive stationary
fresh determinations. Runtime is recorded for later optimization but cannot
reject an otherwise qualified result.

The run is operational imaging preparation, not an absolute sub-arcminute
metrology claim.

## Minimal Preflight

1. Verify NINA loaded the version and hashes above.
2. Connect the mount, main camera, and Avalon UPAS. Keep Weather, Safety
   Monitor, and the removed flat panel disconnected. Do not alter Switch
   outputs.
3. In the live TPPA panel select Avalon UPAS and enable automated adjustments.
   Leave the external supervisor/campaign mode disabled.
4. Set `Adjust for refraction=True`, `AutoPause=False`, exposure `5 s`, and a
   verification settle between `5 s` and `120 s` that is sufficient for the
   mount to be stationary.
5. Set alignment tolerance to `3'` and leave `Enforce five-minute runtime
   budget` off while establishing the reliable protocol. Version 2.2.6.126
   keeps the same initial-error admission, response validation, calibration,
   terminal confirmation, and bounded move ceiling with this option off;
   elapsed time remains telemetry only.
6. Confirm the physical UPAS marker readings, signed movement directions, and
   headroom inside the configured `-5.4..+5.4 deg` envelope. MPos after reset
   is not physical-position evidence.
7. Verify COM30 is reclaimable by NINA and the persistent com2tcp bridge has
   exactly one established controller connection.

P20, iPolar, supervisor receipts, covariance/cadence authorities, alternate
arcs, and independent drift are optional diagnostics. They are not admission
requirements for this attended direct route.

## Alignment Run

1. Start from a fresh TPPA determination. If the two-axis response model is not
   available, allow the controller's bounded X and Y identification probes.
   Once both fresh responses are independently measurable and well-conditioned,
   2.2.6.126 promotes that session-local model directly into damped two-axis
   coarse correction. Each component is capped at 80 logical units and remains
   constrained by the signed physical travel envelope.
2. After every UPAS command, require command completion, settle, and a fresh
   three-point response before any subsequent command.
3. Reuse an accepted, current-epoch post-move determination directly for the
   next modeled correction. Do not add another pre-move sweep or legacy
   continuous-frame solve; completion still receives its independent fresh
   confirmation.
4. Once response identification is complete, allow the measured model to
   correct either direction. Backlash/reversal handling remains active.
5. Continue while fresh evidence supports improvement. Permit no more than
   twelve measured moves in the correction phase and never exceed the signed
   physical travel envelope.
6. Finish only after two consecutive stationary fresh determinations are both
   at or below `3'` and satisfy the existing repeatability threshold.

Do not stop or restart merely because five, six, or seven minutes elapsed.

## Real Stop Conditions

Stop without another UPAS move on:

- uncertain physical position or insufficient travel headroom;
- bridge/controller loss or cancellation;
- inconsistent fresh pre-move determinations;
- implausible, unmeasurable, or materially regressing actuator response;
- solve/geometry failure that makes the TPPA vector unusable;
- the finite twelve-move correction ceiling.

These conditions protect the equipment or prevent blind correction. Runtime,
missing receipts, and absent optional witnesses do not.

## Evidence To Preserve

- initial fresh TPPA vector;
- every UPAS command and its post-move fresh vector;
- learned 2x2 response model and physical travel intervals;
- the final two stationary fresh vectors;
- total elapsed time, solve durations, and any actual stop reason.

After a successful alignment, preserve one real guided 900-second science
exposure and the corresponding PHD2 log when sky and target conditions allow.
Round stars across the sensor are the practical outcome check. The heavier
qualification tooling may analyze these files offline; it must not consume the
night before the exposure is acquired.

## Reliability Campaign

Collect five comparable field attempts from inside the working envelope. The
provisional reliability target is at least four successful `<=3'` completions,
each with the required terminal pair and no physical safety violation. Optimize
runtime only after this protocol converges reliably.
