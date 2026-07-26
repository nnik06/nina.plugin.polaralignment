# PHD2 Polar Drift Validation for TPPA/UPAS

## Purpose

Use PHD2 Polar Drift Alignment (PDA) as an independent polar-region check after TPPA. TPPA same-arc repeats measure internal repeatability; they do not establish absolute accuracy because both repeats share the same plate-solving geometry and systematic errors.

## TPPA internal qualification

Before PDA, run Verification Only. The enhanced diagnostic measures a forward arc, the same arc in reciprocal order, and a repeated forward arc (nine solves total). Require both the forward-repeat repeatability verdict and the forward-reciprocal reciprocity verdict to pass. Reciprocity detects direction-dependent TPPA arc bias, but it remains an internal check and does not replace PDA as the independent method.

## Preconditions

- Complete TPPA and leave the UPAS stationary.
- Keep the camera, rotator, guide camera, and optical train unchanged.
- Track at sidereal rate.
- Point the mount RA axis within 5 degrees of the apparent north pole and slew the telescope to Dec +90 degrees, preferably counterweight-down.
- Select a clean, unsaturated guide star as close to the pole as practical.
- Set PHD2 hemisphere to North and verify the Mirror Image setting for the OAG/camera orientation.
- Do not run TPPA, guide corrections, dithers, slews, autofocus, or UPAS moves during the drift measurement.

## Measurement

1. Record the final repeatable TPPA signed azimuth, altitude, and total errors.
2. Open PHD2 Polar Drift Alignment, select the guide star, and start the drift.
3. Ignore the early estimate while the target line is visibly moving.
4. Continue for at least 10 minutes. Prefer 12-15 minutes for a sub-arcminute comparison.
5. Accept the PDA estimate only when its PA-error value and adjustment angle are stable over the final 3 minutes. As an initial field criterion, require PA error to vary by no more than 0.5 arcminute and angle by no more than 5 degrees.
6. Stop PDA and record its final PA error, angle, elapsed time, star position, pointing coordinates, pixel scale, hemisphere, and mirror setting.
7. Do not adjust the UPAS during the first validation run. This preserves a clean TPPA-versus-PDA comparison.

## Verdict

- **TPPA repeatability pass:** both fresh TPPA vectors agree within the plugin's tolerance-scaled repeatability limit.
- **PDA eligible:** duration and final-window stability criteria pass.
- **Absolute agreement candidate:** eligible PDA total error differs from the repeatable TPPA consensus total by no more than 1 arcminute.
- A single disagreement is diagnostic, not proof that either method is wrong. Repeat both methods without moving hardware.
- Persistent same-sign disagreement over at least three sessions indicates a systematic offset. Investigate refraction, solve timing, camera parity, pointing distance from the pole, flexure, and TPPA arc geometry before applying any empirical correction.

## Automation Boundary

PHD2 2.6.14 implements PDA as an interactive window. Its standard JSON-RPC server exposes looping, star selection, guiding, guide-output control, and related operations, but no command to start/stop PDA or retrieve its PA-error/angle result. The existing supervisor can prepare the field and guide-star lock, but full unattended PDA requires either:

- an upstream PHD2 server-API extension exposing PDA state/results, preferred; or
- a maintained PHD2 fork/companion implementation of the PDA least-squares calculation.

UI click automation is unsuitable for safety-critical unattended UPAS movement. Until a structured API exists, PDA should validate TPPA and should not directly command the UPAS.

## Notes

Neither PDA nor TPPA is qualified ground truth. A third independent witness, the iOptron iPolar, is covered by `IPOLAR_WITNESS_CAMPAIGN.md`; it is a corroborating witness with no control authority, and it does not replace the qualified open-sky drift measurement that an absolute claim still requires.

PHD2 Guiding Assistant DEC slope is not interchangeable with PDA. Near-pole geometry, camera orientation, RA periodic motion, and the chosen field all affect a raw slope. Use PDA's polar-region calculation for this comparison.
