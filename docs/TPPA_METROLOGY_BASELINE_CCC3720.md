# TPPA Metrology Baseline ccc3720

## Frozen Reference

The legacy three-point estimator baseline is commit
`ccc3720aa9bffb5351c29f2263f20cf0b92a237a`, plugin version `2.2.6.33`.
Its local Release test baseline is 307 passing tests.

Until a field campaign closes the July same-arc walk and establishes an
external accuracy reference, estimator equations, actuator calibration, and
hardware configuration must not change in the same experimental block.
One causal variable changes at a time, with an unchanged A-B-A control.

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

## Error Budget

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
