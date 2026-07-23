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
3. Run no-motion A-B-C-A acquisitions and compare five-, eight-, and
