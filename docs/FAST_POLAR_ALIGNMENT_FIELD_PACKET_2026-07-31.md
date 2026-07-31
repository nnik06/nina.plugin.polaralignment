# Fast Polar Alignment Field Packet

Date: 2026-07-31
Status: unit-tested shadow qualification only; field claims remain `UNPROVEN`

## Frozen checkpoints

- TPPA root: `C:\Dev\upas-nina-tppa-plugin`
- TPPA implementation HEAD: `03a4a2347f31888370ad2ed4577d1de03d3cdebe`
- TPPA policy SHA256:
  `0ef7966f50b1c5f2fdc3f7109172105524ac3a9f064a994aab8b9d3ea96c3926`
- TPPA receipt implementation SHA256:
  `7d1f54e44c7dab2989456711d32b1832895e151584180c5840bd4f2ecffe9af1`
- Supervisor root: `C:\Dev\upas-polar-align`
- Reviewed iPolar policy commit:
  `2b7195d7b6498843f58d9e04838ba637e030c22a`
- iPolar policy SHA256:
  `3d0d80383fb8fdd938039cd16d3240e7f83d42186fb09bb7f4ac13a3d54a06ae`

The TPPA suite passed 396/396 after its checkpoint. The iPolar policy passed
20/20 after sole-motion-owner hardening. The supervisor remainder passed
850 tests with 2 skipped after five exact unrelated P20 source/canary tests
were deselected. Those P20 failures belong to concurrent modified Android
commissioning sources and remain visible; this packet does not waive them.

## Authority boundary

- TPPA measures and reports. It never commands UPAS.
- `upas-motion-supervisor` is the only accepted motion owner.
- Every proposed iPolar command binds a campaign ID, source polar-error-vector
  SHA256, calibration SHA256, hardware epoch, camera stream, monotonic clock
  domain, and unique pre-frame content hashes.
- The current policies are not wired to production execution. They grant no
  movement authority and must not be deployed as though they do.
- Mele deployment waits for a reviewed shadow adapter and a clean artifact
  receipt. No hardware restart or field actuation is justified by this packet.

## Preflight

1. Fix tripod, UPAS, mount, imaging train, iPolar attachment, and cables.
2. Create one hardware-epoch identifier. Any mechanical or cable change ends it.
3. Re-run the manufacturer iPolar RA-axis centre calibration after tripod
   relocation or uncertain provenance.
4. Restart iPolar, explicitly select the preserved valid dark, and prove at
   least three incrementing live solved frames with unique content hashes.
5. Qualify exact latitude, longitude, elevation, UTC clock, coordinate frame,
   pressure, temperature, humidity, refraction state, and true-pole target.
6. Establish P20 evidence custody and readable signed AZ/ALT scales.
7. Verify UPAS bridge identity, exclusive lease, controller idle, travel
   envelope, and the supervisor as sole motion owner.
8. Keep TPPA automatic correction and iPolar actuation disabled.

Any failed item stops the affected campaign.

## Campaign A: TPPA five-minute shadow qualification

Use one safe, geometrically qualified arc. Do not move tripod or UPAS.

1. Start the timer immediately before the first fresh capture.
2. Acquire at least three fresh uncached three-point determinations.
3. Preserve exact solve mid-times and all geometry/closure diagnostics.
4. Compute repeatability with spherical polar-error-vector separation.
5. Stop at 300 seconds.
6. Evaluate the frozen TPPA policy.

The policy evaluates a self-contained, content-hashed evidence envelope. It
does not perform the astrometric measurement itself; the shadow adapter remains
responsible for deriving every quantity and preserving the underlying solves.

A passing repeatability result requires all of:

- maximum pairwise separation at most 0.5 arcminute;
- final reported error at most 1 arcminute;
- qualified arc span, geometry, and closure;
- fresh qualified station pressure, temperature, and humidity;
- qualified site, elevation, clock, epoch, coordinate frame, and true-pole mode;
- no physical adjustment between determinations.

This establishes only an internally repeatable result. Absolute accuracy also
requires a current instrument-bound independent true-pole witness, captured in
the same mechanical state through a qualified disjoint input path. The witness
must have error at most 0.5 arcminute, differ from TPPA by at most 0.5
arcminute, and keep their explicit sum at most 1 arcminute.

## Campaign B: arc and time discrimination

With no actuation, run:

`A -> B -> A -> B -> A`

Use qualified safe arcs and preserve elapsed time. Interpret conservatively:

- A repeats while B differs: sky-position geometry/refraction bias.
- A and B both walk with time: mechanical/thermal or clock/state drift.
- Neither repeats: no correction is actionable.

This campaign is not part of the five-minute claim. It determines whether a
passing same-arc result can generalize.

## Campaign C: iPolar response calibration

Run only after preflight and only under the supervisor. Begin in shadow mode.

For each axis/sign/direction:

1. Capture synchronized native iPolar and full-resolution P20 pre-evidence.
2. Use same-direction commands solely to engage backlash. Mark them
   `backlashTakeup`; never learn gain from them.
3. Collect at least three loaded-response samples with visible signed response.
4. Fit minimum/maximum raw units per pixel and a maximum qualified command.
5. Validate using a withheld same-direction command.
6. Reject, never clip, any request above the lesser of the calibrated maximum
   and 200 raw units.

Every command plan must be from one campaign and one source-vector digest, name
`upas-motion-supervisor` as motion owner, use fresh unique frame content from
one monotonic clock domain, and remain inside the P20-observed physical margin.

A response is accepted only when:

- the executed plan digest exactly matches the qualified plan;
- command completion follows planning in the same clock domain;
- post frames are strictly later, same-stream, unique, and content-disjoint
  from pre frames;
- signed response is in the intended direction;
- target crossing does not exceed 1 pixel;
- improvement exceeds 0.3 pixel and both observed spreads;
- response ratio is from 0.3 to 1.5.

## Campaign D: iPolar five-minute trial

Promotion to a timed trial requires loaded-response calibration and a passing
withheld command for every required axis/sign/direction.

Start the timer after calibration is already valid. Require three live solved
frames per decision, at least five stars, at most one-pixel planning spread,
one engaged direction per axis, at most six commands per axis, and one command
at a time. Completion requires:

- median absolute iPolar offset at most 1 pixel;
- spread at most 0.3 pixel;
- stable native green-circle state;
- P20 scales inside physical limits;
- no failed or unexplained response;
- elapsed time at most 300 seconds.

The result remains iPolar-relative until independently reconciled with a
qualified true-pole witness.

## Stop conditions

Stop without another command on stale/reused frames, missing content hashes,
clock-domain mismatch, changed centre/dark/profile/hardware epoch, ambiguous P20
scale, bridge reconnect, lost exclusive lease, direction reversal, response
outside bounds, target overshoot, exhausted command budget, or any competing
motion owner.

## Shadow integration queue

1. Add a read-only TPPA adapter that constructs and logs the qualification
   input/result without changing existing completion or actuator behavior.
2. Add a read-only iPolar adapter that emits proposed plans and validation
   results but cannot call the motion executor.
3. Define one canonical campaign/source-vector receipt exchanged between the
   adapters and supervisor.
4. Replay historical good, divergent, overshoot, stale-frame, and Pi-reconnect
   sessions through both adapters.
5. Council-review the adapters and receipts, then deploy shadow telemetry to
   Mele with hashes and rollback.
6. Promote motion only after three separate successful field trials and explicit
   review of the complete evidence packets.
