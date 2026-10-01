# Inactive sky-completion candidate — 1 October 2026

Owner cutoff: **2 October 2026 18:43:49 UTC /22:43:49 Dubai**. Owner is at the rig and allows indoor work, but explicitly defers sky positioning. This candidate is **not installed in NINA or the Mele service, not field qualified, and not a motion permit**. The previously qualified attended non-sky installation is preserved.

## Implemented responsibilities

- Supervisor: one finite continuous native controller/capture owner; genuine home before imaging; stable measurement session; counter/source/clock/owner continuity; no home after imaging; local signed interval conversions; all command writes, original guards, cancellation and cleanup. The new whole-epoch backend cannot be used as a per-chunk adapter.
- Encoders: separate named native observation profile; original contributors, conservative interval union, oldest contributor age, original freshness/vetoes, disjoint motion windows. Accuracy and validForControl remain absent/false without genuine pinned adoption. No motor ownership.
- TPPA: asynchronous reference preparation before imaging, original three-exposure UTC and qualified geometry, one session identity, unconverted sky intent, new measurement after each mechanical job, independent stationary confirmation. No actuator fallback on refusal. Mechanical EXECUTED alone never establishes alignment.

Contract A1.1 remains unchanged. Reference preparation is a separate authenticated `/attended/v1/sky-reference` extension, bound to an exact previously admitted finite operation UUID. A lease cannot create that admission. Port release, cancellation, uncertainty, restart, source/clock failure or deadline invalidates the reference.

## Verified evidence

- Release TPPA build passed; Release NUnit suite **1,234 passed, zero failed**.
- New reference/conversion/profile/native tests **33 passed**. Real frozen native engines with synthetic ports: one home per axis, one capture/port owner, supported +0.3/+0.3 request, original cleanup. A +0.15 request lacking the original persistent output witness is explicitly refused, not relabelled successful.
- Affected supervisor regressions: **71 passed** in the initial grouped run; six fixture setup errors were caused by intentional rejection of a second frozen import root. Required separate interpreter run: **8 attended-service tests passed**, zero errors.
- Original encoder interface regression: **56 passed, one failed**. The unchanged counter test assumes two immediate perf_counter_ns reads must be strictly different; this host returned equal readings. Production checks require clock order and reject regressions. The failure is preserved; no retry or product-clock change was applied.
- Original actual three-component C# client/supervisor/encoder synthetic demonstration passed with six writes and durable duplicate identity, zero hardware operations.
- New actual C# workflow/supervisor/encoder synthetic demonstration passed: prepare, one mechanical job, post-move measurement, final stationary measurement, original workflow close; zero hardware operations. Its reference fixture is explicitly synthetic. Actual continuous native both-axis execution is covered by the separate native test; these results are not combined into field qualification.

Three cause-specific product correction rounds are spent: TPPA geometry member binding, per-axis capture projection, and native logger axis identity. Frozen native sources and all old charges/limits remain unchanged.

## Exact unmet activation requirements

1. Owner has deferred sky positioning, so real three-point sky response and sky acceptance have not been performed.
2. Adopt actual measured signed gains in both directions, local support, sky measurement uncertainty and cross-axis response. The interval compiler exists; it does not automatically adopt an injected probe list. A pinned gain-artifact loader/production construction path remains to be completed and verified before deployment.
3. Produce genuine pinned production-observation adoption with independently bounded absolute error and held-out evidence. Diagnostic scatter and synthetic results cannot supply it.
4. Qualify the new continuous native binding with fresh finite genuine physical/controller/sensor/source/clock admission, source pins, original cancellation/Idle/cleanup and charges. The former twelve non-sky field blocks do not qualify this new binding.
5. Finish a source-pinned production factory and reversible service/NINA installation; qualify the full newly bound three-component path. Current tests cover separate seams and explicitly synthetic transport, not that installation.
6. Verify fault and concurrency handling before production, including encoder publication/read synchronization, exact per-job versus cumulative charge reporting, prepare-refusal cleanup without a started worker, and actual executing-file pin revalidation. These are identified follow-up checks, not claims of verified defects or waived requirements.
7. Establish an empirically supported small-correction range compatible with the retained persistent movement witness. The synthetic 0.15 refusal is not evidence of accurate fine alignment.

All historical charges remain spent: AZ19,972 raw/613 writes; ALT103,340 raw/809 writes. No new physical motor/camera/sensor operation was performed in this phase. Requested +/-3.5, guard +/-4, physical AZ+/-5.6/ALT+/-6, uncertainty .3, stop .3, ALT clearance .05/reversal .075 and all original sensor/transport protections remain intact. P20 is not a dependency. Unattended use remains unqualified.

## Reproduction

Set PYTHONPATH to the isolated supervisor/src and encoders/src. Run the new supervisor test with UPAS_ENCODER_ROOT set to the exact encoder checkout. Run attended-service tests in a separate interpreter from any test that materializes a different native root. Use a fresh writable --basetemp. Run the TPPA Release build and test serially. Demonstration scripts are offline test fixtures only; never supply a live serial/remote endpoint.

The deliverable contains an explicit source overlay, two Release TPPA DLLs and SHA-256 manifest. It has no tokens, database, service configuration, launcher, boot trigger or admission. Do not replace the qualified installation with it. Preserve the listed baseline commits to reverse each source overlay.
