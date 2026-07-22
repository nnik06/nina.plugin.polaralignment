# Automated PHD2-Style Polar Drift Alignment for UPAS

## Decision

The Claude Fable 5 High and Gemini 3.1 Pro High council selected a companion estimator using stock PHD2 GuideStep events. A private PHD2 fork is not required for the first implementation.

PHD2 Polar Drift Align fits guide-star camera X/Y position against time. With guide output disabled, GuideStep dx/dy slopes are equivalent because the fixed lock-position offset cancels from a linear regression.

The first implementation is deliberately passive. It estimates total polar error and camera-frame pole direction but cannot command UPAS.

## Equations

For camera slopes sx and sy in pixels per second:

- offset_px = sqrt(sx^2 + sy^2) * 86400 / (2*pi)
- total_error_arcmin = offset_px * pixel_scale_arcsec_per_pixel / 60
- theta = atan2(sy, sx)
- alpha = theta + hemisphere * 90deg * mirror (the camera-space vector from the current star to PHD2's target)
- phd2_display_angle = normalize(-alpha)

The implementation uses the same 24-hour factor as PHD2 polardrift_toolwin.cpp.

## Stability gates

Default field eligibility requires:

- at least 600 seconds,
- at least 200 samples,
- propagated total-error sigma no greater than 0.25 arcmin,
- first-half versus second-half vector-slope disagreement no greater than max(0.5 arcmin, 35% of measured total error),
- finite positive PHD2 pixel scale,
- no guide pulses,
- no star loss, dither, lock-position change/loss, or guiding stop/restart event.

A stable result establishes drift-derived total polar error. It does not yet establish a safe UPAS X/Y command because camera orientation, parity, atmospheric refraction, and actuator response must be handled separately.

## Implemented phase

- PolarAlignment/PolarDriftEstimator.cs: pure incremental capture estimator and stability verdict.
- NINA.Plugins.PolarAlignment.Test/PolarDriftEstimatorTest.cs: PHD2-equation, target/display direction oracles for hemisphere and mirror, offset-invariance, curvature, duration, invalidation, and sample-order tests.
- tools/tppa_phd2_supervisor.ps1: requires a tracked, stationary field within 6 degrees of the selected celestial pole; captures camera dx/dy and pixel scale; preserves PHD2 exposure by default; fails closed on guide pulses or any guide-star, lock-position, dither, or guiding-state discontinuity; quarantines rejected CSVs; retries only bounded transient star-loss failures with a fresh star; and invokes the analyzer after every valid capture.
- tools/analyze_tppa_phd2_diagnostics.ps1: writes a read-only phd2-polar-drift-results.json artifact plus CSV and Markdown summaries, with the same magnitude, uncertainty, direction, and stability rules.

## Next phases

1. Collect new field captures containing camera dx/dy; historical captures without those columns remain explicitly unavailable.
2. Replay real field CSVs through the estimator and tune uncertainty/nonstationarity thresholds.
3. Require stable passive drift as an optional completion validation gate.
4. Commission a camera-to-UPAS axis map using attended bounded X/Y probes.
5. Only then permit one damped, travel-guarded UPAS trim followed by a complete new drift measurement.
6. Never perform actuator movement during an active drift fit and never trust GRBL MPos as physical feedback.

## Council disagreement resolved conservatively

Gemini proposed short 20-45 second control windows and orientation calibration from a TPPA vector. This is not adopted because it would be vulnerable to seeing and periodic error and would partially share TPPA systematics. Field captures remain 10-15 minutes until real data supports a shorter statistically qualified window.