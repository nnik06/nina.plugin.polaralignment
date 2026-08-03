# UPAS Supervisor Coarse Transaction Handoff

Date: 2026-08-03

## Provenance

- TPPA repository: `C:\Dev\upas-nina-tppa-plugin`
- TPPA checkpoint: `5fab701` (`Bind coarse plans to authenticated evidence`)
- TPPA verification: 861/861 tests passed
- Supervisor repository inspected read-only: `C:\Dev\upas-polar-align`
- Supervisor inspected checkpoint: `6244abc517939c1519dbb515b254cc607686369d`
- The supervisor worktree was extensively dirty. This handoff does not assert
  that its checkpoint contains the active uncommitted P20/iPolar work.

## Existing TPPA Contract

The exact proposed response is documented in
`docs/UPAS_SUPERVISOR_COARSE_PLANNING_EVIDENCE_V2.md`.

TPPA now has inert parsers for:

- nonce/session/boot/freshness envelope;
- signed AZ/ALT position and correlated Hall/P20 witnesses;
- full signed 2x2 sky-response calibration and covariance;
- directional deadband and fixed command uncertainty;
- directional and cumulative remaining travel budgets;
- lease, lock, transaction, and capability state;
- a non-actuating full-vector planner; and
- a deterministic planning receipt with `motionAuthorityIncluded=false`.

The compiled TPPA hard limit is +/-5.4 degrees. The current independent reserve
is 1.0 degree, so planning remains strictly inside +/-4.4 degrees. The reserve
must not be reduced as part of this integration.

## Required Supervisor Increment A: Evidence Endpoint

Add an authenticated, read-only endpoint dedicated to schema-2 coarse planning.
Do not overload frozen `/v1/status`.

Suggested request:

```text
GET /v1/coarse-planning-evidence?nonce=<64-lowercase-hex>
Authorization: Bearer <client token>
Accept: application/json
```

Required transport behavior:

1. HTTPS only; no redirect.
2. Authenticate the normal client bearer before constructing evidence.
3. Record request receipt and response capture on the supervisor monotonic clock.
4. Return the nonce byte-for-byte.
5. Emit `X-UPAS-Content-SHA256` over the exact UTF-8 response bytes.
6. Emit `X-UPAS-Evidence-ID` equal to body `evidenceId`.
7. Define and share the exact canonicalization implementation used to derive
   `evidenceId`; the current prose requires RFC 8785 and must not be weakened to
   an unanchored sorted-JSON hash.
8. Disable response compression for the first interoperable fixture so both
   repositories bind the same received representation unambiguously.

The response must be one atomic snapshot. Do not assemble axes, calibration,
lease, budgets, or capabilities through separate unlocked reads.

## Required Supervisor Increment B: Atomic Reservation

The evidence budgets are advisory. Add a non-moving reservation operation that
atomically rechecks:

- evidence/session/boot/nonce and evidence-body hash;
- caller-owned lease identity;
- no active transaction or lock;
- current witnessed positions and uncertainty;
- response-calibration identity, applicability, and artifact hash;
- requested directional and cumulative costs;
- full uncertainty-expanded path inside +/-4.4 and +/-5.4 degrees; and
- no change in the physical epoch since evidence capture.

Reservation failure performs zero motion. A reservation has a short relative
lifetime, is single-use, and is durably recoverable by idempotency key.

## Required Supervisor Increment C: Sequential Transactions

The committed supervisor schema-1 `CorrectionIntent` is insufficient for the
coarse path: it is scalar, capped by `maximumCorrectionDegrees=0.5`, and does
not bind the planning evidence or expanded path.

Create a separately versioned coarse intent rather than widening schema 1.
Each intent represents exactly one axis and binds:

- client, lease, reservation, idempotency, and plan-receipt identities;
- supervisor evidence ID and exact response-content SHA-256;
- calibration ID and artifact SHA-256;
- TPPA measurement/session identity and error-vector digest;
- axis and signed requested physical delta;
- model uncertainty, fixed-command uncertainty, and applicable deadband;
- integer-millidegree expanded path and directional/cumulative costs;
- required coordinate convention; and
- the prior verified transaction ID for the second axis.

Execute AZ and ALT sequentially in the order sealed by the reservation. Never
run both axes concurrently. After the first verified axis, a denial of the
second is an honest partial completion and requires fresh TPPA measurement;
there is no blind rollback.

## Terminal Receipt Requirements

Only terminal `VERIFIED` may be interpreted as physical completion. The durable
receipt must bind:

- the exact accepted request and idempotency digest;
- pre/post Hall and P20 artifacts plus calibration/provenance;
- same-connection controller acknowledgement and two stable `Idle` reports;
- commanded and achieved signed delta with uncertainty;
- consumed directional/cumulative budgets;
- resulting signed physical position and expanded hard-limit margin;
- supervisor session/boot/lease/transaction identities; and
- complete legal transition history.

Timeout, cancellation after submission, restart ambiguity, missing post witness,
or uncertain controller outcome poisons the epoch. The client may poll the same
idempotency key; it may not resubmit motion as a new request.

## Shared Fixtures And Tests

Publish one byte-identical valid schema-2 response fixture and one mutation per
rejection rule. Both repositories must independently parse the same fixture and
agree on response SHA-256 and canonical `evidenceId`.

Required adversarial cases include:

- altered body under unchanged transport digest;
- altered evidence ID or nonce;
- cross-session, cross-boot, or cross-calibration splicing;
- stale Hall/P20 evidence or stale response calibration;
- correlated witnesses falsely counted as independent;
- depleted directional or cumulative budget;
- endpoint equality after outward millidegree rounding;
- foreign lease, active transaction, and any lock;
- reservation replay, expiry, restart, and idempotency conflict;
- X verified/Y denied partial completion; and
- proof that every denial performs zero controller writes.

## Commissioning Boundary

Do not deploy or enable physical coarse motion after unit/integration tests alone.
The next gate is dry-run interoperability using the exact two repository
checkpoints and shared fixtures. Physical commissioning then requires attended
P20/Hall evidence, one small one-axis command, durable receipt verification,
fresh stationary TPPA response measurement, and explicit review of the result.

This handoff advances the 0--5 degree campaign but grants no motion authority
and makes no absolute polar-accuracy claim.
