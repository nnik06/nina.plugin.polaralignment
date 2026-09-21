# TPPA Stage 1: preserved source and behavior handoff

Owner-authorized scope: local backup, provenance comparison, two separate source
commits, full Release NUnit verification, behavior JSON export. No client work,
schema edits, remote creation, push, deployment, port access, hardware access,
removal, or archiving was performed. No runtime controller behavior was changed.
The supervisor thread owns and drafts the accepted contract A amendments.

## Backup before commits

- Repository: `C:/Dev/upas-nina-tppa-plugin`, branch `master`.
- Initial HEAD: `672b94477495e17921e67e7c87dae851f39d5d14`.
- Command: `git bundle create <path> --all`.
- Path: `C:/Users/nnik0/OneDrive/Документы/Playground/_backups/upas-nina-tppa-plugin-20260921.bundle`.
- Size: **4,104,887 bytes**.
- SHA256: `EF6681514D0942C7707BC97F8C0EA4FAF77E32AA61187E9892BED57933F5A1A1`.
- `git bundle verify <path>` succeeded, reporting complete history and 22 refs,
  including master, the isolated v145 branch, stash, and worktree HEAD refs.
- A Git bundle protects Git objects/refs, not arbitrary untracked or dirty files.
  The 15 approved dirty source files were additionally verified against the
  already committed v145 source below before editing. No claim is made that the
  bundle backs up unrelated untracked evidence or the extra uncommitted probe.

## Provenance comparison and commits

Deployed source reference:
`cb901f61b73e64a45cdcd36325d89abcad2a7329`, branch
`agent/tppa-monitored-waypoint-20260916`. Branch and worktree retained:
`C:/Users/nnik0/AppData/Local/Temp/upas-tppa-waypoint-isolated-20260916`.

Compared each of the 17 approved files by reading `git show <reference>:<path>`
and the working file, replacing CRLF with LF only. No trimming or other
whitespace normalization. Before editing: **15 identical; 2 extra probe files**.

All 15 identical paths, relative to the canonical repository:

```text
PolarAlignment/Changelog.md
PolarAlignment/Instructions/PolarAlignment.cs
PolarAlignment/NINA.Plugins.PolarAlignment.csproj
PolarAlignment/Resources/PolarAlignmentInstructionTemplate.xaml
PolarAlignment/TPAPAVM.cs
PolarAlignment/TppaMountMotionEnvelope.cs
PolarAlignment/UniversalPolarAlignmentBase.cs
PolarAlignment/TppaFreshMeasurementTimingPolicy.cs
PolarAlignment/TppaMonitoredWaypointSlew.cs
PolarAlignment/UpasSerialOwnership.cs
NINA.Plugins.PolarAlignment.Test/TppaFastPostMoveOrchestrationContractTest.cs
NINA.Plugins.PolarAlignment.Test/TppaMountMotionEnvelopeTest.cs
NINA.Plugins.PolarAlignment.Test/TppaFreshMeasurementTimingPolicyTest.cs
NINA.Plugins.PolarAlignment.Test/TppaMonitoredWaypointSlewTest.cs
NINA.Plugins.PolarAlignment.Test/TppaPostStopEnvelopeRegressionTest.cs
```

Snapshot commit: `3b877515e390d58b8822e1f94fea6e3d45926ad6`.
Message: "Preserve v145 content identical to cb901f61 on master". Its body
explicitly names the only intentional exception: correcting the stale deployment
notes in `Changelog.md`. After committing, a comparison restricted to the 15 paths
against cb901f61 shows **only Changelog.md differs** (13 additions, 5 deletions).
This is not a claim that the whole master tree equals the deployed release;
master already contains later raw-supervisor changes.

Separate probe commit: `4ce4fcc592d4aed2bbaf94aef051533fd9988685`, only:

```text
tools/UpasOwnershipProbe/Program.cs
tools/UpasOwnershipProbe/UpasOwnershipProbe.csproj
```

The probe was preserved, not run against an operational ownership lock.
The first sandboxed staging attempt failed with `index.lock: Permission denied`.
Normal reviewed escalation for the explicitly authorized paths succeeded; no
permission policy was changed and no indirect execution was used.

### Deployment record

The v145 DLL SHA256 recorded in the snapshot commit and changelog is:
`f9e4e55d799f25a830babc34d972ff26827f949edb3d4bba01035a7f2df16223`.
Evidence read, not newly generated hardware evidence:

- `C:/Dev/upas-polar-align/docs/MELE_REMOTE_COMMAND_FAILURES_AND_RECOVERY_2026-07-31.md`,
  2026-09-16 02:20-02:48 Dubai entry: isolated source, hash, installation at
  2026-09-15 22:47 UTC, and rollback location.
- `C:/Users/nnik0/OneDrive/Документы/Playground/upas_alt_inertial_20260918/evidence/balcony_20260920/commission_02_nina.log`,
  line 45: successful loading of plugin version 2.2.6.145 on September 20.

Versions 142-144 are now labeled historical work incorporated into v145; this
does not assert separate deployments of those versions. No current Mele state
was checked and no present working-tree build was deployed.

## Release NUnit results by identity

Command used serially, with different TRX filenames:

```powershell
dotnet test NINA.Plugins.PolarAlignment.Test/NINA.Plugins.PolarAlignment.Test.csproj -c Release -p:SkipLocalNinaPluginCopy=true --logger "trx;LogFileName=stage1-vectors-r2.trx" --results-directory artifacts/stage1-20260921 --verbosity quiet
```

| Run | Passed | Failed | Skipped | Total |
|---|---:|---:|---:|---:|
| Snapshot plus separate probe commit | 1175 | 0 | 0 | 1175 |
| Export-only invocation | 1 | 0 | 0 | 1 |
| First full vector replay | 1193 | 10 | 0 | 1203 |
| Corrected full vector replay | 1203 | 0 | 0 | 1203 |

Raw reports remain at `artifacts/stage1-20260921/{stage1-snapshot,stage1-export,stage1-vectors,stage1-vectors-r2}.trx`.
The first exporter build lacked `using System.IO`; corrected before export.
The 10 first-replay failures were solely new exporter tests: JSON serializes
the controller's positive-Infinity optional default as a string, whereas the
in-memory token was a float. Normalize exporter argument tokens through JSON
serialization before comparison. The frozen JSON was NOT regenerated to hide a
behavior change; controller code, expectations and numerical tolerances were
unchanged. Existing tests passed in both full runs. Known NU1701 and nullable
warnings remain.

`STAGE1_TEST_IDENTITIES_20260921.csv` lists every reported identity, before/after
instance counts, outcomes and duplicate-display-name flag. Comparison:

- 1173 existing reported identities, 1175 result instances: zero missing and
  zero changed outcomes/counts.
- 28 added identities, all passed: 27 scenario replays plus corpus verification.
- Final: 1201 reported identities, 1203 result instances.
- Existing `ContinuousPolarErrorEstimatorOracleTest.PolarAlignment_OracleScenario_MatchesExternalReference(OracleScenario)`
  has THREE cases with the same TRX display name and test ID. They are retained
  as one explicitly ambiguous three-instance group, all passed before and after.
  Execution IDs differ per run and cannot establish a stable one-to-one mapping.

## Exported behavior

- Fixture: `NINA.Plugins.PolarAlignment.Test/BehaviourFixtures/controller-stage1.json`.
- SHA256: `A11EA2B6F4E98F9285F9A3C86D1884882DF5A66E79942943C280980EC9AD4FBA`.
- 27 scenarios, 288 recorded controller API invocations and resulting states.
- One recorded trace, as transcribed in the July-17 regression test; its original
  raw log was not independently reverified here. Outputs are the current
  controller's decisions, not an assertion that those commands ran that night.
- 26 synthetic scenarios. The measured starting-vector simulation explicitly
  labels only its initial condition as recorded; every subsequent observation
  is synthetic. No simulation is field convergence evidence.
- Includes A.7 inconclusive take-up, consecutive strong worsening, weak-streak
  reset, one early reversal only, confirmed gain, damping, cross-coupling,
  bootstrap weakness/collinearity, regression, failed/partial execution, and
  four sub-degree corner simulations.
- Each entry names its originating regression test, constructor, named method
  arguments, outputs and post-call state. Synthetic plant matrices are included.
- `XMagnitude`/`YMagnitude` are **HISTORICAL LOGICAL UNITS**, not raw counts, motor
  steps or physical degrees. Historical +/-5.4 test envelopes are NOT today's
  +/-5 soft limit. Never deploy these fixtures as a calibration or motion plan.
- The supervisor reimplements behavior or documents a deliberate difference.
  In particular, do not silently adopt historical gains, travel constants or
  sky-inferred backlash as current encoder-driven policy.

Re-export explicitly (only the exporter test; ordinary full tests never update
the frozen fixture):

```powershell
$env:TPPA_BEHAVIOUR_EXPORT_PATH = 'C:\Dev\upas-nina-tppa-plugin\NINA.Plugins.PolarAlignment.Test\BehaviourFixtures\controller-stage1.json'
dotnet test NINA.Plugins.PolarAlignment.Test/NINA.Plugins.PolarAlignment.Test.csproj -c Release -p:SkipLocalNinaPluginCopy=true --filter FullyQualifiedName~AutomatedAdjustmentBehaviourExportTest.ExportOrVerifyBehaviourCorpus
Remove-Item Env:TPPA_BEHAVIOUR_EXPORT_PATH
```

## Exact AZ and target convention for contract A

Source: `TPAPAVM.PolarErrorDetermination.CalculateInitialMountAxisError` and
`CalculateTargetPoleAltitudeDegrees`, plus `RefractionAlignmentTarget` and its tests.
Let A,h be the solved mount-axis topocentric azimuth and altitude in degrees,
phi be site latitude, and hp the chosen pole altitude.

```text
North: azErrArcmin = 60 * wrap180(A)
       altErrArcmin = 60 * (h - hp)
South: azErrArcmin = 60 * wrap180(A + 180)
       altErrArcmin = 60 * (hp - h)
```

**AZ is an azimuth-coordinate residual: angular rotation about the LOCAL
VERTICAL. It is NOT great-circle angular distance on the sky and has no
cos(latitude) multiplier applied.** North-positive means east of north; the
ideal vertical-axis correction has the opposite sign. This is not a claim that
an unlevel physical base's encoder must move by exactly that angle: the
supervisor owns mapping sky requests to the real mechanism.

For equal polar altitude hp, great-circle separation from an azimuth-only
offset dA is `2*asin(cos(hp)*abs(sin(dA/2)))` (arguments in radians). For small
errors it is approximately `cos(hp)*abs(dA)`. At phi = 25.116194444444446 degrees,
cos(phi) = **0.905448864410678**. A 60-arcmin azimuth residual therefore
contributes about 54.327 arcmin of great-circle separation, not 60 arcmin.
For both components, the small-angle sky-distance approximation is
`sqrt(dAlt^2 + (cos(hp)*dAz)^2)`.

The current TPPA total uses the unweighted coordinate norm `hypot(dAlt,dAz)`.
It is NOT that great-circle distance. Preserve this fact in the interface and
do not silently change existing PA-error math during the structural migration.

**Automated requests target the TRUE celestial pole**, hp = abs(phi), with
`RefractionAdjustment=True`. This is the current default and the existing
automated target-policy requirement. With the setting false, measurement-only
mode may use the refracted pole (hp from NINA's refraction calculation; a failed
calculation falls back to the true-pole altitude and logs it). The numeric
controller alone cannot tell which target produced an observation; the
historical July trace's setting is unknown, not retroactively declared true.
Contract A must explicitly identify the pole target; no schema was changed here.

## Stop point

No automatic-route retirement or sky-client implementation yet. The supervisor
owns the schema amendments, including observation-only feedback, lease lifecycle,
measurement timing/clock semantics, pole-target and diagnostic version fields.
No worktrees, candidate packages, old branches or evidence were removed/archived.
Unrelated council scratch files, artifacts, report backup and the pre-existing
no-content-diff controller status remain untouched. Stage 1 ends with this report.
