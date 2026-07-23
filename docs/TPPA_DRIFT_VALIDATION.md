# TPPA Drift Validation

## Status

This feature is an experimental, report-only absolute-accuracy check. It does
not issue UPAS commands and must not be used as a completion condition until
simulation and field validation establish reliable confidence-interval
coverage.

## Measurement

Acquire four stationary tracks using the TPPA positions:

1. Position A
2. Position B
3. Position C
4. Return to position A

Each track must:

- remain at or above 30 degrees altitude;
- begin after 20-30 seconds of post-slew settling;
- span at least 300 seconds;
- contain at least 30 timestamped plate solves;
- report the fitted declination drift and its slope uncertainty;
- record hour angle at the track midpoint;
- record the computed apparent-declination drift caused by refraction.

Five minutes is the experimental floor. Longer tracks should be compared
against five-minute subsets during field qualification. The acquisition must
retain raw samples so residual autocorrelation and solve-quality gates can be
added without repeating the observation.

## Model

For small polar-axis errors, differentiating the standard polar-misalignment
equations gives:

```text
d(delta)/dt = sidereal_rate * (
    -altitude_error * sin(hour_angle)
    + azimuth_error * cos(latitude) * cos(hour_angle))
```

The estimator subtracts a separately computed refraction drift from each
observed declination slope, then performs a weighted two-parameter
least-squares fit. It intentionally excludes RA drift, tracking-rate error,
periodic terms, fitted refraction, and arbitrary per-track slopes. Those terms
would either be unnecessary for the initial DEC-only model or would absorb the
polar-alignment signal in the restricted balcony geometry.

## Fail-Closed Gates

No polar-error value is valid unless all gates pass:

- four or more tracks;
- at least one revisited position;
- minimum duration, cadence, altitude, and slope precision;
- finite input and positive uncertainties;
- conditioned hour-angle geometry;
- acceptable global reduced chi-squared;
- acceptable residuals for every track at the revisited position.

The report must include both fitted components, their uncertainties, total
error, reduced chi-squared, design condition number, and the maximum
revisited-position standardized residual.

## Qualification Plan

1. Expand the seeded tests into Monte Carlo coverage tests with correlated
   noise, changing refraction, and position-dependent disturbances.
2. Add raw plate-solve track fitting with outlier and residual-autocorrelation
   gates.
3. Wire the report-only acquisition session into TPPA's capture/solve path.
4. Run no-motion A-B-C-A acquisitions and compare five-, eight-, and
   ten-minute track fits.
5. Compare repeated sessions on unchanged hardware and require agreement in
   both fitted magnitude and direction.
6. Compare qualified sessions against independent drift measurements, while
   keeping those measurements advisory until their own repeatability is
   demonstrated.

## Acquisition Session

`TppaDriftValidationSession` implements the report-only state boundary for one
ordered A-B-C-A acquisition. It:

- enforces the required position order;
- accepts only finite declinations with strictly increasing UTC observation
  times;
- retains every raw solve rather than only fitted slopes;
- qualifies each stationary track through
  `TppaDeclinationDriftTrackEstimator`;
- refuses global validation when any track fails or when computed refraction
  drift metadata is absent;
- contains no telescope, UPAS, or actuator dependency.

Completing the four-track acquisition and obtaining a valid polar-error fit
are separate states. A completed acquisition can still produce a rejected
validation report, and rejection must never be converted into an actuator
command.

`TppaRefractionDriftCalculator` now supplies the candidate per-track
refraction term by simulating the plate-solved declination through NINA's
apparent/vacuum coordinate transforms. It validates site, atmosphere, UTC,
altitude, and centered-transform closure before returning a value.

The next integration step is to feed exposure-midpoint timestamps and solved
declinations from the existing TPPA solve path into the acquisition session
and attach the calculator result to each track. The runtime path must reject a
track when this calculation is unavailable; it must not guess, fit away, or
silently replace refraction drift with zero.
