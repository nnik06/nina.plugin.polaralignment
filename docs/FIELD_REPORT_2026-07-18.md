# UPAS TPPA field report - 2026-07-18

## Build

- Deployed commit: 683b5a6 (Bound UPAS reversal engagement)
- DLL SHA256: AB947B005FEBB5640B0D927430F8A65B7AF4FA214FC2CDF09D008BF8E72EC322
- Regression suite: 113 passed, 0 failed

## Completion-verification result

Completion verification now slews back to the original first measurement
point and repeats the original A -> B -> C mount arc in the same direction.
This removed the large and sign-changing result differences seen when
verification either continued onto a different arc or traversed the same arc
backwards.

Passive same-direction verification before the corrective run differed by
only 14 arcsec in azimuth, 10 arcsec in altitude, and 17 arcsec total.

## Automated correction

The first bounded run started at 47'19" total and reached 2'47" before the
old 12-move limit stopped it. Acquisition moves were deliberately consumed
while crossing the measured 24-unit azimuth engagement distance.

The resulting patch:

- limits an engagement command to the remaining clearance instead of always
  sending the full base probe magnitude;
- raises the fresh-feedback move budget from 12 to 18;
- retains the 4-degree cumulative azimuth travel guard and fresh three-point
  feedback after every move.

After deployment, a fresh run started at 3'41" total. One X=+8 move produced
0'53", and an independent no-move confirmation measured 0'23". TPPA
finished automatically.

Subsequent no-move measurements were:

| Measurement | Azimuth | Altitude | Total |
| --- | ---: | ---: | ---: |
| Passive run, first | +0'27" | +0'11" | 0'29" |
| Passive run, confirmation | -0'06" | +0'26" | 0'26" |
| Delayed run, first | +0'34" | +0'08" | 0'35" |
| Delayed run, confirmation | +0'27" | +0'12" | 0'30" |

The delayed pair was obtained roughly 50 minutes after convergence without
UPAS movement. The former large fresh-run discrepancy was not reproduced.

## PHD2 drift

An 8-minute passive trace was too nonstationary for a polar-error conclusion.
A second 20-minute trace contained 557 guide steps. With the 2.1 arcsec/pixel
guide scale, ordinary least-squares slopes were:

- full 20 minutes: DEC -0.52 arcsec/min, RA -2.73 arcsec/min;
- after the first 2 minutes: DEC -0.51 arcsec/min, RA -2.77 arcsec/min.

Five-minute DEC windows changed from -1.01 to -0.58, -0.24, and
-0.20 arcsec/min, while RA changed from -2.12 to -2.59, -2.99, and
-3.26 arcsec/min. The curvature and simultaneous RA trend indicate settling,
tracking, or periodic structure. This trace is not a clean constant DEC drift
and does not establish a TPPA/PHD2 disagreement.

## Next field session

1. Begin with passive TPPA A -> B -> C reproducibility checks.
2. Keep the current fresh-feedback controller and travel guard unchanged.
3. Exercise a true near-target X reversal only when the starting residual
   naturally requires it; verify the final engagement command equals the
   remaining clearance.
4. For an independent drift comparison, use a documented drift-alignment
   pointing and collect a longer trace after adequate settling. Do not infer
   polar error from a curved short trace.

