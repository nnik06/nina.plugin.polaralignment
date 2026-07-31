# Fast Polar Alignment Qualification Protocol

Date: 2026-07-31
Status: unit-tested admission policy; field qualification pending
TPPA repository checkpoint: `1111bd704a718a76c1cdc6fa70f695539dc6996f`
UPAS supervisor checkpoint: `5e335800aa245adc919738a47b149e6ea1f0bb87`

## Claims

The project has two separate target claims:

1. TPPA determines true-pole error within 1 arcminute in at most 300 seconds.
2. iPolar plus UPAS reaches a stable green-circle state in at most 300 seconds.

Neither claim is currently established. Passing unit tests proves that the
software rejects known-invalid evidence. It does not prove field accuracy.

## Ownership

- `C:\Dev\upas-nina-tppa-plugin` owns TPPA measurement, NINA integration,
  true-pole metrology, and qualification reporting.
- `C:\Dev\upas-polar-align` owns UPAS motion planning, P20 scale evidence,
  physical travel safety, iPolar response calibration, and command receipts.
- TPPA never infers physical UPAS position from controller coordinates.
- TPPA is measurement and reporting only. It never owns or emits an UPAS
  movement command.
- The supervisor is the sole motion owner. A campaign identifier and source
  polar-error vector must be bound into every accepted movement plan and
  command receipt so TPPA and iPolar cannot independently correct the same
  observation.
- Any competing motion owner, missing arbitration proof, or stale campaign aborts.
- The supervisor never certifies TPPA absolute accuracy.

## Block 0: fixed configuration

Before either five-minute trial:

1. Fix the tripod, mount, imaging train, iPolar attachment, UPAS, and cables.
2. Record a hardware-configuration identifier and hashes for the iPolar capture
   profile and RA-axis centre calibration.
3. Re-run the manufacturer iPolar RA-axis centre calibration after the tripod
   has moved or whenever centre provenance is unknown.
4. Restart iPolar, select the preserved valid dark frame, and prove live capture
   with at least three consecutive incrementing frames.
5. Record exact site latitude, longitude, elevation, UTC clock state, pressure,
   temperature, humidity, refraction setting, coordinate frame, and pole target.
6. Use true-pole mode for every absolute-accuracy claim.
7. Keep automatic UPAS actuation disabled during baseline and witness capture.
8. Declare one monotonic clock domain for causal evidence. Wall-clock timestamps
   are reporting metadata and never establish pre/post ordering.

Any attachment, mode, dark, centre calibration, focus, camera profile, tripod,
or cable change starts a new hardware epoch.

## Block 1: iPolar response calibration

Run this before attempting fast iPolar alignment:

1. Capture synchronized native iPolar and P20 baselines.
2. Read both factory scales from fresh full-resolution P20 evidence.
3. Select one axis and one approach direction with adequate physical headroom.
4. Use small same-direction commands to take up backlash. Label these samples
   `backlashTakeup`; never use them to estimate loaded gain.
5. Once loaded response is visible, collect at least three same-direction
   command/response pairs. Record signed offset change, raw command, iPolar mode,
   centre digest, capture digest, hardware epoch, and P20 scale before/after.
6. Fit a conservative loaded-response interval in raw units per pixel. Bind the
   calibration to one axis, one command direction, and one corrected-offset sign.
7. Validate the fit with a withheld command. It must improve in the expected
   direction and achieve 0.3 to 1.5 times the predicted reduction.
8. Repeat independently for every axis and direction intended for use.

The historical `X+300` event is a mandatory replay fixture. It must be rejected,
not clipped into a smaller command.

## Block 2: five-minute iPolar trial

Start the timer after the fixed configuration and response calibration are
already valid.

For each command:

1. Require three consecutive live solves from one stream session, at least five
   stars each, within a five-second window.
2. Require matching hardware epoch, iPolar mode, RA-centre digest, and capture
   profile digest. Every frame must have a unique content digest.
3. Require one declared monotonic clock domain, strictly increasing frame times,
   and pre-evidence no older than two seconds when the plan is created.
4. Require offset spread at most 1 pixel.
5. Plan only from a loaded-response calibration for the observed offset sign.
6. Bind the command identifier, calibration SHA256, clock domain, stream,
   hardware epoch, mode, centre digest, capture digest, pre-frame identifiers,
   pre-frame content digests, and pre-frame times into the immutable plan digest.
7. Stay in the engaged direction. A reversal aborts the fast trial and requires
   a new take-up qualification.
8. Reject a requested command above the lesser of the calibrated maximum and
   200 raw units. Never clip it.
9. Allow at most six commands per axis.
10. Execute one command, wait for explicit controller idle and settling, then
   acquire fresh iPolar and P20 post-evidence.
11. Require the response receipt to name the executed plan digest and complete
    after planning in the same monotonic clock domain.
12. Validate only strictly later same-stream post frames with fresh, unique
    content digests that do not overlap any pre-frame content. Require correct
    signed response, no crossing beyond 1 pixel, material improvement beyond
    observed spread, and a response ratio from 0.3 to 1.5 before another command.

Completion requires:

- median absolute offset at most 1 pixel;
- spread at most 0.3 pixel;
- stable native iPolar green-circle state;
- P20 scales within physical limits;
- no failed or unexplained response;
- elapsed time at most 300 seconds.

The result remains an iPolar-relative alignment claim until Block 3 supplies an
independent true-pole comparison.

## Block 3: five-minute TPPA trial

Start from a mechanically fixed state. Do not adjust UPAS or tripod between
determinations.

1. Run at least three fresh, uncached determinations on the qualified arc with
   true-pole refraction enabled.
2. Require qualified, fresh station pressure, temperature, and humidity plus
   qualified site, elevation, epoch, clock, coordinate frame, and exact solve
   mid-times.
3. Require qualified three-point geometry, minimum arc span, and closure.
4. Compute pairwise differences as spherical polar-error-vector separation, not
   independent raw altitude/azimuth component subtraction.
5. Require maximum pairwise separation at most 0.5 arcminute.
6. Require reported final error at most 1 arcminute.
7. Complete within 300 seconds.
8. Capture a current, instrument-bound, calibrated independent true-pole witness
   in the same mechanical state using a qualified disjoint input path. Its own
   error must be at most 0.5 arcminute and its spherical-vector separation from
   TPPA at most 0.5 arcminute. Their sum must remain at most 1 arcminute.

The run cannot claim absolute accuracy if atmosphere, site, clock, epoch,
witness calibration, pole convention, or mechanical-state provenance is
missing.

## Block 4: reconcile iPolar and TPPA

Use the sequence:

`iPolar pre -> TPPA A -> TPPA B -> TPPA A -> iPolar post`

No actuation is allowed inside the block.

- If TPPA A repeats while TPPA B differs, investigate arc geometry/refraction.
- If TPPA A itself walks with time, investigate mechanical or thermal movement.
- If iPolar remains near zero while repeated TPPA is near two degrees, quarantine
  iPolar centre calibration or its absolute overlay.
- If recalibrated iPolar agrees with repeated TPPA, preserve the old centre as
  the failed discriminator and qualify the new centre only after repeat sessions.

Neither instrument is promoted to ground truth merely because the two agree.

## Promotion gates

### Supervisor live actuation

Remain in shadow mode until:

- loaded-response calibration exists for the required axis/sign/direction;
- a withheld command passes direction and scale validation;
- P20 before/after scale evidence is complete;
- replay tests reject take-up learning, reversal, stale frames, stale centre,
  stale hardware epoch, excessive command, wrong direction, and implausible gain;
- at least three separate field trials finish without a safety or provenance
  violation.

### TPPA sub-arcminute claim

Remain `UNPROVEN` until:

- the executable qualification gate passes;
- the independent witness has a defensible calibration and uncertainty;
- at least three sessions on different nights pass;
- an alternate-arc A/B/A campaign bounds sky-position bias;
- a fixed-state time series bounds mechanical/thermal walk;
- the error budget allocates measured random and systematic terms.

## Current implementation evidence

- TPPA qualification policy:
  `PolarAlignment\TppaFastQualification.cs`
- TPPA policy tests:
  `NINA.Plugins.PolarAlignment.Test\TppaFastQualificationTest.cs`
- iPolar fast-path policy:
  `src\upas_control\ipolar_fast_path.py`
- iPolar policy tests:
  `tests\unit\test_ipolar_fast_path.py`
- TPPA full suite: 396 passed.
- UPAS supervisor intended-scope suite: 850 passed, 2 skipped, with 5 unrelated
  modified P20 commissioning fixture tests explicitly deselected. Those five
  quarantined tests must be reconciled by their owner before claiming a wholly
  green repository.

These modules are not permission to actuate. They are executable admission
criteria for the next controlled field campaign.
