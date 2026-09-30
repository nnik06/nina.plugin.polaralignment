# Offline integration candidate: partial delivery

The executable TPPA adapter → asynchronous C# client → Python supervisor → encoder provider path passes its synthetic demonstration. This is an offline candidate, not an activated motion service. Contract A response compatibility remains incomplete after the three permitted correction rounds.

Owner-authorized session: 30 September 2026 18:42:31 UTC to 1 October 2026 06:42:31 UTC (22:42:31 to 10:42:31 Dubai). All three correction rounds are spent. No further implementation remedy or successor is initiated under this allowance.

## Validation

- Encoder selected interface, commissioned, Hall and handover checks: 193 tests and 128 subtests passed. The initial entire historical suite exceeded its 300-second bound; that broader scope is unverified.
- TPPA Release build and full NUnit suite: 1,209 passed, none failed/skipped; local NINA plugin copy disabled.
- Final supervisor integration and inherited protocol checks: 177 passed, one response-schema test failed and remains present.
- Final actual three-component synthetic demonstration: EXECUTED on both axes, six synthetic writes, exact duplicate job identity without extra execution. Mechanical completion is not sky verification.
- Supervisor-owned frozen 70-raw adapter demonstration: 32 synthetic writes, 1,675 gross raw units; no write above 70; cleanup passed. Measurement only, not field acceptance.

## Remaining defects

Lease acquisition and renewal omit required `clientId` and `durationSeconds` response fields. The exercised conflict response omits `holderClientId` and uses `IDEMPOTENCY_CONFLICT` instead of the contracted `IDEMPOTENCY_PAYLOAD_CONFLICT`. Other conflict reasons require a complete mapping audit. Successful synthetic execution does not excuse these API compatibility defects.

Read-only diagnosis is saved in `schema-response-audit/SCHEMA_DIAGNOSIS.json`; the failed response test and all earlier failure evidence are preserved. No fourth code correction was applied. A separately authorized bounded response-compatibility repair would be the next implementation step.

## Reproduce the demonstration

From `C:/tmp/upas-i-20260930/tppa`, use a new synthetic journal path for each distinct demonstration:

```powershell
dotnet run --project tools/UpasOfflineIntegration/UpasOfflineIntegration.csproj -c Release -p:SkipLocalNinaPluginCopy=true -- "C:/Program Files/Python312/python.exe" "C:/tmp/upas-i-20260930/sup" "C:/tmp/upas-i-20260930/enc" "C:/tmp/new-synthetic-demo.db"
```

From `C:/tmp/upas-i-20260930/sup`:

```powershell
& "C:/Program Files/Python312/python.exe" tools/offline_trial_adapters/measurement_demo.py --encoder-root "C:/tmp/upas-i-20260930/enc"
```

The test transport is subprocess stdio with injected synthetic ports; no server or equipment connection is launched. Existing idempotency records preserve spent synthetic charges rather than replaying a command.

## Practical limits and ownership

Encoder code reports observations; the supervisor owns admission, finite jobs, accounting, stop and reconciliation; TPPA owns fresh sky geometry and alignment orchestration. The plugin seam is unregistered. Production transport/backend, live clock binding, short-window field behavior, calibration adoption, sky conversions and physical qualification remain outside this candidate. The frozen ATT and installed v4b package were not changed.

Requested ±3.5°, guard ±4°, physical AZ ±5.6°/ALT ±6°, uncertainty/stop reserves and original sensor ages remain unchanged. No hardware, camera, remote operation, deployment, production activation, push or live permit was supplied by these tests. P20 is not an operational dependency.

Transient-disturbance recovery is a future todo in `TRANSIENT_DISTURBANCE_TODO.md`: automatically clear a qualified stationary observation pause, reconcile any interrupted job, obtain fresh sky geometry, and retain genuine uncertainty/guard faults. No wind/bird recovery was implemented or newly qualified.

Claude's read-only audit used explicit root/HEAD/dirty-state context; its provenance was prompt-asserted, not an independent repository inspection. Source manifests, test/log hashes, original-tree preservation and local commit identities accompany this delivery. The authoritative workspace evidence directory is `C:/Users/nnik0/OneDrive/Документы/Playground/.implementation-work/integration-20260930`.
