# Guided 900-Second Acceptance Protocol

## Objective

Qualify polar alignment as operationally sufficient for uninterrupted
PHD2-guided 900-second DSO exposures on both supported optical trains:

- EdgeHD 9.25 + 0.7x + OAG-L/ASI220MM Mini + ASI2600MM Pro;
- WO GT81 IV + 0.8x + OAG-L/ASI220MM Mini + ASI2600MM Pro;
- HAE29C-EC mount under the Dubai balcony constraints.

This is a delivered-imaging qualification. It is not an absolute true-pole
calibration and cannot make TPPA, iPolar, or PHD2 an independent truth oracle.

## Operational polar-error budget

Until an optical-train-specific OAG geometry receipt replaces the planning
radius, use a conservative guide-star-to-farthest-corner radius of 8000
ASI2600 pixels. For a 900-second exposure and 0.5-pixel maximum field-rotation
smear:

- the conservative maximum total PA error is 3.27 arcminutes;
- use 3.0 arcminutes as the provisional hard operating ceiling;
- prefer 1.5 arcminutes for approximately 0.23-pixel margin;
- at 3.0 arcminutes the bound is 0.458 pixels: about 0.216 arcseconds on the
  reduced EdgeHD and 0.929 arcseconds on the reduced GT81.

The bound assumes guiding removes translation. Passing a TPPA number below
3.0 arcminutes is necessary planning evidence, not sufficient field evidence.

## Fixed-state prerequisites

Run each optical train as a separate qualification epoch. Before collecting a
bracket:

1. Install and hash-verify the reviewed TPPA runtime while NINA is closed.
2. Connect only camera, mount, filter wheel, focuser, rotator, guider, flat
   device, and Switch. Keep Weather and Safety Monitor disconnected and never
   alter Switch output channels.
3. Keep the flat panel open with its light off.
4. Fix camera/rotator orientation, OAG prism and guide-camera focus. Use a
   current PHD2 calibration appropriate to that unchanged geometry.
5. Focus the main camera, select one filter, enable cooling, and let the mount
   and optics thermally settle. Disable autofocus, dithering, meridian flip,
   filter changes, and derotation for the bracket.
6. Use one safe stationary field and pier side. Confirm sidereal tracking,
   PHD2 guiding, guide output, lock position, algorithms, exposure, camera
   gain/offset/binning/readout mode, filter, focus and cooling are stable.
7. Enable TPPA `Adjust for refraction` and verify that every qualification
   artifact records `true-celestial-pole` and
   `refractionAdjustmentEnabled=true`. An apparent-refracted-pole result cannot
   satisfy the 3.0-arcminute true-pole planning gate.
8. Record pressure, temperature and humidity from an explicit qualified source
   without connecting NINA Weather or Safety devices.
9. Preserve strict undistorted ASTAP WCS files from blind guide-camera and
   main-camera solves no more than 60 seconds apart inside the evidence bundle.
   Both must retain valid UTC `DATE-OBS`, and the train must remain stationary
   and mechanically unchanged between them. Produce the
   create-new schema-v2 receipt with
   `tools/new_derived_oag_geometry_receipt.ps1`. Qualification reopens both
   files once, binds each digest to the same bytes it reparses, projects the
   true sensor centres through TAN WCS, and independently reproduces every
   parsed and computed geometry field. Duplicate/unknown receipt properties,
   stale pairs, path indirection, and distorted or ill-conditioned WCS fail
   closed.
   The hand-entered `calculate_oag_geometry_bound.ps1` result is
   diagnostic-only. Never reuse a receipt across optical trains or camera
   geometry changes.
10. Replace each nominal policy pixel scale with the same-session measured WCS
    scale, replace the filter placeholder with the exact NINA/FITS filter name,
    and preserve the resulting policy file and SHA-256. The exact filter must
    appear in both `RequiredFilterName` and `AllowedFilterNames`; `None`,
    `Unknown`, `--`, placeholders, aliases, and case-only approximations fail.
11. At the unchanged field, focus, cooling, gain 100, offset 50, bin 1, and exact
    filter, acquire one 10-second and one 60-second LIGHT scout no more than five
    minutes apart. Run `tools/new_actual_exposure_saturation_scout.ps1` and
    preserve the two FITS files, calibrated policy, receipt, and all SHA-256
    values. Every scout-critical policy field must be explicit rather than
    inherited from analyzer defaults.
12. A `PASS` scout establishes only that the measured background, gradient,
    pedestal consistency, and projected saturation headroom do not prohibit a
    900-second attempt. It grants no connection, sequence-start, motion, polar-
    alignment, guiding, or star-shape authority. A pass never predicts that a
    900-second exposure will guide well or produce acceptable stars.
13. Before starting the collector, run `tools/test_guided_900s_readiness.ps1`
    with the loaded plugin directory, exact passive sequence, calibrated policy,
    both scouts and their passing receipt, schema-v2 OAG receipt, and fresh TPPA
    operational report. It independently checks every declared hash, reproduces
    the scout and OAG receipts from their immutable sources, rejects `.template.`
    policies, and requires a passing three-or-more-run TPPA set inside the
    declared 3-arcminute ceiling. Its create-new receipt is a readiness record
    only: it grants no connection, sequence-start, motion, or absolute-accuracy
    authority.

## Acquisition

1. Generate the train-specific NINA sequence with
   `tools/new_actual_exposure_sequence.ps1`.
2. On first use with each NINA build, load it without starting and visually
   verify the exact `5 x short + 1 x 900 s + 5 x short` tree. It must contain
   only annotations, loop containers and `TakeExposure` instructions.
3. Start `tools/capture_phd2_guided_evidence.ps1` with the exact
   `OpticalTrainId`, five-second state cadence, and enough duration to cover
   the complete bracket plus margins.
4. Confirm the collector remains alive and has passed its initial read-only
   PHD2/NINA gates before starting the NINA sequence.
5. Run the sequence without any manual, sequencer, or external change to the
   fixed state. Preserve all eleven original FITS files.
6. Require the sequence to finish normally and the collector to write its
   qualified `summary.json`, `guidesteps.csv`, `events.jsonl`, and schema-2
   `state.json`. A collector or sequence failure makes the run ineligible.

## Offline analysis

Run `tools/analyze_actual_exposure_bracket.ps1` with the five pre-controls,
one 900-second frame, five post-controls, exact policy, PHD2 summary, sampled
state receipt, OAG geometry receipt, and hash-pinned ASTAP executable. Then
reproduce the receipt with `tppa-qualify verify-actual-exposure`.

The analyzer must independently qualify:

- exact exposure ordering and durations;
- FITS state equality and measured WCS scale;
- complete PHD2 GuideStep and fixed-state coverage;
- stable control PSF and pre/post consistency;
- matched-star survival and bounded long-frame attrition;
- center and all four outer-zone differential ellipticity and major-axis
  growth;
- absolute eccentricity for the sampled EdgeHD policy;
- at least two-pixel control FWHM for the undersampled GT81 policy.

## Verdicts

`QUALIFIED` requires all structural evidence and star-shape gates to pass for
one actual 900-second exposure. It proves only that the train delivered an
acceptable bracket under the recorded conditions.

`INCONCLUSIVE` applies when sampling, seeing, focus, star count, GT81 PSF
sampling, state continuity, geometry, or provenance is insufficient. It is not
a failure of polar alignment and must not be converted into a pass.

`FAILED` applies when valid evidence shows the 900-second frame exceeds a
preregistered delivered-performance threshold. Diagnose PA, guiding, flexure,
optics, focus and seeing separately; do not infer one cause from star shape.

## Goal completion matrix

The project objective remains incomplete until all rows are supported by
current artifacts:

| Requirement | Required evidence |
| --- | --- |
| EdgeHD 900-second suitability | Qualified EdgeHD bracket and reproduced receipt |
| GT81 900-second suitability | Qualified GT81 bracket and reproduced receipt |
| PHD2 continuity | Qualified per-train PHD2 event/GuideStep/state artifacts |
| OAG geometry | Separate hash-bound receipt for each train |
| TPPA operational alignment | Repeated qualified true-pole TPPA determinations at or below 3.0 arcminutes |
| Safety | No forbidden device connection, no unverified UPAS motion, and preserved final-state evidence |

A pass on one optical train cannot qualify the other. Agreement among TPPA,
iPolar and PHD2 is supporting consistency only; no vote or average establishes
absolute polar accuracy.
