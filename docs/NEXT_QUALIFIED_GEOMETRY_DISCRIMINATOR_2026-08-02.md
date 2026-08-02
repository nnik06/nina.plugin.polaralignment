# TPPA Qualified-Geometry Field Discriminator

Date prepared: 2026-08-02

## Purpose

Determine whether the failed 2026-08-02 repeatability and reciprocity results
were caused by the foreshortened 9.3-degree on-sky legs, or by a remaining
field-dependent systematic. This protocol grants no absolute-accuracy claim and
no UPAS movement authority.

## Required Build

- TPPA 2.2.6.71.
- Verify the deployed DLL hash against the release manifest before starting
  NINA.
- Confirm NINA logs the declination-aware predicted span and required RA leg.
- Keep UPAS automatic correction and pre-seat disabled for this discriminator.

## Environmental Contract

- Use the exact validated site coordinates and elevation.
- Enable true-pole refraction adjustment.
- Record pressure, temperature, humidity, wavelength, their source, and age.
- Reject fallback or stale atmosphere for an absolute comparison.
- Keep settings, focus, camera angle, and solve exposure fixed across both arms.

## Safety Contract

- Preflight the complete telescope trajectory against the balcony envelope:
  azimuth 270..360 or 0..010 degrees, altitude 25..55 degrees.
- Require every solved adjacent pair to span at least 15 degrees on sky.
- Do not move UPAS during either arm.
- Stop on a failed solve, pier-side ambiguity, trajectory violation, tracking
  anomaly, stale telemetry, or failed geometry gate.

## Arm A: Low-Declination Control

1. Select a safe field near declination +20 degrees and altitude 35..50 degrees.
2. Use approximately 16 degrees of RA travel per leg; compute the exact value
   from the 15-degree on-sky floor before the run.
3. Run VerificationOnly forward, reciprocal, then repeated-forward on the same
   fixed geometry.
4. Preserve every raw solve and qualification receipt.

## Arm B: High-Declination Comparison

1. Select the same safe high-declination region used on 2026-08-02, near
   declination +62.35 degrees, only if the complete trajectory preflight passes.
2. Use at least 32.7 degrees of RA travel per leg so the predicted adjacent
   on-sky span reaches 15 degrees. Add a small operational margin if the balcony
   trajectory remains safe.
3. Repeat the same forward, reciprocal, repeated-forward sequence.
4. Preserve every raw solve and qualification receipt.

## Decision Rules

- Each run must pass the solved minimum-pairwise-span gate.
- Repeatability vector separation must be at most 1 arcminute.
- Reciprocity vector separation must be at most 1 arcminute.
- Passing both arms establishes qualified internal consistency only.
- Arm A pass and Arm B fail implicates long RA travel, field geometry, or
  load-dependent flexure.
- Both arms pass supports foreshortened geometry as the cause of the 2026-08-02
  failure.
- Both arms fail requires stopping correction work and investigating timing,
  atmosphere, solver bias, and mechanics.
- No sub-arcminute absolute claim is allowed without a disjoint qualified
  true-pole witness.

## Movement Authority Afterward

UPAS correction remains denied until two independent fresh TPPA determinations
on qualified geometry agree in axis, sign, and magnitude, every physical travel
guard passes, and the result is independently reverified after movement.
