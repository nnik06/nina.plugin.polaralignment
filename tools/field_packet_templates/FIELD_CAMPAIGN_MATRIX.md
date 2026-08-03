# Deficit-Driven Field Campaign Matrix

Checkpoint: `__SOURCE_COMMIT__`

Inventory result on 2026-08-04: no non-synthetic local field artifact was
found that is compatible with this checkpoint's covariance, cadence or sealed
fast-campaign schemas. Older results remain useful engineering history but do
not reduce the preregistered denominator below.

Changing between the GT81 and EdgeHD changes the hardware/load/mechanical
identity consumed by both authority parsers. Commission and preserve separate
covariance and cadence authorities for each optical train.

## Per-train commissioning quota

Use two commissioning nights for one mechanically unchanged train. Do not swap
trains between the two nights.

| Arm | Night C1 | Night C2 | Total | Required distribution |
| --- | ---: | ---: | ---: | --- |
| Coarse response | staged | staged | protocol minimum | both signs/axes, fit plus held-out; outer range only after prior-stage pass |
| Covariance VerificationOnly runs | 10 | 10 | 20 | unchanged train, UPAS stationary, schema 6 |
| A settle probes | 10 | 10 | 20 | 5 each direction per night |
| B candidate/reference pairs | 10 | 10 | 20 | 5 each direction and 5 each order per night |
| C same-cadence null pairs | 5 | 5 | 10 | alternate directions; at least 4 total each |
| D exact-path timing determinations | 30 | 29 | 59 | split each night nearly evenly by direction |

This 30/29 timing split and every 10/10 or 5/5 split remain below the
70-percent single-night dominance ceiling. Keep all initiated observations;
zero post-hoc exclusions are allowed.

Suggested within-night order:

1. immutable identity/hash/state preflight;
2. five covariance runs;
3. five A probes;
4. five B pairs with alternating order and direction;
5. half of the D timing determinations;
6. five covariance runs;
7. five A probes;
8. five B pairs;
9. five C pairs;
10. remaining D determinations;
11. immutable final-state and log preservation.

Interleaving prevents temperature or clock trend from being confounded with a
single evidence arm. The scripts, not this schedule, determine eligibility.

After C2, run all analyzers. Mint the covariance and cadence authorities only
if every report passes without exclusion and all identities/hashes agree.

## Per-train fast-alignment denominator

Seal exactly 20 attempts over three later nights. Use the following starting-
error stratum sequence unless the sealed manifest declares a different exact
sequence before attempt 1.

| Night | Ordered TPPA-reported starting-error strata, arcminutes |
| --- | --- |
| F1, 7 attempts | 0--30, 60--120, 180--240, 30--60, 240--300, 120--180, 0--30 |
| F2, 7 attempts | 30--60, 120--180, 240--300, 0--30, 180--240, 60--120, 30--60 |
| F3, 6 attempts | 60--120, 180--240, 0--30, 240--300, 120--180, 240--300 |

Totals are 4, 3, 3, 3, 3 and 4 attempts across the six contiguous strata.
Every initiated attempt counts, including admission denial, cancellation,
crash, timeout and missing terminal telemetry.

Before every governed attempt:

1. obtain fresh supervisor/P20 physical AZ and ALT evidence;
2. if both conservative 3-sigma bounds are within +/-0.1 degree, mint a fresh
   zero witness;
3. otherwise perform one bounded return-to-zero transaction and obtain a fresh
   post-move witness;
4. deny the attempt if evidence is missing, stale, ambiguous or outside the
   physical/software envelope;
5. start the 300-second TPPA clock only after physical-zero admission.

Create starting error by bounded manual tripod orientation while UPAS remains
physically zero. Confirm the fresh TPPA-reported vector lies inside the sealed
stratum before starting the governed attempt. Never relabel an out-of-stratum
attempt after seeing its result.

Campaign pass requires at least 16/20 successes. The 0--30 and 240--300
arcminute strata each have four attempts and require at least two successes;
each three-attempt stratum requires at least one. Every successful attempt
requires two independent fresh stationary true-pole determinations no greater
than 3 arcminutes inside 300 seconds, with zero false success and zero safety
violation.

## Imaging bracket

After a passing same-session alignment for the unchanged train, collect the
guided narrowband bracket immediately. Fix one exact filter name before the
saturation scout. Preserve fresh OAG geometry, 10/60-second scouts, readiness
receipt, `5 x 30s + 1 x 900s + 5 x 30s` FITS frames, PHD2 events/GuideSteps and
sampled NINA state. A qualified bracket for one train does not qualify the
other.

## Minimum calendar

For each train, the evidence design requires at least five nights: C1, C2, F1,
F2 and F3. The 900-second bracket may be acquired on F3 only if that session's
alignment is already qualified and the optical/mechanical state remains fixed.
Complete one train end-to-end before changing the optical train, then repeat
the five-night block for the other train.
