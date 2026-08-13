# Fast UPAS Response Bootstrap Protocol

## Purpose

This is the one attended calibration block required before claiming the
five-minute TPPA fast route. It measures the actuator response which the
correction controller needs; it is not a polar-alignment performance attempt.

The fast route may move only when it has either a qualified measured 2x2
response or a measured X response plus a physically bounded Y bootstrap.
That admission is enforced by commit `e13e2e5`.

## Preconditions

1. NINA has loaded the hash-verified plugin and the mount, camera, and UPAS
   bridge are healthy. Weather, Safety Monitor, and flat panel remain
   disconnected.
   Launch NINA from the logged-in interactive desktop (for example, the Mele
   NoMachine session). A `Start-Process` sent through SSH does not establish a
   usable WPF desktop session and is not a field-start recovery path.
2. Confirm the signed physical AZ and ALT marker positions and usable headroom
   from current P20 evidence. Never use GRBL MPos as physical position after a
   reset.
3. Extract the effective settings by merging NINA user overrides with plugin
   defaults. The direct-route flag and +/-5.4 degree travel guard values alone
   do not qualify a response route.
4. Use fixed refraction settings, a qualified three-point arc, and two agreeing
   fresh baseline determinations.

## Evidence To Preserve

Every accepted response sample now logs `TPPA_UPAS_FRESH_RESPONSE_SAMPLE` with
the command, observed axis deltas, fitted local 2x2 response when qualified,
probe-rejection counts, and the next bounded probe size. A sub-floor probe also
logs `TPPA_UPAS_PROBE_REJECTED`. These records are report-only: use them to
populate or review the calibrated response fields after an attended block, but
never treat a missing log record as authority to bypass a physical motion guard.

## Attended X Measurement

1. Select an X-only command bounded by the current signed marker headroom.
   Record the physical scale frame before and after it.
2. Settle, then take a fresh three-point determination. Record the signed
   changes in both TPPA axes per X command unit.
3. Reject the result on inconsistent solves, wrong sign, insufficient response,
   regression, or a physical-scale ambiguity. Do not compensate with an
   opposite command merely to make the record look symmetric.
4. Store the accepted signed X response as
   `AvalonCalibratedAzimuthDeltaPerXUnit` and
   `AvalonCalibratedAltitudeDeltaPerXUnit`. Store the separately observed
   physical AZ travel-per-unit and safe maximum X command.

## Bounded Y Bootstrap

1. Measure the physical ALT scale displacement for a safe attended Y command
   and establish a conservative positive
   `AvalonAltitudeDegreesPerNudgeUnit`. Configure signed starting position,
   +/-5.4 degree envelope, and a maximum Y command of at least the fixed
   20-unit bootstrap probe while retaining two-sided headroom.
2. Restart the fast route. With X qualified but the 2x2 incomplete, it may
   issue only the bounded Y probe. It must not issue a computed diagonal.
3. Take the mandatory fresh three-point response. Accept Y only if the response
   is independent and non-regressing; otherwise the controller latches motion
   off without an automatic reversal.
4. Record the signed Y response column, cross terms, backlash direction, and
   timing. The first run is successful if it establishes the response safely,
   even if it does not reach the 3-arcminute alignment target.

## Performance Run

Run only after the calibration values are present and a new baseline pair
agrees. The imaging-critical objective is <=3 arcminutes on two independent
fresh determinations within 300 seconds. The ordinary uncalibrated direct
route remains limited to <=120 arcminutes; the attended first-run or measured
2x2 direct route is the path that may admit the physical +/-5.4 degree vector
and must reduce it to <=24 arcminutes without touching tripod bolts. A move
whose post-move response regresses, disagrees, or exhausts runtime stops the
run; it does not authorize another correction.
