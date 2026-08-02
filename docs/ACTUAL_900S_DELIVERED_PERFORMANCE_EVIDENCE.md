# Actual 900-Second Delivered-Performance Evidence

## Claim boundary

This layer answers one narrow operational question: with one fixed optical train,
target, filter, focus state, camera state, and uninterrupted PHD2 guiding, did an
actual 900-second main-camera frame preserve star shape relative to short control
frames taken immediately before and after it?

It does not estimate polar error, certify absolute true-pole accuracy, or grant
mount/UPAS motion authority. A passing result is seeing-inclusive delivered-image
evidence. It cannot uniquely separate polar error from seeing, optics, guiding,
mirror motion, cable forces, differential flexure, or focus drift.

## Acquisition contract

1. Use at least five 10-30 second controls before and five after one 900-second
   frame. Do not register or stack the controls.
2. Keep target, pier side, rotator, filter, gain, offset, binning, readout mode,
   focus, cooling, tracking, PHD2 profile/exposure, and guiding algorithms fixed.
3. Start `tools/capture_phd2_guided_evidence.ps1` before the first control. Its
   qualified interval must contain the complete bracket plus the configured
   margin. The evidence producer independently reparses the hash-bound
   `guidesteps.csv`; contiguous frame numbers, monotonic UTC/monotonic clocks,
   bounded cadence gaps, and first-to-last coverage of the complete imaging
   bracket are mandatory even when the collector summary says it qualified.
4. Produce the orientation-independent OAG geometry receipt for the same train.
5. Record the required state fields in a schema-1
   `TppaActualExposureStateReceipt` spanning the complete bracket.
6. Every FITS file must retain a current WCS whose measured pixel scale agrees
   with the hash-bound optical-train policy within its preregistered tolerance.
7. Run `tools/analyze_actual_exposure_bracket.ps1` offline. It copies source
   FITS files, runs hash-pinned ASTAP `-extract2` only on the copies, validates
   the exact seven-column catalog schema, and binds every artifact SHA-256.
8. Recompute the deterministic receipt with
   `tppa-qualify verify-actual-exposure --manifest manifest.json --receipt receipt.json`.

The analyzer first defines the eligible population from stars that survive every
short control, then measures attrition explicitly when those stars disappear or
become unusable in the long frame. This prevents a degraded 900-second image
from passing on only its best surviving stars. It rejects blends, edges,
nonlinear or nonfinite pixels, low SNR, unstable background and failed adaptive
moments, fits a local sigma-clipped background plane, and compares signed
adaptive-moment ellipticity and major-axis growth in the center and each of four
outer quadrants. The worst outer quadrant is gated independently. Measured WCS
scale, long-frame attrition, control-frame scatter, and a two-pixel minimum
control FWHM are mandatory fail-closed gates.

## Optical-train policies

The templates under `tools/actual-exposure-policies` are preregistration
starting points, not immutable camera calibrations.

- EdgeHD 9.25 + 0.7x + ASI2600MM: nominal 1645 mm and 0.4715 arcsec/pixel.
  Its sampling permits an absolute eccentricity quality gate, while the receipt
  still makes no polar-alignment inference.
- WO GT81 IV + 0.8x + ASI2600MM: nominal 382.4 mm and 2.028 arcsec/pixel.
  Absolute eccentricity is explicitly disabled. Differential long-vs-control
  moments qualify only when the measured control FWHM is at least two pixels;
  better seeing or focus that produces a narrower sampled PSF returns an
  inconclusive result rather than silently accepting subpixel phase bias.

Before a field campaign, replace nominal pixel scale with the same-session solved
scale and verify gain conversion/linearity for the exact ASI2600MM mode. Changing
those values creates a new policy file and hash.

A passing bracket establishes only that one delivered 900-second exposure met
the preregistered star-shape limits under the recorded state and guiding. The
thresholds are operating limits, not confidence intervals, and require field
calibration on both optical trains before they support a production decision.

Nominal geometry sources: William Optics lists GT81 IV at 478 mm with an optional
0.8x FLAT 6AIII; Celestron lists EdgeHD 9.25 at 2350 mm and its 0.7x reduced focal
length as 1645 mm; ZWO lists ASI2600MM pixels as 3.76 micrometers.

## Interpretation with TPPA, iPolar and PHD2

TPPA and iPolar are axis-geometry witnesses with distinct calibration and
refraction systematics. Qualified PHD2 drift or guided-continuity evidence is a
delivered tracking witness. The 900-second bracket is an end-product witness.
Agreement narrows a bounded operational claim, but no vote or average among the
three establishes absolute PA without independently qualified truth and a stated
error budget. Disagreement is diagnostic evidence, not a reason to pick whichever
tool reports the smallest number.
