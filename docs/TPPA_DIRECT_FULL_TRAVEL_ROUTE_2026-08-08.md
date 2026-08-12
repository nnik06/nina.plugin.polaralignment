# TPPA Direct Full-Travel Route

## Purpose

This is the intended attended TPPA route for the Avalon UPAS when the separate
UPAS supervisor/P20 system is unavailable or not commissioned. It is not an
absolute-metrology programme and does not require receipts, covariance
artifacts, P20 optical authority, or a sealed campaign before TPPA can align.

The imaging-ready operational target is a confirmed total error of at most 3
arcminutes in five minutes. The wider tripod-free bulk target is at most 24
arcminutes from within the UPAS +/-5.4 degree software envelope. The selected
target is carried through route qualification: a bulk result is explicitly not
imaging ready and can never be promoted to fine merely because it lands below
3 arcminutes.

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

## NINA Field Setup

With automated Avalon adjustment enabled, the TPPA Avalon panel now exposes a
**Calibrated full route** row. Before a direct full-travel run, enter and
review the measured `AZ/X`, `AZ/Y`, `ALT/X`, and `ALT/Y` response matrix, the
per-move `Max X`/`Max Y` command authority, and the physical AZ marker range.
The regular AZ and ALT travel-guard settings remain the runtime motion limits.

Enable the route and, when a clamp-limited recovery is intended, enter the
measured relative response uncertainty and enable that recovery mode. The
enabled route plus the entered response matrix and current signed physical
marker positions are the attended admission inputs. There is no separate
checkbox attestation: it carried no measurement and must not block a field run.

Calibration values persist for review. A reset or physical adjustment still
requires the operator to enter the current signed marker positions before the
travel envelope can qualify; that is the motion-relevant session action.

## First Session With No ALT/Y Column

Do not invent an ALT/Y value. A route with a fresh, trusted X column can begin
with the existing bounded bootstrap path:

1. Enter the measured X column, physical marker bounds, X/Y per-move limits,
   and the current-session marker positions; attest the reviewed route.
2. Obtain the two agreeing fresh TPPA determinations required before motion.
3. Let TPPA issue its one bounded 20-unit Y bootstrap probe and wait for its
   fresh three-point feedback.
4. It may continue only when that response is non-regressing, materially
   observable, independent of the X column, and conditioned. Otherwise the
   run stops without a compensating guess or a further UPAS command.
5. A qualified probe supplies a session-local 2x2 model for the remaining
   bounded feedback moves. It is not silently promoted to a persistent field
   calibration; preserve the run log and promote the measured values only
   after reviewing their response and uncertainty.

## Five-Minute Contract

The full-travel route is admitted only when
`TppaDirectFullTravelFeasibilityPolicy` proves the current calibrated residual
fits the selected 3-arcminute fine or 24-arcminute coarse terminal target in at
most three fresh-feedback moves and within 300 seconds. The route accepts up to
324 arcminutes per axis and 458.205 arcminutes total only after its direct
calibration is enabled and confirmed; ordinary direct operation remains limited
to 120 arcminutes.

For a qualified clamp-limited recovery, the response matrix must have condition
number no greater than 5 and relative response uncertainty no greater than 10
percent. Only an inverse-command axis within five percent of its hard command
limit may consume that entire limit; an unsaturated companion axis remains
damped at 0.65. This avoids turning a one-axis recovery into an unsafe
whole-vector full-gain command.

Every calibrated full-travel route, including a non-recovery route, requires a
2x2 response condition number no greater than 5. A non-singular but nearly
collinear matrix is still refused because its inverse would turn modest solve
noise into an unstable UPAS command.

For each admitted move, the live controller must still:

1. Derive the signed command from the current fresh TPPA residual and the
   directional measured response, then clamp it to the smaller of calibrated
   authority and physically remaining headroom.
   Qualification evaluates the signed multi-move path, so an inward correction
   from a marker near one travel edge is permitted while an outward path is
   refused. It does not require artificial clearance in both directions.
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
6. End only after two independent fresh determinations are within the selected
   target. A 24-arcminute completion is labelled coarse/not imaging ready; a
   separate new <=3-arcminute run is required before long-exposure imaging.

An unsuccessful response, inconsistent fresh solve, timeout, cancellation, or
travel denial ends the automatic run without authorizing another move. It must
report the last measured position/residual rather than infer success.

## Current Status

The controller integration and NINA field panel are present as of commit
`92c8564`, but the route is disabled by default. Enabling it without a fresh
2x2 field calibration, measured ALT/Y response, directional backlash evidence,
and a stated response uncertainty is not a qualified use of the route. Offline
fixtures exercise both the 3-arcminute fine and 24-arcminute tripod-free coarse
tiers; that is a software proof, not field validation.

The separate supervisor remains the preferred route for unattended,
machine-witnessed physical position and broad 2x2 calibration. Its incomplete
commissioning must not silently block this attended direct route, but it also
must not be bypassed when selected as the explicit supervisor route.
