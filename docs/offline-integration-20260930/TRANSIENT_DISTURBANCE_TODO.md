# Transient disturbances: bounded recovery todo

Status: planned; no implementation, live qualification, or motion authorization supplied by this list. The current offline integration session has used all three correction rounds. These items are future work, not a fourth correction round.

Practical outcome: an attended alignment pauses during a temporary disturbance and can continue after a bounded stationary recovery check. It must not remain permanently inhibited merely because a gust has passed. Recovery itself makes no corrective move, changes no reference, and replays no command.

## Observed behavior and limits

The current role policy rejects stale, unhealthy, unsupported, or disagreeing ALT witnesses. Its conservative position interval trips at the guard. MovementWitness latches a failed witness for that witness instance. The offline supervisor latches an UNCERTAIN job or unresolved cancellation; it has no recovery endpoint. Its encoder observation history also latches contract errors. These are distinct mechanisms and need explicit cause classification rather than a blanket reset.

A recovered sensor condition is not proof that an interrupted command was reconciled. A wind gust may cause acceleration without lasting displacement; a bird's load can also cause real flex or tilt. Sensor agreement alone cannot distinguish every such case. Persistent load, a real guard crossing, or uncertain actuation may legitimately keep alignment stopped.

Source evidence: `C:/tmp/upas-i-20260930/enc/src/upas_encoders/commissioned/role_policy.py`, `C:/tmp/upas-i-20260930/enc/src/upas_encoders/interface/contract_b.py`, `C:/tmp/upas-i-20260930/sup/src/upas_supervisord/encoder_contract_consumer.py`, and `C:/tmp/upas-i-20260930/sup/src/upas_supervisord/integration_jobs.py`.

## Todo

- [ ] Encoder layer: report current validity, disturbance evidence, and stable-window evidence separately. Include fresh source identity/timestamps, acceleration validity, position intervals, and agreement. Do not claim to identify wind or birds from these signals alone.
- [ ] Characterize stationary noise and oscillations with bounded synthetic cases, followed by separately authorized field observations. Choose finite entry/recovery thresholds, minimum dwell/sample count, and maximum recovery wait from that evidence. Do not invent a universal settling time or raise existing travel/freshness limits to hide disturbances.
- [ ] Supervisor: distinguish a recoverable observation inhibit from guard trips, controller/transport uncertainty, reference discontinuity, and unresolved motion. A temporary bad observation may inhibit dispatch immediately; its classification must not erase a genuine safety fault.
- [ ] Supervisor: for an observation-only inhibit before dispatch, automatically reassess while stationary. Re-arm only after a continuous qualified stability interval, sufficient distinct fresh windows, healthy required witnesses, agreement within established uncertainty, unchanged identities/references, verified controller Idle, and safe margins for the proposed command. Use recovery hysteresis to avoid rapid stop/start oscillation.
- [ ] Supervisor: if disturbance occurs during motion, issue the existing owned stop and reconcile delivery, charges, actual displacement, both new strict Idle observations, and fresh post-stop witnesses. End that job; never resume its remaining command automatically. Unresolved motion remains latched and cannot be cleared by calm readings alone.
- [ ] Supervisor: after a fully reconciled stop, reassess the current position and retained travel margin without resetting counters, homing, changing calibration, or silently moving the reference. A proven guard trip or other fault requires its own recovery policy; do not relabel it a gust solely because readings later improve.
- [ ] TPPA: expose a clear waiting-for-stability state with reason and bounded timeout. After recovery or any interrupted motion, obtain new settled sky geometry before submitting a newly identified correction through normal admission. Preserve the old job's terminal result and idempotency record.
- [ ] End-to-end tests: passing gust before dispatch; gust during a command; damped and recurring oscillation; persistent bird load/tilt; Hall/gravity disagreement; common-mode apparently healthy tilt; stale/replayed data; real near-limit crossing; uncertain ACK/cancel; controller restart. Verify no corrective motion during recovery, no duplicate writes, no automatic clearance of uncertainty, and finite termination.
- [ ] Acceptance: transient pre-dispatch disturbance clears automatically after qualified stationary stability; a reconciled interrupted job can be followed by a fresh admitted job; persistent disturbance produces a finite informative stop. Preserve existing physical vetoes and uncertainty reserves. Deliver remaining limitations if the bounded qualification cannot establish recovery.

Function ownership: encoders describe evidence; the motion supervisor owns inhibit, stop, reconciliation and admission; TPPA owns alignment progress and fresh sky corrections. P20 is not an operational dependency. Any optional external optical recording remains a separate qualification activity.
