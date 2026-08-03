# UPAS Supervisor Coarse Success Transport Checkpoint

Date: 2026-08-03

## Scope

This checkpoint implements and verifies the non-moving schema-2 success
transport between the UPAS supervisor and TPPA. It does not commission live
physical-position evidence, response calibration, budget reservation, a motion
transaction, or any actuator command.

The reviewed repositories before implementation were:

- TPPA: `C:\Dev\upas-nina-tppa-plugin` at
  `561ca6310221d73d7710bd9ef4776533e9bd4867`;
- supervisor: `C:\Dev\upas-polar-align` at
  `16679b1ed2bce41e681fba8e293828c58a899b80`.

Claude Opus 5 High and Gemini 3.1 Pro High both approved an optional atomic
provider boundary. Claude's direct read-only CLI fallback was used after two
MCP responses stopped at an intent to inspect. Gemini completed through MCP.

## Implemented Boundary

The supervisor now has:

- a closed five-reason internal unavailability taxonomy;
- an immutable deep-frozen provider snapshot;
- an optional commissioned evidence provider;
- an independent optional commissioned-digest verifier;
- production denial unless both dependencies are supplied deliberately;
- API-owned nonce binding and processing duration;
- RFC 8785 evidence identity over the payload excluding `evidenceId`;
- one exact canonical UTF-8 response byte buffer used for both SHA-256 and the
  HTTP response;
- compiled safety cross-checks at hard limits `[-5.4,+5.4]`, independent
  reserve `1.0`, and automatic planning limits `[-4.4,+4.4]`; and
- byte-identical fallback to the existing strict HTTP 409 denial for absence,
  unavailability, provider faults, verifier refusal, and invalid snapshots.

The production CLI composition root injects neither provider nor verifier.
Therefore deployment behavior remains fail-closed and no new motion authority
is reachable.

## Shared Fixture

Both repositories consume the same canonical success body and metadata:

- body SHA-256:
  `d90ba0f935859d954b3b5f5f57fa4842f6641e4c83b747334b0049575a8854d5`;
- evidence ID:
  `bc965cda8c4513d5c3f63613ae1046f91d335bcdae6d0b80bc4bf481f867dd01`;
- body length: `3948` bytes;
- request nonce: 64 lowercase `a` characters;
- synthetic request/response monotonic interval: `25 ms`.

TPPA verifies the exact received-body digest, authenticated evidence-ID header,
strict schema-2 parser, and `motionAuthorityIncluded=false` against that shared
fixture.

## Verification

- supervisor focused coarse-evidence tests: `15 passed`;
- supervisor broad suite: `1047 passed, 2 skipped, 4 failed`;
- TPPA full suite: `874 passed`;
- Ruff format/check on changed supervisor Python files: passed.

The four supervisor failures are the same unrelated dirty Android source-canary
length/hash mismatches present before this slice. No coarse-evidence test failed.

## Remaining Commissioning Work

The success transport is not production authority. A future provider must still
bind, in one coherent epoch:

1. signed Hall/P20 physical positions and covariance;
2. commissioned full 2x2 sky-response calibration and covariance;
3. direction, deadband, command uncertainty, temperature and load
   applicability;
4. directional and cumulative travel budgets; and
5. an independently verified calibration artifact digest.

Only after those authorities exist and are field-qualified may production wire
both provider and verifier. Motion remains a later, separately commissioned
transaction boundary.
