# Flexure, Geometry, and Refraction Campaign

## Purpose

This campaign separates support motion, internal mount/OTA/camera flexure,
time-dependent settling, TPPA arc geometry, and atmospheric refraction before
any estimator correction is considered. It does not authorize UPAS motion and
it does not claim sub-arcminute absolute accuracy.

The plan was reviewed on 2026-07-27 by Claude Opus 5 High and Gemini 3.1 Pro
High against canonical root `C:\Dev\upas-nina-tppa-plugin` at commit
`f23688dea6706d30023677befc88ea0c4eb38a98`. Claude's two substantive MCP calls
reached the 300-second transport ceiling; the same pinned read-only seat was
completed through the direct CLI fallback with safe plan/no-tools flags.

## Accepted Interpretation

The current evidence is compatible with a field-dependent combination of
mechanical motion, geometry, and refraction, but does not separate them. A
same-arc signed-azimuth walk of about 75 arcseconds over 24 minutes is larger
than the expected correction from replacing standard-atmosphere defaults with
a representative Dubai-summer snapshot. True-pole versus refracted-pole
semantics can produce a larger nearly constant offset, but do not by themselves
explain a monotonic short-term walk.

The current iOptron carbon tripod, even with retracted legs, visibly wobbles as
RA/DEC center of mass changes. The Celestron heavy-duty CPC tripod is therefore
a useful controlled comparator. Reinstallation changes absolute PA, so tripod
performance must be compared with offset-invariant metrics, not the absolute
TPPA value.

## Invariants

- No UPAS movement during any diagnostic block.
- No code, atmosphere-source, tripod, payload, cable, balance, or estimator
  change inside a block.
- Keep telescope commands inside AZ 270..360 or 0..010 and ALT 25..55 degrees;
  prefer ALT 35..50.
- Lock or remove the P20 flex arm before settling. Record any physical contact
  as a disturbance and restart the affected block.
- Preserve exact solved coordinates, observation midpoint UTC, achieved arc
  geometry, tracking state, pier side, rotator angle, atmosphere inputs and
  source, focus/solver quality, NINA/plugin versions, commit, and DLL hash.
- Treat PHD2 PDA/DA and iPolar as passive witnesses until separately qualified.

## Phase 0: Coordinate and Refraction Conformance

Complete this offline before changing production estimator math:

1. Prove the coordinate frame and epoch returned by every supported plate
   solver and consumed by `Coordinates.Transform`. Record whether coordinates
   are astrometric or apparent and whether precession, nutation, aberration, and
   refraction have already been applied.
2. Verify pressure, temperature, relative-humidity, and wavelength units at the
   NINA boundary. Test hPa versus Pa, Celsius versus kelvin, RH fraction versus
   percent, and micrometres versus nanometres with monotonic synthetic cases.
3. Compare the current transform on ALT 25..55 and representative Dubai
   atmospheres against SOFA/ERFA and an independently implemented published
   Bennett/Saemundsson calculation. Astropy and ERFA alone are not independent
   references. Require sub-arcsecond agreement before calling the convention
   proved.
4. Verify true-pole and refracted-pole targets separately. `RefractionAdjustment`
   changes the target-pole interpretation; it is not a simple raw-refraction
   on/off switch because solved positions are still transformed with atmosphere
   parameters.
5. Add only provenance and shadow calculations until these tests close. Do not
   patch a measured field offset into production math.

## Phase 1: Carbon Stability Baseline

Use the carbon tripod in its current configuration before reseating anything:

1. Mark feet, leg azimuths, head orientation, payload/cable routing, balance,
   and phone-arm state. Use identical imaging payload for every later arm.
2. Allow at least 30 minutes after the final touch for thermal/mechanical
   settling. Record station pressure, ambient temperature, and RH at start,
   midpoint, and end without connecting NINA Weather or Safety devices.
3. Run at least 20 fresh same-geometry TPPA determinations over at least 40
   minutes on arc A. Do not use the continuous overlay as a measurement.
4. Record an independent mechanical witness. Preferred options are a mount-body
   laser on a distant scale, a fixed high-resolution inclinometer, or an iPolar
   repeated-frame centroid series. A P20 time-lapse may record the witness, but
   UPAS scale images alone cannot reveal tripod tilt.
5. Compute signed-component Theil-Sen slope with bootstrap 95 percent CI,
   successive-difference scatter, Allan deviation at 2/4/8/16 minutes, and
   current median/MAD plus trend-span gates.

A research stability target is a 95 percent slope CI contained within
+/-0.5 arcsecond/minute over a >=40-minute block. This is not yet a production
movement gate. The existing A-return and trend gates remain authoritative.

## Phase 2: Load and Return Hysteresis

On the unchanged support, choose two safe pointings that produce meaningfully
different RA/DEC load vectors. Determine a fixed settle dwell with a one-time
5/10/20/40-second ladder, then use twice the observed knee.

Run counterbalanced `A-B-B-A` cycles, reverse the leading direction in alternate
cycles, and collect at least three complete cycles. Repeat with any safe rotator
180-degree reversal if it changes camera/OTA load without cable risk. Compare:

- A1-to-A2 signed-vector closure;
- B1-to-B2 closure;
- forward versus reverse sweep hysteresis;
- mechanical-witness displacement;
- temporal slope before and after the load excursion.

A load-correlated TPPA shift with matching mount-body witness motion supports
support flexure. A TPPA shift without mount-body motion points toward internal
mount/OTA/camera flexure, atmosphere, or estimator geometry.

## Phase 3: Carbon Re-seat and CPC Crossover

Use three support states:

- `C`: carbon tripod as currently assembled;
- `C-prime`: carbon tripod disassembled, re-seated, and torque-controlled;
- `H`: Celestron heavy-duty CPC tripod.

Absolute PA is a nuisance intercept and must not be compared across states.
Compare only temporal slopes, Allan deviation, same-state scatter, load-return
hysteresis, settle time, and mechanical-witness displacement.

Use a two-night crossover when possible: `C-H-C` on one night and `H-C-H` on
the next, with the leading order randomized and recorded. Keep payload, height
where practicable, foot surface, cable routing, leg extension, and settle time
constant. If CPC and `C-prime` are indistinguishable, attribute the improvement
to re-seating/torque rather than tripod type.

Call CPC superior only when, on at least two nights:

- paired drift-slope improvement has a 95 percent CI excluding zero and a
  practically material reduction;
- load-return hysteresis is reduced by at least 50 percent with uncertainty
  excluding zero;
- same-state scatter does not regress; and
- the independent mechanical witness agrees in sign and timing.

Otherwise report `not distinguished`.

## Phase 4: Geometry Bias

After mechanical stability passes, run three fixed geometries on unchanged
hardware and atmosphere:

1. narrow safe arc at mean ALT about 38 degrees;
2. widest safe, well-conditioned arc at the same mean altitude;
3. the same wide span at mean ALT about 48 degrees.

Interleave them as `A-B-C-C-B-A` and repeat. Also reverse the three-point order.
Log achieved angular span and a numerical conditioning metric rather than
assuming commanded geometry was achieved. Use A-return closure to remove time
trend from field comparison.

Safe production work is limited to geometry metadata, poor-conditioning
warnings/rejection, and fixed validated arc selection. A joint common-pole fit,
N-point weighted fit, span extrapolation, or field-nuisance regression remains
shadow mode until signed dependence repeats across nights and held-out arcs.

## Phase 5: Refraction Bias

Use a fail-closed session atmosphere snapshot with station pressure in hPa,
ambient temperature in Celsius, RH with explicit units, effective wavelength in
micrometres, sensor identity, UTC, uncertainty, and source. Do not silently use
sea-level-reduced pressure as station pressure. A missing or stale snapshot
invalidates absolute/refraction claims but may still permit repeatability work.

Counterbalance wide-arc runs at ALT about 38 and 48 degrees while atmosphere is
stable. Compute current-default and measured-atmosphere results in shadow mode,
plus both true-pole and refracted-pole targets. Require the predicted
altitude-dependence and observed signed change to agree across multiple nights
before compensation is considered.

Local building-plume refraction cannot be eliminated in software. Bound it by
repeating high-altitude blocks, avoiding the lowest window when possible,
logging atmosphere trends, and rejecting nonstationary or arc-dependent nights.

## Claim Gates

- `Eliminated`: a maximally exciting manipulation yields a null whose 95 percent
  upper bound is below the declared threshold on at least two nights.
- `Bounded`: the measured effect and its confidence interval agree in sign and
  approximate magnitude with an independent physical prediction.
- `Compensated`: mechanism-level model, independent oracle, held-out
  night/geometry validation, shadow-mode improvement, external corroboration,
  and rollback are all present.

Even after geometry and standard refraction are bounded, anomalous local
refraction and the absence of a qualified independent absolute witness limit
absolute-accuracy claims. Repeatability and transfer consistency must not be
reported as absolute accuracy.

## Immediate Next Session

1. Keep the carbon tripod unchanged; lock/remove the phone arm.
2. Record the manual atmosphere snapshot and full provenance.
3. Run the >=40-minute carbon same-arc baseline with the mechanical witness.
4. Run three counterbalanced load-return cycles without UPAS movement.
5. Repeat the original same-arc baseline for A-return closure.
6. Analyze before installing the CPC. If witness motion follows the TPPA walk,
   proceed to the `C/C-prime/H` crossover. If it does not, prioritize coordinate
   conformance and geometry/refraction blocks before attributing the effect to
   the tripod.