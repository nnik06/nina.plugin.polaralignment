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

