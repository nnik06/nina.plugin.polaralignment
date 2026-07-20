# TPPA / UPAS Council Review - 2026-07-20

## Reviewers

- Claude Code: Claude Fable 5, high effort
- Antigravity CLI: Gemini 3.1 Pro (High)
- Private branch disclosure was explicitly authorized by the user.

## Infrastructure findings

Both CLIs were smoke-tested before review. Claude resolved to claude-fable-5; Antigravity resolved to Gemini 3.1 Pro.

Antigravity can retain an older project independently of the process working directory. Every future repository review must therefore:

1. Invoke the PTY wrapper with --new-project --add-dir "<canonical-root>".
2. Require the reviewer to print and validate git rev-parse --show-toplevel and git rev-parse --short HEAD.
3. Reject a review when either value differs from the expected canonical root or checkpoint.

Canonical repository for this project: C:\Dev\upas-nina-tppa-plugin.

## Shared conclusions

- A forward / reciprocal / repeated-forward nine-solve diagnostic is safe to field-test in Verification Only mode because it sends no UPAS actuator commands.
- It tests short-term internal repeatability and direction-dependent bias; it cannot establish absolute polar-alignment accuracy.
- Independent drift validation remains necessary. PHD2 Polar Drift Align is suitable for attended validation, while normal PHD2 JSON-RPC does not expose complete PDA start/stop/result automation.
- The historical continuous-estimator result must never be accepted as completion truth without fresh three-point confirmation.

## Claude findings adopted

- Do not mutate the persisted, UI-bound EastDirection property during reciprocal measurement.
- Do not discard successful diagnostic measurements solely because the final pointing restoration failed.
- Compare signed azimuth/altitude vectors geometrically rather than combining separate component and total-magnitude gates.
- Compare the reciprocal result with the midpoint of the two forward results to cancel first-order linear drift.
- When two below-tolerance fresh measurements disagree, take one stationary fresh tie-breaker before failing closed.
- Capture sweep direction once for all fresh confirmation, feedback, and tie-breaker arcs.

## Gemini findings

After correcting its stale OneDrive project binding and verifying root C:\Dev\upas-nina-tppa-plugin at HEAD c0f5f08, Gemini independently approved the implemented changes. Its earlier review of the obsolete OneDrive tree was rejected and is not evidence.

## Implemented after council

- Immutable direction override threaded through verification and fresh-measurement arcs.
- Cleanup restore timeout increased from 30 to 120 seconds.
- Successful measurements survive cleanup failure; the failure is logged and shown to the operator.
- Euclidean signed-vector repeatability metric added.
- Drift-centered reciprocity metric added.
- One bounded stationary three-point completion tie-breaker added.
- Verification message schema bumped to version 2.
- Focused regression tests added.

## Remaining absolute-accuracy work

1. Field-test Verification Only on stable sky and retain all nine solves.
2. Run a settled PHD2 Polar Drift Align capture near the north celestial pole for at least 12-15 minutes.
3. Compare TPPA fresh-vector mean and scatter with PHD2 drift-derived error, using consistent refraction assumptions.
4. Do not claim absolute accuracy from TPPA repeatability alone.
5. Investigate an automatable drift estimator from PHD2 star-position events or a native plate-solve declination-drift instruction; avoid GUI automation.