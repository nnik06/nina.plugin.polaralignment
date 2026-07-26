# External UPAS supervisor client boundary

## Status

TPPA 2.2.6.35 adds the first NINA-side boundary for the external
`upas-supervisord` service. It is deliberately a readiness-and-denial
increment. It does not submit corrections and cannot enable supervisor-driven
hardware motion.

The new `RequireExternalUpasSupervisorForAutomatedMoves` setting defaults to
true. Legacy direct actuation remains available only as an explicit
compatibility opt-out. When supervisor-required mode is active, both automated
correction moves and the optional UPAS azimuth pre-seat
use the same external-supervisor executor. There is no catch-path fallback to
the legacy actuator.

## Current fail-closed behavior

The client:

1. accepts only an absolute HTTPS endpoint with no embedded credentials,
   query, or fragment;
2. obtains the client bearer from `UPAS_SUPERVISOR_CLIENT_TOKEN`;
3. sends only authenticated `GET /v1/status`;
4. parses the complete frozen status V1 top-level and capability property set;
5. rejects unknown schema versions, modes, properties, axes, invalid session
   IDs, malformed lease budgets, locks, and unavailable axis capabilities;
6. rejects `physicalMotion=false` without invoking local motion;
7. still rejects a future `physicalMotion=true` response because correction
   submission is not commissioned in this build.

A dry-run `VERIFIED` outcome is never represented as physical movement because
this client does not call `/v1/corrections`.

## Threat model

The protected failure is an automated caller bypassing P20-witnessed,
single-authority motion after the external mode has been selected. The first
boundary therefore routes both known automated entry points through one mode
selector:

- pre-seat before the initial TPPA measurement;
- X and Y movement from `TPAPAVM.MoveCloser`.

The supervisor-required executor owns no legacy mover reference. Service,
authentication, parsing, capability, and cancellation failures return denial
or propagate cancellation. They cannot select the direct mover.

Manual UI nudge and absolute-move controls remain a separate attended legacy
surface in this increment. They are not represented as supervisor-authorized
and must not be used concurrently with a future automated supervisor lease.
Before physical supervisor authority is enabled, composition must make those
surfaces mutually exclusive with an active lease and remove raw transport
bypasses from normal operation.

## Required next increment before motion

Do not add physical motion merely because the status service later reports a
new capability. A separately reviewed transaction client must first provide:

- durable idempotency intent persisted before submission;
- RFC 8785 canonical request hashing;
- sole client lease acquisition and ownership checks;
- one-axis request construction in
  `azEastPositive_altUpPositive` coordinates;
- sequential X then Y transactions, never parallel;
- bounded polling that accepts only a terminal `VERIFIED` transaction;
- pre/post P20 witness and same-connection controller-receipt validation;
- restart recovery using the original idempotency key;
- unresolved-transaction poisoning after ambiguous cancellation or timeout;
- honest partial completion when X verifies and Y is denied;
- remeasurement before any new plan;
- mutual exclusion between attended manual motion and the automated lease.

Until all of those are implemented, reviewed, and HIL-qualified, the final
line of `SupervisorRequiredAutomatedMoveExecutor.ExecuteAsync` remains an
unconditional commissioning denial.
