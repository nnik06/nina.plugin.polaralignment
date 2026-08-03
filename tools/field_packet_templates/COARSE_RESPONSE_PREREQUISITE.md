# Coarse Response And Travel Prerequisite

This packet accepts TPPA-reported starting errors through 300 arcminutes, but
that objective is not itself motion authority. Before any attempt above the
fine-controller envelope can move UPAS, commission the exact load profile under
the governing supervisor protocol:

`C:\Dev\upas-polar-align\docs\COARSE_RESPONSE_AND_FIVE_DEGREE_FIELD_PROTOCOL.md`

Record the supervisor repository commit and protocol SHA-256 in the calibration
ledger. The commissioned artifact must bind the hardware/controller identity,
P20 and independent position witnesses, mechanical epoch, load profile,
temperature interval, response applicability interval, and expiry.

## Required order

1. Keep the compiled +/-5.4 degree software hard limits and one-degree reserve.
2. Establish repeatable physical zero and map only safely reachable stop
   clearance, witness disagreement, command overshoot, halt distance, backlash,
   and restart/datum uncertainty. Do not touch a physical stop merely to make
   the 5-degree objective reachable.
3. With automatic correction disabled, acquire signed 2x2 response data for
   each optical train at 0.5/1.0 degrees, then 2.0 degrees, then outer-range
   amplitudes only after the preceding stage passes held-out prediction.
4. At every stage use fresh pre/post physical witnesses, one atomic supervisor
   transaction, explicit Idle and settle, and independent fresh stationary TPPA
   pairs before and after movement.
5. Preserve fit and held-out observations without reuse, deletion, or role
   reassignment. Require both signs, both axes, coupled/diagonal challenges,
   and positions spanning the claimed applicability interval.
6. Fit with `upas-coarse-response-fit`; a passing fit remains non-actuating.
   Commission it through the append-only supervisor calibration ledger before
   using it in authenticated coarse evidence.
7. Prove every admitted uncertainty-expanded path and endpoint remains inside
   the commissioned operational envelope. Starts that cannot be reached safely
   fail closed and remain failures in the sealed campaign denominator.

Do not reduce the one-degree reserve, raise the hard limit, extrapolate beyond
the commissioned response interval, or silently clamp a requested correction.
A future reduced-reserve policy requires a new evidence schema, measured
worst-case uncertainty budget, boundary/fault-injection tests, and hardware-in-
the-loop qualification for both load profiles.

This prerequisite qualifies motion response and reachability only. It does not
establish TPPA covariance, cadence, campaign success, imaging fitness, or
traceable absolute polar-axis accuracy.