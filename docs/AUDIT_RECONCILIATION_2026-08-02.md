# TPPA Council Audit Reconciliation - 2026-08-02

## Scope

The unanimous council `BLOCK` in
`C:\Users\nnik0\AI\claude\upas_nina_tppa_audit\20260802-033201-0043dd`
audited checkpoint `8e4bf3c` while the working tree was changing. Its
`summary.json` records `status: partial`, three substantive `BLOCK` responses,
and a synthesis artifact named `synthesis.md`, but that synthesis file is not
present. The raw response files are therefore authoritative. This
reconciliation checks their actionable findings against the later committed
baseline and the 2.2.6.80 cancellation hardening at
`02b4bb7ca8085870ea54e7c6e13b560822c20c0f`.

The verification-round synthesis at
`C:\Users\nnik0\AI\claude\upas_nina_tppa_audit_verify\SYNTHESIS_VERIFY.md`
audited the earlier `7be2f99` checkpoint. Its strongest new finding, that the
default five-minute path could authorize only one move while advertising an
18-move loop, remained present in 2.2.6.80 and was independently confirmed
against current source. Other recommendations are adjudicated below rather
than inherited wholesale.

Two verdicts must remain separate:

1. **Software/deployment readiness:** the audit's repository and executable-path
   blockers are closed by committed code and tests.
2. **Absolute field accuracy:** the `<1 arcmin true-pole total error in <5 min`
   claim remains **BLOCKED / UNPROVEN** until independent field evidence passes
   the existing qualification contract.

## Original Critical Findings

### C1 - Five-minute movement budget: closed as an explicit one-shot contract

The verification round correctly found that the default 300-second path could
never reach its advertised 18-move ceiling. Version 2.2.6.81 now declares one
fresh-feedback move for the five-minute path while preserving the historical
18-move ceiling when the operator explicitly disables the fast contract.

Before that one move, admission reserves the 15-second move and two independent
fresh determinations. Each determination is budgeted at the larger of the
75-second clean-field minimum or the complete observed pre-move runtime. Thus a
90-second pre-move cadence reserves 195 seconds and passes with 210 seconds
remaining; a cadence above approximately 95 seconds is denied before physical
movement. The post-move fresh result and stationary confirmation remain
mandatory. Automatic rollback is deliberately not added: UPAS is open-loop,
and an unverified reverse move after a solve failure could worsen the physical
state.

### C2 - Untracked QualificationCore and CLI: closed

`QualificationCore/` and `tools/TppaQualificationCli/` are tracked projects in
the solution. The plugin, test project, and CLI consume the core through
`ProjectReference`; source globs are gone. The extraction was committed
atomically in `3e466baf929c50ff29eab0341a10a49d1d1a997d`.

### C3 - CI ran no tests: closed

`bitbucket-pipelines.yml` builds the complete solution and executes the NUnit
project with `--no-build --no-restore`. PowerShell contract tests pin this CI
shape so a plugin-only build cannot silently return.

## Original High/Medium Findings

- Settle override: closed. The upper bound is 120 seconds while the actuator
  qualification floor remains 30 seconds.
- Non-finite settle comparison: closed. Both movement and verification checks
  use fail-closed comparisons.
- Agent documentation: closed. `AGENTS.md` describes the headless core, CLI,
  project-reference boundary, movement gates, and test commands.
- Physical sign/convergence coverage: materially improved by the actuator
  contract, reversal, response-model, and closed-loop controller tests. This is
  simulation evidence; the signed physical response still requires a guarded
  field perturbation before actuator authority is earned.

## Additional Safety Finding From Current-HEAD Review

Both plugin movement APIs emit GRBL `$J=` jog commands, so realtime byte `0x85`
is the correct cancellation primitive. The cancellation proof was nevertheless
too permissive: generic `Hold` and `Door` states could be accepted before motion
was fully stationary.

Version 2.2.6.78 now:

- accepts cancellation only after two 300 ms-spaced `Idle` reports;
- requires X/Y/Z to remain stable across those reports;
- rejects `Hold:1`, `Door:2`, and `Door:3` as moving transitional states; and
- fails closed if stable `Idle` cannot be established.

Version 2.2.6.80 closes the remaining offline cancellation-test gap. The
production path now uses a deterministic orchestration seam whose injected
controller sequence verifies one realtime `0x85` emission, transitional
`Run`/`Hold`/`Door` handling, two stable `Idle` observations, bounded polling,
missing-telemetry rejection, and timeout. Physical controller behavior still
requires ordinary guarded field commissioning; it is no longer an untested
software sequence.

## Verification

- Focused GRBL/status tests for 2.2.6.80: 40/40 passed.
- Focused fast-budget tests for 2.2.6.81: 23/23 passed.
- Complete NUnit suite for 2.2.6.81: 639/639 passed.
- PowerShell contract suite for 2.2.6.81: 132/132 passed.
- Release build passed through the test build; existing package-compatibility
  warnings only.

## Exact Field Package

The field candidate is TPPA 2.2.6.80 built from
`02b4bb7ca8085870ea54e7c6e13b560822c20c0f`:

- plugin SHA-256:
  `5314D26358D01046FA04EFFB6C2926CF4C424196D492E0EB44D9D80E3BDA47E2`;
- qualification-core SHA-256:
  `8EE1850924D596079740D16BF3299FD11A87494E2B6D1120DBD270F8EBD403E1`;
- runtime-manifest SHA-256:
  `7D082FC7D99B7D13E00E99EC2A5699EB3963900E4FA763F428D7B96DA7544D47`.
- guarded-installer SHA-256:
  `384A0B19B1DD7FF05F2539135D6B53B8FF77C8C44EE996BBCDE3C21415769709`;
- validator SHA-256:
  `A050F1F7B67453F0B9E0AE3AC290096C787CFD944328AC2ACB93D4B2126A60AF`.

The package was copied to
`C:\Users\nnik0\Documents\TPPA-deploy\tppa-2.2.6.80-02b4bb7` on Mele and
validated there against the manifest. It is staged only. NINA was still
running with TPPA 2.2.6.71 loaded, so activation was correctly refused.
Version 2.2.6.81 is source- and test-qualified only; it has not been packaged,
deployed, or field-qualified. Deploy only after a controlled
park/panel-close/NINA-close gate, preserve the existing installation as
rollback, and independently hash the modules loaded after restart.

## Verification-Round Adjudication

- **Accepted:** the default fast controller was one-shot in practice and
  misdescribed by its 18-move loop. Version 2.2.6.81 makes that contract
  explicit and cadence-aware.
- **Rejected:** retargeting the project to 3-5 arcminutes or deleting the
  qualification boundary. The standing project objectives are less than one
  arcminute absolute TPPA error and 30-arcsecond iPolar accuracy; lowering the
  goal would answer a different question.
- **Rejected:** the claim that the 300-second actuator budget blocks stationary
  diagnostics. The budget is armed only when actuator movement is enabled.
- **Qualified:** a 30-40 minute synchronized fixed-point experiment is the
  highest-value discriminator for the 2.73 arcsec/min walk and approximately
  two-degree iPolar/TPPA gap. Use passive main-camera solves, guide-camera
  displacement, iPolar output, mount telemetry, environment data, and exact UTC
  timestamps in one fixed mechanical state. Opposite pier-side states must be
  separate repeated epochs; the mount cannot be both parked and on both sides
  simultaneously.
- **Deferred, not deleted:** a universal physical X/Y sign convention remains
  unproven. Runtime witnessed calibration remains required until the frame
  disagreement is classified.

## Remaining Blockers To The Accuracy Goal

1. Install the exact post-commit runtime package only while NINA is stopped,
   then record loaded plugin/core path, version, SHA-256, and MVID.
2. Run the pre-registered true-pole protocol with refraction adjustment enabled,
   exact site/time/weather evidence, and no hidden coordinate reuse.
3. Demonstrate repeatable same-arc and reciprocal/alternate-arc results within
   the declared uncertainty budget.
4. Compare the final stationary result with a geometrically qualified,
   independently timed witness. TPPA or iPolar agreement with itself is not an
   absolute reference.
5. Demonstrate the complete workflow under five minutes without weakening
   settle, movement, confirmation, travel, or evidence gates.
