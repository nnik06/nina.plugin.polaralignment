# TPPA Direct Full-Travel Route

## Purpose

This is the intended attended TPPA route for the Avalon UPAS when the separate
UPAS supervisor/P20 system is unavailable or not commissioned. It is not an
absolute-metrology programme and does not require receipts, covariance
artifacts, P20 optical authority, or a sealed campaign before TPPA can align.

The operational target is a confirmed total error of at most 3 arcminutes in
five minutes. The wider fallback target is at most 24 arcminutes from within
the UPAS +/-5.4 degree software envelope.

## Evidence Required At Start

The direct route requires only information that protects a move:

1. An attended, signed physical AZ and ALT marker reading entered for the
   current session; a reset, manual adjustment, or uncertain motion invalidates
   it.
2. Configured hard bounds of -5.4 to +5.4 degrees and the measured physical
   degrees-per-command-unit scale for both axes.
3. A measured, directional TPPA response for the command sizes the route will
   use. ALT/Y additionally needs its own response and backlash evidence; an
   AZ/X response cannot stand in for it.
4. Two agreeing fresh TPPA determinations before the first correction.

The P20/supervisor route can provide stronger versions of these facts, but is
not the only route to them.

## Five-Minute Contract

The full-travel route is admitted only when
`TppaDirectFullTravelFeasibilityPolicy` proves the current calibrated residual
fits in at most three fresh-feedback moves and within 300 seconds. The route
accepts up to 324 arcminutes per axis and 458.205 arcminutes total only after
its direct calibration is enabled and confirmed; ordinary direct operation
remains limited to 120 arcminutes.

For a qualified clamp-limited recovery, the response matrix must have condition
number no greater than 5 and relative response uncertainty no greater than 10
percent. Only an inverse-command axis within five percent of its hard command
limit may consume that entire limit; an unsaturated companion axis remains
damped at 0.65. This avoids turning a one-axis recovery into an unsafe
whole-vector full-gain command.

For each admitted move, the live controller must still:

1. Derive the signed command from the current fresh TPPA residual and the
   directional measured response, then clamp it to the smaller of calibrated
   authority and physically remaining headroom.
2. Refuse a command that exceeds headroom, has unknown response sign, relies on
   stale calibration, crosses an unverified reversal/deadband condition, or
   cannot leave the reserve for its fresh feedback and terminal confirmation.
3. Wait for the existing command completion, settle, and cancellation checks.
4. Obtain a fresh three-point TPPA result before considering another move.
5. For a clamp-limited recovery, compare the first fresh result with the
   predicted response. Each material predicted error component must have a
   signed actual/predicted response ratio in [0.4, 1.6], and the actual
   residual reduction must be at least 40 percent of the predicted reduction.
   A failure latches automated motion off before another recovery move.
6. End only after two independent fresh determinations are within 3 arcminutes.

An unsuccessful response, inconsistent fresh solve, timeout, cancellation, or
travel denial ends the automatic run without authorizing another move. It must
report the last measured position/residual rather than infer success.

## Current Status

The controller integration is present as of commit `c12a03a`, but it is
disabled by default. Enabling it without a fresh 2x2 field calibration,
measured ALT/Y response, directional backlash evidence, and a stated response
uncertainty is not a qualified use of the route. The nominal full-diagonal
fixture reaches the 3-arcminute terminal target in two feedback moves and 230
seconds; that is a software proof, not field validation.

The separate supervisor remains the preferred route for unattended,
machine-witnessed physical position and broad 2x2 calibration. Its incomplete
commissioning must not silently block this attended direct route, but it also
must not be bypassed when selected as the explicit supervisor route.
