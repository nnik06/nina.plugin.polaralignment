# TPPA Council Audit Reconciliation - 2026-08-02

## Scope

The unanimous council `BLOCK` in
`C:\Users\nnik0\AI\claude\upas_nina_tppa_audit\SYNTHESIS.md` audited
checkpoint `8e4bf3c` while the working tree was changing. This reconciliation
checks its actionable findings against the later committed baseline
`7be2f998b348f0b54bd3144da229b996875565fb` and the 2.2.6.78 safety
hardening that followed.

Two verdicts must remain separate:

1. **Software/deployment readiness:** the audit's repository and executable-path
   blockers are closed by committed code and tests.
2. **Absolute field accuracy:** the `<1 arcmin true-pole total error in <5 min`
   claim remains **BLOCKED / UNPROVEN** until independent field evidence passes
   the existing qualification contract.

## Original Critical Findings

### C1 - Unsatisfiable five-minute movement budget: closed

The pre-move reserve is now 165 seconds: a 15-second UPAS move plus two
75-second fresh determinations. A realistic 90-second initial determination
therefore leaves 210 seconds and permits one guarded move. Positive-path,
boundary, and denial tests are in `TppaFastAlignmentExecutionBudgetTest.cs`.
The movement call site reserves the complete move/feedback/stationary tail.

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

- Focused GRBL/status tests: 40/40 passed.
- Complete NUnit suite: 636/636 passed.
- PowerShell contract suite: 132/132 passed.
- Release solution build: passed; existing package-compatibility warnings only.

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
