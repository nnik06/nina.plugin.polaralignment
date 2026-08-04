# Coarse Response And Travel Prerequisite

This packet accepts TPPA-reported starting errors through 300 arcminutes, but
that objective is not itself motion authority. Before any attempt above the
fine-controller envelope can move UPAS, commission the exact load profile under
the governing supervisor protocol:

`C:\Dev\upas-polar-align\docs\COARSE_RESPONSE_AND_FIVE_DEGREE_FIELD_PROTOCOL.md`

This packet binds supervisor commit
`__SUPERVISOR_SOURCE_COMMIT__` and the embedded protocol SHA-256
`__SUPERVISOR_PROTOCOL_SHA256__`, and exact-checkpoint source archive SHA-256
`__SUPERVISOR_SOURCE_ARCHIVE_SHA256__`. Record all three values in the calibration
ledger and use only `SUPERVISOR_COARSE_RESPONSE_PROTOCOL.md` and
`UPAS_SUPERVISOR_SOURCE.zip` from this packet. The deployed supervisor must
bind this archive digest as its installation identity before any physical motion.
The commissioned artifact must bind the hardware/controller identity,
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
   Commission a canonical manifest with `upas-coarse-response-commission`,
   preserving its immutable ledger generation and canonical receipt. For a
   supersession, bind both the predecessor calibration ID and exact prior
   ledger-tip SHA-256. A fresh supervisor instance must verify the new ledger
   tip against runtime coarse evidence and the exact repository checkpoint
   before it can be used.
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
