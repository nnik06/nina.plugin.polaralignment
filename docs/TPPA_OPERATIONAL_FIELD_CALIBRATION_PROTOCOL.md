# TPPA Operational Field Calibration Protocol

## Purpose

This protocol produces the measured UPAS response needed for the two operating
tiers without making the external supervisor, signed receipts, covariance
artifacts, or physical-zero provenance a prerequisite for direct TPPA motion.

- **Imaging tier:** two independent fresh TPPA determinations confirm total
  error at or below 3 arcmin within 300 seconds.
- **Tripod-free tier:** a separately labelled direct route reduces an in-range
  starting error to 24 arcmin or below. It is not an imaging-ready result.

The protocol never changes a controller gain or response matrix from a
synthetic fixture. It first measures the rig in the actual balcony load state.

## Preconditions

1. Use the direct field path: external-supervisor campaign mode remains off.
   Motion protection remains on: direct travel guards, oversized-command
   refusal, telescope envelope, cancellation, settling, and fresh-solve
   agreement.
2. Enable refraction adjustment and retain the same weather/site configuration
   throughout one response dataset.
3. Confirm the actual UPAS start position and signed headroom by the physical
   scale method in use. Treat controller coordinates after a reset as unknown.
   The direct guard envelope is `[-5.4, +5.4]` degrees on each axis.
4. Establish one repeatable, plate-solvable TPPA arc. Record the two initial
   fresh determinations, their elapsed times, and the camera/filter/exposure
   settings. Do not use a solve failure as response data.
5. Abort the current calibration attempt on a mount-envelope failure, failed
   solve, uncertain physical position, materially regressive response, or any
   cancellation. Do not issue a compensating blind move.

## Response Dataset

Each response observation is one bounded, single-axis command followed by a
settled fresh three-point TPPA determination. Record the pre/post azimuth and
altitude residual vectors, command units, physical scale position, command
direction, settle time, and solve duration.

1. **X / azimuth column:** acquire positive and negative X observations after
   taking up the known azimuth deadband. Repeat each direction enough to
   estimate gain and repeatability; include a reversal to measure backlash.
2. **Y / altitude column:** once X is trusted, use the existing bounded Y
   bootstrap probe. It is identification, not an alignment correction. Take
   positive and negative Y observations with the same fresh-before/fresh-after
   evidence and verify that the physical ALT guard remains inside the envelope.
3. **Coupling:** derive both `DeltaAz / X`, `DeltaAlt / X`, `DeltaAz / Y`, and
   `DeltaAlt / Y`; do not collapse cross-axis terms to zero without data.
4. **Repeatability and settling:** repeat a same-direction move at least five
   times across safe travel positions. Record the time at which the residual
   has stabilized sufficiently for a fresh determination. A response model
   that varies by more than the imaging target is not eligible for the fine
   tier.
5. **Saturation:** characterize only with genuine physical headroom and a
   conservative margin. The controller must see saturation as a stop, never
   integrate through it.

## Admission Decisions

The full-travel direct route may be enabled only when the field dataset gives:

- a non-singular measured 2x2 response matrix;
- direction and magnitude repeatability adequate for the claimed tier;
- measured directional deadband/backlash;
- measured physical degrees per X/Y unit and signed starting headroom;
- an observed timing budget that fits the requested moves plus fresh feedback
  and final confirmation.

Until then, direct automation remains limited to the already demonstrated
route. A missing Y response is a request for a bounded Y probe, not permission
to guess the Y gain or widen the input cap.

## End-to-End Evidence

1. Run one direct tripod-free correction from a documented in-envelope error.
   A `<=24'` result must be reported as **coarse operational alignment** and
   explicitly state that it is not imaging readiness.
2. Run the imaging path with a fresh pair before motion, bounded iterative
   corrections, and two independent fresh determinations at `<=3'` after the
   final move.
3. Validate the imaging result independently with unguided DEC drift at or
   below `0.79 arcsec/min`, or with round stars across the sensor in a real
   900-second guided narrowband sub.

## Reliability Claim

Do not call the path reliable from a single good run. Record exactly five
initiated field attempts under this protocol. Four must satisfy the complete
fresh-confirmed imaging-tier result within 300 seconds. Every failure must have
passed all motion safety gates and either left no displacement or produced a
documented no-worse restoration. Finally, one independent outcome must pass:
unguided DEC drift at or below `0.79 arcsec/min`, or a real guided 900-second
sub with round stars across the sensor. `TppaOperationalReliabilityCampaign`
is a report-only evaluator for this claim; it never authorizes a move.

The completed dataset and these outcome measurements, not a green synthetic
test, establish field readiness.
