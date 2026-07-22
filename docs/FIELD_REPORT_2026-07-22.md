# Field report: 2026-07-22

## Independent measurements

Two fresh TPPA three-point determinations, with no UPAS movement between them:

- Az -31'25", Alt -9'39", total 32'51"
- Az -31'11", Alt -9'42", total 32'40"

Three passive 20-minute PHD2 PDA captures at the same safe near-pole field
(Dec +84.964 deg, Alt about 30 deg) produced:

| Capture | PA magnitude | Display direction | Half-window direction change |
| --- | ---: | ---: | ---: |
| 21:26 | 29.569' | +173.689 deg | 2.589 deg |
| 21:54 | 29.154' | -176.085 deg | 1.394 deg |
| 22:48 | 29.578' | -166.766 deg | 1.287 deg |

The magnitude is repeatable, but unwrapped direction walks approximately
173.69 -> 183.91 -> 193.23 deg. Cross-capture direction repeatability fails.
No PDA or UPAS correction was authorized.

## Interpretation

The low within-capture direction changes and monotonic between-capture walk
point to a time/arc-phase systematic rather than random centroid noise. The
existing straight-line camera-space estimator must not provide an actuator
vector until direction is transformed to a common reference epoch or replaced
with a validated curvature-aware model. PHD2 calibration scaling also requires
independent validation before it is used as a certification precondition.


## Balcony map and ordinary drift validation

At altitude 45 degrees, guide-star detection succeeded at azimuths 300, 290,
280, and 270 degrees. It failed at 260 degrees, where the wall entered the
field. The guarded western operating sector is therefore 270..300 degrees.

Two passive ordinary-drift captures were made without guide output or UPAS
movement:

| Field | HA | DEC drift | Stable windows |
| --- | ---: | ---: | --- |
| Az 270, Alt 45.23 | +47.585 deg | +6.883 arcsec/min | 5 |
| Az 299.93, Alt 29.90 | +70.301 deg | +5.673 arcsec/min | 5 |

The first-order model

`D = 0.2625 * (-E*sin(HA) - A*cos(latitude)*cos(HA))`

gives a two-field diagnostic solution of Az -24.95', Alt -14.86', total
29.04'. Signs agree with TPPA and the total scale is similar, but the per-axis
differences are 6.05' and 5.20'.

This is not an absolute calibration. The fields are separated by only 22.72
degrees in hour angle (determinant -0.02412; condition number about 5), and the
second field is low enough for unmodeled refraction to materially bias drift.
Both Claude and Gemini therefore classify the result as a blunder-check only.

The solver now reports this geometry as unqualified, always keeps
`ActuationEligible=false`, requires explicit site latitude, and uses strict
future qualification floors: at least 45 degrees hour-angle separation,
determinant at least 0.04, altitude at least 40 degrees, and at most 2'
per-axis TPPA disagreement. The current balcony cannot satisfy all of those
conditions simultaneously, so ordinary drift must remain verification-only.

PHD2's pre-capture `xRate` was found to be the declination-compensated value
left from the previous guide field, not a fixed calibration invariant.
Diagnostics now log both pre-capture and post-lock calibration snapshots.

## Code checkpoints

- `2fe88e5`: guarded regular drift capture to the measured balcony opening.
- `fee921b`: guarded slew, two-field verification-only solver, post-lock
  calibration logging, and regression tests.
- `09cb64b`: fail closed on weak drift geometry.

All 151 C# tests and affected PowerShell suites passed.