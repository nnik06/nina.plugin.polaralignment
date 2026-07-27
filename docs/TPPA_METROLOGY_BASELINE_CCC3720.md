# TPPA Metrology Baseline ccc3720

## Frozen Reference

The legacy three-point estimator baseline is commit
`ccc3720aa9bffb5351c29f2263f20cf0b92a237a`, plugin version `2.2.6.33`.
Its local Release test baseline is 307 passing tests.

Until a field campaign closes the July same-arc walk and establishes an
external accuracy reference, estimator equations, actuator calibration, and
hardware configuration must not change in the same experimental block.
One causal variable changes at a time, with an unchanged A-B-A control.

Historical field results that do not record `RefractionAdjustment` and the
associated atmosphere inputs cannot support a true-pole absolute-accuracy
claim. They remain usable for lower claim levels when their other provenance is complete.

## Claim Levels

The project distinguishes these claims:

1. **Model conformance**: synthetic/oracle fixtures agree with an external
   implementation of the same mathematical model.
2. **Repeatability**: independent fresh determinations agree under unchanged
   geometry and hardware.
3. **Transfer consistency**: A-B-A and reciprocal arcs return to the original
   result within declared gates.
4. **Absolute accuracy**: a qualified external witness agrees after its own
   bias, repeatability, and independence are demonstrated.

Passing a lower level never implies a higher one. In particular, the Astropy
oracle sweep is model conformance, not an absolute sky measurement.

## Open Evidence

The 2026-07-23 no-motion series walked approximately 75 arcseconds in
azimuth over 24 minutes while its median/MAD gate passed. That dataset is a
mandatory replay fixture. A result is not movement-authorizing merely because
its component and total MAD values are small; a time-trend gate must also pass.

PHD2 PDA/DA and iPolar remain passive witnesses. Neither may command UPAS or
be called ground truth until same-state repeatability, direction stability,
and cross-night bias are qualified. Open-sky qualification with a third
reference remains required before a sub-arcminute absolute claim.

## Error-Budget Provenance

Every field artifact must retain enough provenance to separate:

| Contributor | Required evidence |
| --- | --- |
| Site/time | latitude, longitude, elevation, UTC source and uncertainty |
| Atmosphere | refraction enabled state and actual pressure/temperature/humidity source |
| Geometry | A/B/C solved coordinates, arc direction, angular span, altitude, pier side |
| Timing | exposure start and midpoint UTC for every solve |
| Solver | solver identity, search radius, success/failure, solved RA/Dec |
| Mount | tracking mode, settle interval, motion completion and closure |
| Optics | camera, binning, focal length, pixel size, rotator angle, focus quality |
| Actuator | supervisor authorization, pre/post scale observations, command and verified displacement |
| External witness | raw iPolar/PHD2 output, witness state, repeatability and known bias |

Missing provenance invalidates the associated accuracy claim; it must not be
silently replaced with a default.

## Current Quantified Terms

These are measured or deterministic scales, not yet an uncertainty allocation:

| Contributor | Current scale | Type | Release treatment |
| --- | ---: | --- | --- |
| Apparent-pole versus true-pole target at the Dubai fixture | 121 arcsec | Deterministic target bias when `RefractionAdjustment=false` | True-pole mode is the default; automated correction and drift validation fail closed when it is off |
| Forward-versus-reciprocal TPPA difference | approximately 25 arcsec | Observed direction-dependent systematic | Open; retain reciprocal diagnostics and do not fold into random sigma |
| 2026-07-23 same-arc temporal walk | approximately 75 arcsec over 24 minutes | Observed nonstationary systematic | Theil-Sen trend gate rejects movement authorization |
| Same-arc random solve scatter under qualified geometry | Unknown | Random | Estimate per session from no-motion replicates after trend rejection |
| Atmosphere-input/model residual | Unknown | Systematic and time-varying | Record pressure, temperature, humidity, source, target mode, and exact solve times |
| Independent witness bias and repeatability | Unknown | External systematic | iPolar and PHD2 remain report-only until independently qualified |

No root-sum-square total is reported yet. The known terms are not established
as independent zero-mean random variables, and two of the largest observed
terms are explicitly systematic. A numerical combined uncertainty requires
qualified distributions or defensible bounds for every retained contributor.

## Experiment Order

1. Replay the July monotonic walk and verify that MAD passes while trend fails.
2. Run no-motion A-B-A blocks with unchanged estimator and hardware.
3. Qualify iPolar only as a passive witness using repeated same-state sessions.
4. Perform signed, adaptive UPAS perturbations (`2'`, then `5'`, optionally
   `10'`) only through the external supervisor and only after image-derived
   scale positions authorize travel.
5. Return to the initial physical state and require A closure.
6. Research multi-solve estimators in shadow mode; do not replace the legacy
   result until replay and field gates pass.
7. Seek open-sky third-reference qualification before claiming sub-arcminute
   absolute accuracy.

## Release Boundary

A build may improve diagnostics and fail-closed gates without changing the
frozen estimator. Any estimator-math change requires:

- a separately identified experimental build;
- replay of every frozen field fixture;
- no-motion and signed-perturbation comparisons against the legacy build;
- council review of the evidence and thresholds;
- no simultaneous hardware, refraction-source, or actuator-calibration change.
