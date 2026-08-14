# Next Operational UPAS Field Session

Status: photon-backed field execution remains required. This packet supersedes
the 2026-08-13 packet for direct attended TPPA-to-UPAS alignment.

## Runtime

- Source commit: `fe2127c67cd13d1dd4be94389e30b358a3466515`
- Plugin version: `2.2.6.124`
- Plugin SHA-256:
  `11869303405F7B945D48661EB5F244335D6A4D29F4A0FD6125361C9D55053BAB`
- Qualification core SHA-256:
  `EE7834FA7105A71E0AA4942AF8D669136282F15B5849D4D8D9B1F0D0C1A6A7FA`
- Runtime manifest SHA-256:
  `52D89E2C3071839A055A7F3EF5962D9ECD31B81F577EF185BC1931756223DA0B`

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
5. Set alignment tolerance to `3'`. The historical five-minute option may
   remain enabled to select the operational route, but elapsed time is
   telemetry only.
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
