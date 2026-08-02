# TPPA Five-Position Field Report - 2026-08-01

## Scope

This field block exercised the report-only five-position shadow-model check in
TPPA 2.2.6.60. It did not permit UPAS actuation and does not establish absolute
polar-alignment accuracy.

The deployed assembly was verified before launch:

- version: `2.2.6.60`
- source checkpoint: `8e4bf3c8e77a0d86949fce03657f75cb520c11a8`
- SHA256: `81D6093339D209242FD33603A7196A45C4B87747CBB7A71BFDD43429C83F1CA2`

## Restored pointing defect and fix

The first 2.2.6.59 field attempt exposed an invalid restoration coordinate:
`GetCurrentPosition()` returned RA zero while the mount was not at RA zero. The
derived restoration destination was outside the balcony envelope and the
plugin correctly refused to slew.

Version 2.2.6.60 captures the original and A/correction pointings from validated
`telescopeMediator.GetInfo().Coordinates` telemetry and fails closed on null or
non-finite data. The live 2.2.6.60 run captured:

- original: RA 308.123333 deg, Dec 62.439456 deg, JNOW;
- A/correction: RA 308.240583 deg, Dec 62.348750 deg, JNOW.

After the external 600-second deadline cancelled the diagnostic, cleanup
restored A successfully at Az 329.72 deg, Alt 33.86 deg. This verifies the field
fix at the failure point that blocked 2.2.6.59.

## Safe arc qualification

An initial candidate starting at Az 315 deg, Alt 48 deg was rejected before
movement because its outer waypoint mapped to Alt 56.99 deg. The qualified
replacement started at Az 330 deg, Alt 35 deg and kept every preflight waypoint
inside the balcony envelope on `pierEast`:

- azimuth range: approximately 329.97 to 338.75 deg;
- altitude range: approximately 34.93 to 47.05 deg.

All absolute-destination, actual-position, travel, and constant-pier-side gates
passed for the points reached. No UPAS command was issued.

## Runtime result

The diagnostic started at 04:20:30 and hit its guarded 600-second deadline at
04:30:31. It completed the three forward samples and one reciprocal sample,
then was cancelled while settling for reciprocal sample 2. A five-position fit
was therefore not produced.

Observed timing included:

- initial slew and required settle before A capture: about 62 seconds;
- forward point 1 exposure-to-solve completion: about 243 seconds, dominated by
  a one-time camera/solver delay;
- forward point 2 exposure-to-solve completion: about 57 seconds;
- forward point 3 exposure-to-solve completion: about 57 seconds;
- required waypoint settle: 30 seconds per moved point.

The launcher then called the mount stop and sequence stop endpoints. NINA
cancelled the sequence, the plugin restored A, the mount was stationary and
tracking, and the camera returned to `Idle`.

## Conclusions

1. The five-position path and cleanup are fail-closed under the tested balcony
   constraints.
2. The 2.2.6.60 validated-coordinate restoration fix is field verified.
3. The five-position shadow check is not yet compatible with a five-minute
   operational TPPA target under the observed device and solve latency.
4. This incomplete run provides no model-residual or absolute-accuracy result.
5. Do not shorten the 30-second scientific settle requirement merely to make a
   runtime number pass. First remove the one-time capture/solver cold-start and
   measure stable per-point latency.

## Next qualified test

Before another field run:

1. add explicit capture, download, solve, slew, and settle duration telemetry;
2. perform one discarded warm-up capture and solve before starting the
   diagnostic timer;
3. repeat the same safe arc with a diagnostic deadline derived from the number
   of points and the measured latency budget;
4. preserve the operational under-five-minute claim for the normal three-point
   path, while treating the five-position shadow check as a separate metrology
   campaign until its runtime is reduced without weakening data quality.

## Council review

Claude Opus 5 High and Gemini 3.1 Pro High reviewed the field packet against
canonical root `C:\Dev\upas-nina-tppa-plugin`, HEAD
`8e4bf3c8e77a0d86949fce03657f75cb520c11a8`, with the dirty worktree disclosed.
Both seats independently recommended:

- pre-warm the camera/solver path before starting the diagnostic timer;
- retain the 30-second settle and every trajectory gate;
- budget the five-position metrology timeout separately from the operational
  three-point under-five-minute goal;
- persist partial point receipts so a timeout preserves collected evidence;
- use iPolar only as a differential stability witness, not as an independent
  absolute pole reference.

The operational gate is five consecutive three-point VerificationOnly runs
under 300 seconds with no solve failures or safety-gate failures. The metrology
gate is a complete pre-warmed five-position and reciprocal dataset with
per-point residuals, all safety gates passing, and no UPAS actuation.

## Evidence

- NINA log:
  `C:\Users\nnik0\AppData\Local\NINA\Logs\20260801-040316-3.2.0.9001.14256-202608.log`
- launcher run:
  `C:\Users\nnik0\Documents\UPAS\field-20260801\tppa-5pos-20260801-0420-safe-arc`
- sequence:
  `C:\Users\nnik0\OneDrive\Documents\N.I.N.A\1_TPPA_VERIFICATION_5POS_AZ330_ALT35_20260801.json`
