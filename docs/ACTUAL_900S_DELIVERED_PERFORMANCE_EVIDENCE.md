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

1. Generate a motion-free NINA Advanced Sequencer file with
   `tools/new_actual_exposure_sequence.ps1`. It emits exactly five identical
   short controls, one 900-second light, and five identical short controls. It
   contains no equipment connection, pointing, tracking, guiding, filter,
   focus, autofocus, or shutdown instruction and refuses to overwrite an
   existing file. For example:

   `pwsh -NoProfile -File tools/new_actual_exposure_sequence.ps1 -OutputPath C:\tmp\edge-witness.json -OpticalTrainId EdgeHD-9.25-0.7-OAG-L-ASI2600MM-gain100-bin1`

   Load the generated file only after the complete field preflight. Do not
   register or stack the controls. On first use with each NINA build, load it
   without starting and visually verify the exact `5 + 1 + 5` tree; preserve
   that check with the run artifacts.
2. Keep target, pier side, rotator, filter, gain, offset, binning, readout mode,
   focus, cooling, tracking, PHD2 profile/exposure, and guiding algorithms fixed.
   Preserve the complete NINA imaging profile and vendor camera configuration;
   sampled state does not expose every driver setting such as USB bandwidth.
3. Start `tools/capture_phd2_guided_evidence.ps1` before the first control and
   supply the exact policy `OpticalTrainId`. The default five-second state probe
   cadence must remain enabled. The one read-only collector writes PHD2 events,
   `guidesteps.csv`, `summary.json`, and sampled `state.json`.
4. Its qualified interval must contain the complete bracket plus the configured
   margin. The evidence producer independently reparses the hash-bound
   `guidesteps.csv`; contiguous frame numbers, monotonic UTC/monotonic clocks,
   bounded cadence gaps, and first-to-last coverage of the complete imaging
   bracket are mandatory even when the collector summary says it qualified.
5. The schema-2 state receipt samples NINA mount, camera, filter wheel, focuser
   and rotator state throughout the bracket. It also binds the PHD2 profile,
   exposure and guide-algorithm parameter digest. Every sample, clock, cadence,
   summary statistic and bracket boundary is independently revalidated; a
   transient changed sample that later returns to baseline still fails.
   This proves only that no change was observed at the recorded sample points;
   a change that begins and ends entirely inside one sampling gap is not
   observable. Preserve sample count, observed maximum gap and cadence in the
   receipt when interpreting a run. Keep the default five-second cadence during
   guiding; the one-second option is for compatibility diagnosis, not routine
   evidence collection.
   The short controls observe the bracket endpoints, not every instant inside
   the 900-second exposure. The long image contains the delivered effect of an
   interior disturbance, but neither endpoint controls nor sampled state prove
   that the interior was transient-free or uniquely identify the disturbance.
6. Produce the orientation-independent OAG geometry receipt for the same train.
7. Every FITS file must retain a current WCS whose measured pixel scale agrees
   with the hash-bound optical-train policy within its preregistered tolerance.
8. Run `tools/analyze_actual_exposure_bracket.ps1` offline. It copies source
   FITS files, runs hash-pinned ASTAP `-extract2` only on the copies, validates
   the exact seven-column catalog schema, and binds every artifact SHA-256.
9. Recompute the deterministic receipt with
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
