# Coarse Evidence Readiness Checkpoint - 2026-08-03

## Provenance

- TPPA parent checkpoint: `5a0a3f12e55801d07166c58af77c7ab3fbadb1f7`
- Supervisor repository: `C:\Dev\upas-polar-align`
- Supervisor checkpoint: `16679b1e` (`Expose fail-closed coarse evidence readiness`)
- Shared exact-byte denial fixture SHA-256:
  `7d793fe296e73c2ca3ed87057047f2299745fd842715c4fee10077a88f103470`

## Implemented Boundary

The supervisor now exposes authenticated:

```text
GET /v1/coarse-planning-evidence?nonce=<64-lowercase-hex>
Authorization: Bearer <client token>
```

At this checkpoint the endpoint returns a deterministic HTTP 409
`coarse_evidence_not_ready` response. It validates and echoes the request
nonce, sets `motionAuthorityIncluded=false`, emits `Cache-Control: no-store`,
does not emit success evidence-integrity headers, and changes no ledger,
lease, transaction, sensor, or motion state.

TPPA recognizes the exact denial as a typed commissioning failure. Malformed,
duplicate, coerced, extra-property, or nonce-mismatched denial bodies fail
closed and never materialize planning evidence.

## Missing Authorities

A schema-2 success snapshot remains forbidden until the supervisor can
atomically and authoritatively supply:

1. signed AZ/ALT physical positions;
2. correlated Hall/P20 position witnesses;
3. position covariance;
4. a full signed 2x2 sky-response calibration;
5. response-calibration covariance;
6. directional deadband and fixed command uncertainty;
7. temperature, load, direction, and position applicability;
8. directional and cumulative travel budgets; and
9. atomic budget reservation.

Existing schema-1 status, GRBL `MPos`, simulated/DTI witnesses, and unrelated
P20 optical records must not be adapted into synthetic schema-2 success data.

## Verification

- Supervisor focused endpoint suite: 3/3 passed.
- Supervisor broad suite: 1035 passed, 2 skipped, 4 unrelated dirty-tree P20
  source-canary failures.
- TPPA full suite: 873/873 passed.
- Python and C# independently verify the same exact denial fixture SHA-256.

This checkpoint authorizes no UPAS movement and does not reduce the compiled
one-degree operational reserve inside the +/-5.4-degree hard limit.
