# Synchronized PA Discriminator

## Purpose

This campaign classifies the unresolved TPPA same-arc walk, arc dependence,
and iPolar/TPPA disagreement before any new estimator, compensation model, or
accuracy gate is introduced. It has no UPAS, mount-motion, completion, or
absolute-accuracy authority.

The operational objectives remain separate:

- TPPA alignment in less than five minutes with less than three arcminutes
  absolute total true-pole error;
- iPolar alignment in less than five minutes with at least three arcminutes
  absolute accuracy.

The discriminator does not prove either objective. It produces the synchronized
raw evidence needed to determine whether a later field trial is justified.

## Guided 900-Second Exposure Criterion

The current operational objective is polar alignment adequate for PHD2-guided
900-second DSO exposures with the ASI2600MM Pro and OAG-L/ASI220MM Mini on both
the reduced WO GT81 IV and reduced EdgeHD 9.25. This is not identical to an
absolute RA-axis metrology claim.

For a total polar error `e`, exposure `t`, and guide-star-to-image-corner radius
`r`, a conservative upper bound is:

```text
field rotation <= 2 * sidereal_rate * t * sin(e / 2)
corner smear = 2 * r * sin(field_rotation / 2)
```

Use `tools/calculate_guided_exposure_pa_budget.ps1` with a radius measured from
simultaneous main-camera and guide-camera WCS. Do not silently assume that the
OAG guide star is at the main-sensor centre. Until that offset is measured,
8000 ASI2600 pixels is a deliberately conservative planning radius.

At 900 seconds and 8000 pixels, the bound is approximately:

| Total PA error | Maximum rotation | Maximum smear |
| --- | --- | --- |
| 1.0 arcmin | 3.94 arcsec | 0.15 px |
| 1.5 arcmin | 5.91 arcsec | 0.23 px |
| 3.0 arcmin | 11.81 arcsec | 0.46 px |
| 5.0 arcmin | 19.69 arcsec | 0.76 px |

Thus a qualified three-arcminute total error is a reasonable provisional
operational ceiling for these 900-second guided exposures, while 1.5 arcminutes
provides a stronger approximately quarter-pixel margin. The actual pass must
still be based on measured WCS rotation and star-shape residuals in 900-second
frames at representative declinations. Passing this imaging criterion does not
by itself establish the absolute true-pole error.

### Replace The Planning Radius

Before a 900-second acceptance run, acquire a fixed-state main-camera solve and
a blind ASTAP solution of a PHD2 guide frame at the same field. Record both
source hashes, pixel scales, and sensor dimensions. Run:

```powershell
pwsh -NoProfile -File .\tools\calculate_oag_geometry_bound.ps1 `
  -MainCenterRightAscensionDegrees <main-ra-deg> `
  -MainCenterDeclinationDegrees <main-dec-deg> `
  -GuideCenterRightAscensionDegrees <guide-ra-deg> `
  -GuideCenterDeclinationDegrees <guide-dec-deg> `
  -MainPixelScaleArcseconds <main-scale> `
  -GuidePixelScaleArcseconds <guide-scale> `
  -MainSolutionSource <main-artifact> `
  -GuideSolutionSource <guide-artifact>
```

Without a measured PHD2 lock position, the result conservatively admits any
guide star in the full ASI220 frame. With both lock offsets supplied, it uses
the measured radial lock offset. The spherical triangle bound deliberately
does not depend on NINA/ASTAP position-angle sign conventions. Produce separate
source-bound geometry receipts for the reduced GT81 IV and reduced EdgeHD 9.25;
do not reuse one optical train's radius for the other.

### Judge Delivered Rotation

After the synchronized run and optical-train-specific geometry receipt exist,
run the authority-free analyzer:

```powershell
pwsh -NoProfile -File .\tools\analyze_synchronized_pa_discriminator.ps1 `
  -RunDirectory <synchronized-run-directory> `
  -GeometryReceiptPath <optical-train-oag-geometry.json> `
  -ExposureSeconds 900 `
  -AllowedSmearPixels 0.5
```

The analyzer verifies the manifest and all artifact hashes before reading the
main-camera WCS series. NINA position angle describes the image-up axis east of
celestial north; ASTAP's constant 180-degree adapter offset cannot affect a
slope. Each image-axis vector is parallel-transported along the unique shortest
great-circle path into the first solve's tangent basis before circular
unwrapping. This removes local-meridian convergence caused solely by movement
of the solved field centre. Raw position-angle slope remains diagnostic only.
A solve-parity change or antipodal/ambiguous transport fails closed.

The analyzer uses Theil-Sen fits over the full run and the first and last
ten-minute windows. The conservative rate includes both a residual-MAD margin
and a nonzero angular measurement floor; first/last disagreement and
implausible adjacent transported-roll jumps fail closed.

In passive mode, a pass is `PassivePhysicalRotationWitnessQualified`: the
unguided tracking run bounded physical camera roll within the configured
guide-to-corner budget. In guided mode, a pass is
`GuidedDeliveredRotationWitnessQualified`: PHD2 remained continuously guiding
with output enabled while the synchronized main-camera WCS series bounded
physical roll. The guided collector is a second read-only PHD2 client; it does
not start or stop guiding, change output, dither, settle, or restore state.

Both modes keep `PolarAlignmentInferenceQualified` and
`OperationalGuidedExposureRotationQualified` false. A low roll rate does not
bound the polar-error vector because coupling between polar error and field
rotation depends on hour angle and declination and becomes weak near the
celestial pole. Guided WCS evidence is also not an actual long exposure: a real
900-second subframe and star-shape check remain required on each optical train.
Neither verdict establishes absolute true-pole accuracy or authorizes
mount/UPAS movement.

The guided collector's RPC and event vocabulary is pinned to
`OpenPHDGuiding/phd2@4a13cf245d7e485e79533697f87b032b304df952`,
`src/event_server.cpp`. Coverage is based on `GuideStep` events, not correction
pulses; a well-tracking mount may legitimately emit few or no pulses.

## Measurement Meaning

- TPPA and iPolar estimate an RA-axis relation through different optical and
  software paths, but neither is currently qualified as absolute truth.
- Unguided guide-star displacement is a disjoint relative observable, not
  ground truth. It contains PA error, refraction, periodic tracking error,
  seeing, and mechanical flexure.
- Raw main-camera WCS position-angle rate is not physical rotation when the
  field centre changes because its local north/east basis also changes. Only
  common-tangent transported roll is admitted as a passive physical-rotation
  witness. Even that witness is not a guided-exposure qualification and does
  not alone identify the absolute RA-axis vector.
- A full PA vector requires multiple qualified geometries. The restricted
  north/west balcony can provide information, but conditioning and component
  separation must be computed rather than assumed.
- A deliberate UPAS perturbation validates sign and scale only when the applied
  physical angle is independently traceable. Open-loop controller units and the
  two-degree factory scale ticks are not an arcminute reference and cannot
  establish absolute accuracy.

## Recorder

Run `tools/run_synchronized_pa_discriminator.ps1` in the logged-in Mele Windows
session after a stationary handoff. The script starts three existing passive
collectors concurrently:

1. main-camera plate solves through the guarded static solve series;
2. raw PHD2 guide-step/centroid events with guide output disabled;
3. iPolar processed-window frames through the pinned window recorder.

The parent adds 1 Hz NINA mount telemetry, explicit atmosphere provenance,
start/mid/end Windows-time offset probes, UTC plus one monotonic clock, process
outcomes, coverage counts, and SHA-256 for every retained artifact.
The manifest labels the acquisition `PassiveUnguidedTracking`; downstream
analysis must preserve that authority boundary.


The iPolar evidence is explicitly a processed application-window capture, not
a raw iPolar sensor frame. It cannot establish iPolar absolute accuracy.

Example 30-minute fixed epoch:

```powershell
pwsh -NoProfile -File .\tools\run_synchronized_pa_discriminator.ps1 `
  -DurationSeconds 1800 `
  -MainSolveCadenceSeconds 30 `
  -Phd2ExposureMilliseconds 1500 `
  -IPolarCadenceMilliseconds 60000 `
  -PressureHpa 997.4 `
  -TemperatureCelsius 34.2 `
  -RelativeHumidityPercent 68.0 `
  -AtmosphereSource manual-qualified-meter `
  -AtmosphereObservedUtc 2026-08-02T20:00:00Z `
  -ExpectedPierSide West
```

Do not connect the NINA Weather or Safety Monitor devices. Environment values
come from the explicitly named external source.

## Guided Rotation Witness

Use guided mode only after PHD2 is already connected, calibrated, guiding a
fixed star, and has guide output enabled. Complete any dither and settle before
starting. The runner does not change PHD2 state and rejects any observed
dither, settle, star/lock change, calibration, pause, looping transition,
guiding-parameter change, configuration change, or alert during the interval.

Example 30-minute synchronized guided witness:

```powershell
pwsh -NoProfile -File .\tools\run_synchronized_guided_rotation.ps1 `
  -DurationSeconds 1800 `
  -MainSolveCadenceSeconds 30 `
  -Phd2StateProbeCadenceSeconds 30 `
  -PressureHpa 997.4 `
  -TemperatureCelsius 34.2 `
  -RelativeHumidityPercent 68.0 `
  -AtmosphereSource manual-qualified-meter `
  -AtmosphereObservedUtc 2026-08-02T20:00:00Z `
  -ExpectedPierSide West
```

Analyze that schema-3 run explicitly as guided evidence:

```powershell
pwsh -NoProfile -File .\tools\analyze_synchronized_pa_discriminator.ps1 `
  -RunDirectory <synchronized-guided-run-directory> `
  -GeometryReceiptPath <optical-train-oag-geometry.json> `
  -ExpectedEvidenceMode GuidedTrackingRollWitness `
  -ExposureSeconds 900 `
  -AllowedSmearPixels 0.5
```

A guided pass means only that delivered WCS roll stayed within the declared
900-second corner-smear budget under the sampled geometry and guiding state.
It does not prove the same result at a different target geometry, prove round
stars in an uninterrupted 900-second subframe, or identify an absolute PA
vector. Keep `ActualLongExposureArtifactPresent=false` until a separately
sealed exposure artifact exists; this runner never upgrades that field.

## Preregistered Sequence

Keep UPAS disabled and do not touch the tripod between epochs.

1. **A1 fixed epoch:** tracking on, one qualified safe field, 30-40 minutes.
2. **A2 repeat:** repeat the same field and pier side at a separated hour
   angle. This separates field geometry from elapsed time.
3. **B alternate geometry:** repeat at a second safe north/west geometry whose
   design matrix is demonstrably full rank.
4. **Pier-side epochs:** when mechanically and geometrically safe, acquire
   separate alternating pier-side epochs. Do not automate the pier transition
   in this recorder.
5. **iPolar RA-rotation calibration:** use the existing guarded RA-rotation
   witness campaign to measure the iPolar star-field rotation centre. Do not
   infer it from the vendor cross or circle alone.
6. **Tracking-off control:** perform only under a separately reviewed launcher.
   This recorder requires sidereal tracking and does not change mount state.

## Analysis Rules

Preregister before viewing the results:

1. Reproduce the historical 2.73 arcsec/min TPPA walk only when its 95 percent
   slope interval includes that value. Refute it only when the absolute slope
   is below 0.5 arcsec/min and the interval excludes the historical value.
2. Compare first and last ten-minute reductions within each epoch. A vector
   separation above one arcminute is nonstationary for the intended trial.
3. Compare fixed-field epochs and alternate geometries. A vector separation
   above 1.5 arcminutes leaves field dependence unbounded.
4. Treat main-minus-guide motion as evidence of differential optical flexure;
   common main-plus-guide motion as compatible with mount/tripod or atmosphere;
   and pier-odd motion as compatible with gravity/load flexure. None of these
   signatures alone proves a cause.
5. Treat raw guide drift only as a relative witness. It may support an
   absolute result only through a qualified multi-geometry forward model with
   refraction and flexure terms bounded.
6. Re-express TPPA and iPolar in one measured coordinate frame before comparing
   them. A vendor screen overlap is not an absolute vector measurement.

## Admission To The Fast Field Trial

The next less-than-three-arcminute, less-than-five-minute trial remains denied
until all of the following evidence exists:

- start/mid/end clock offsets are present and within 100 milliseconds;
- repeated fixed-field and alternate-geometry reductions are stationary within
  the preregistered one and 1.5 arcminute bounds;
- a disjoint multi-geometry witness has a calibrated uncertainty and agrees
  with TPPA within the allocated absolute-error budget;
- iPolar's measured rotation-centre calibration either reconciles its result
  within three arcminutes or excludes iPolar from the acceptance path;
- UPAS signed response, engagement distance, transfer gain, and one-shot
  initial-error envelope are qualified in the current mechanical setup;
- the conservative total error budget remains below three arcminutes with
  margin; and
- at least five independent cold-start trials are preregistered, with success
  judged by the qualified witness rather than TPPA or iPolar self-report.

The one-shot fast path must pin arc direction and pier side, reject invalid
input geometry/solve quality, and admit movement only when the starting error
and calibrated UPAS transfer-gain uncertainty can still land below the target
after one move. Repeated TPPA confirmations improve precision but do not remove
shared systematic bias.
