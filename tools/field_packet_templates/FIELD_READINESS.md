# TPPA __PLUGIN_VERSION__ Field Readiness Packet

This packet is bound to repository checkpoint
`__SOURCE_COMMIT__`.

It supports an operational imaging qualification. It does not establish
traceable absolute polar-axis accuracy.

## Immutable runtime

- Plugin version: `__PLUGIN_VERSION__`
- Plugin DLL SHA-256:
  `__PLUGIN_SHA256__`
- Qualification core SHA-256:
  `__CORE_SHA256__`
- Runtime manifest SHA-256:
  `__MANIFEST_SHA256__`

Do not deploy a different build into an active campaign. Hash-verify the
installed plugin directory while NINA is closed.

## Campaign order

1. Commission one cadence authority for one fixed hardware configuration,
   mechanical epoch, load profile and environmental envelope.
2. Mint the cadence authority only after all four report-only arms pass.
3. Seal and run a separate 20-attempt fast-alignment campaign for each optical
   train. Every initiated attempt in the sealed window counts.
4. Require at least 16/20 successes over at least three nights. Each
   four-attempt starting-error stratum requires at least two successes and each
   three-attempt stratum requires at least one, with zero false success and zero
   safety violation.
5. After a same-session qualified alignment, acquire and qualify one guided
   900-second narrowband bracket for that unchanged optical train.

Commissioning evidence must not be counted as a fast-alignment attempt.

## Cadence authority commissioning

All arms use the exact installed runtime manifest and DLL, true-pole
refraction, the same hardware/mechanical identity and the same load profile.
No arm grants UPAS movement authority.

### Arm A: cadence nomination

- At least 20 guarded settle probes over at least two Dubai nights.
- At least eight runs in each slew direction and eight per night.
- Zero false-stable exits.
- Analyze with `analyze_tppa_settle_qualification.ps1`.

Example probe skeleton:

```powershell
& $Repo\tools\run_guarded_tppa_settle_probe.ps1 `
  -RigConfigurationId '<RIG-CONFIGURATION-ID>' `
  -TargetAzimuthDegrees <SAFE-AZ> `
  -TargetAltitudeDegrees <SAFE-ALT> `
  -Samples 13 -CadenceSeconds 5 -ExposureSeconds 1 `
  -OutputRoot '<SESSION-EVIDENCE-ROOT>'
```

### Arm B: candidate versus 30-second reference

- At least 20 paired fresh TPPA vectors over at least two nights.
- Both slew directions and both candidate/reference orders represented.
- No pair separated by more than 15 minutes.
- Maximum candidate-reference vector separation: 0.5 arcminute.
- No night may contribute more than 70 percent of the pairs.
- Analyze with `analyze_tppa_settle_vector_pairs.ps1`.

### Arm C: mandatory same-cadence null

- At least 10 null pairs over at least two nights and both directions.
- The observed null maximum must be positive and no greater than 0.25
  arcminute.
- The candidate observed maximum must be no greater than 1.5 times the null
  maximum. These are finite-campaign engineering bounds, not population
  quantile claims.
- No night may contribute more than 70 percent of the pairs.
- Analyze with `analyze_tppa_settle_null_pairs.ps1`.

### Arm D: exact-path fresh-determination timing

- At least 59 successful no-motion determinations over at least two nights.
- Both slew directions, at least eight per direction.
- Exact fresh-three-point-plus-return-field path only.
- True-pole refraction; no cadence authority; zero UPAS movements; 30-second
  settle; zero exclusions.
- Source each receipt directly from its exact NINA log event with
  `new_tppa_fresh_determination_timing_receipts.ps1`.
- Analyze with `analyze_tppa_fresh_determination_timing.ps1`.
- Observed maximum plus five seconds must be no greater than 75 seconds.

### Mint once

Only after A, B, C and D all pass:

```powershell
& $Repo\tools\new_tppa_cadence_authority.ps1 `
  -NominationReportPath '<A-REPORT>' `
  -VectorPairReportPath '<B-REPORT>' `
  -NullPairReportPath '<C-REPORT>' `
  -TimingReportPath '<D-REPORT>' `
  -RuntimeManifestPath '<INSTALLED-TPPA.runtime-manifest.json>' `
  -MechanicalStateId '<64-LOWERCASE-HEX>' `
  -OutputPath '<CREATE-NEW-AUTHORITY-PATH>'
```

Preserve the authority ID and file SHA-256. Never hand-edit it.

## Fast-alignment campaigns

Create one manifest per optical train before its first attempt. Do not reuse a
campaign after changing the plugin, optical train, load profile, mechanical
epoch, cadence authority or covariance authority.

Optical-train IDs:

- `WO-GT81-IV-0.8-OAG-L-ASI2600MM-gain100-bin1`
- `EdgeHD-9.25-0.7-OAG-L-ASI2600MM-gain100-bin1`

Manifest skeleton:

```powershell
& $Repo\tools\new_tppa_fast_alignment_campaign.ps1 `
  -OpticalTrainId '<EXACT-OPTICAL-TRAIN-ID>' `
  -RepositoryHead '__SOURCE_COMMIT__' `
  -PluginAssemblySha256 '__PLUGIN_SHA256_LOWER__' `
  -CovarianceAuthorityId '<UUID>' `
  -CovarianceAuthoritySha256 '<64-LOWERCASE-HEX>' `
  -CadenceAuthorityId '<UUID>' `
  -CadenceAuthoritySha256 '<64-LOWERCASE-HEX>' `
  -QualifiedSettleSeconds <COMMISSIONED-SETTLE> `
  -QualifiedFreshDeterminationSeconds <COMMISSIONED-UPPER-BOUND> `
  -MechanicalStateId '<64-LOWERCASE-HEX>' `
  -LoadProfileId '<FIXED-LOAD-PROFILE>' `
  -LogPath '<PREREGISTERED-NINA-LOG-PATHS>' `
  -OutputPath '<CREATE-NEW-MANIFEST-PATH>' `
  -ExpectedAttemptCount 20 -RequiredPassRate 0.8 `
  -MinimumSuccessfulAttempts 16 -MinimumEligibleRuns 20 `
  -MinimumNights 3 -MaximumRuntimeSeconds 300 `
  -MaximumToleranceMinutes 3 -MinimumMoveCount 1 -MaximumMoveCount 2 `
  -CampaignEndUtc '<SEALED-END-UTC>'
```

Set the returned `TPPA_PREREGISTERED_CAMPAIGN_ID` user environment value and
restart NINA before attempt 1.

Every attempt begins with a fresh physical supervisor observation. If both
conservative signed bounds are within +/-0.1 degree, mint a fresh zero witness.
Otherwise perform one bounded return-to-zero transaction and re-witness both
axes. Missing, stale, ambiguous or reused evidence denies the run. Controller
MPos is never physical evidence. The 300-second timer starts only after this
physical-zero admission.

Starting-error strata are contiguous: 0--30, 30--60, 60--120, 120--180,
180--240 and 240--300 arcminutes. Pre-register exact counts before attempt 1.
Every attempted run, including admission rejection, cancellation, crash,
timeout or missing terminal telemetry, remains in the denominator.

Success is exactly two independent fresh stationary true-pole determinations
at or below 3 arcminutes total inside 300 seconds, with no safety violation.

## Guided 900-second bracket

Run only after the same-session TPPA operational report passes. Keep the
optical train, OAG, rotator, focus, filter, cooling, field, pier side, tracking
and guiding fixed. Disable autofocus, dithering, meridian flip, filter changes
and derotation.

For each optical train:

1. Produce a fresh schema-2 OAG geometry receipt from near-simultaneous,
   undistorted ASTAP WCS solves of guide and imaging cameras.
2. Choose and record one exact narrowband filter name. The same case-sensitive
   value must appear in NINA, FITS and the calibrated policy.
3. Acquire 10-second and 60-second LIGHT scouts at gain 100, offset 50, bin 1;
   ensure neither is saturated or background-limited for extrapolation.
4. Mint and pass the saturation-scout receipt.
5. Generate the passive `5 x 30s + 1 x 900s + 5 x 30s` NINA sequence and
   visually verify it before first use with the installed NINA build.
6. Run `test_guided_900s_readiness.ps1` with exact SHA-256 values. A readiness
   pass grants no device or sequence authority.
7. Start `capture_phd2_guided_evidence.ps1` for 1200 seconds and verify its
   initial read-only gates before starting the NINA sequence.
8. Preserve all eleven original FITS files and complete PHD2/state artifacts.
9. Run `analyze_actual_exposure_bracket.ps1` and reproduce the receipt with
   `tppa-qualify verify-actual-exposure`.

`QUALIFIED` requires fixed-state continuity and acceptable star-shape behavior
in the center and all four outer zones. A pass for one train cannot qualify the
other.

## Stop conditions

Stop and count the affected attempt as a failure when applicable if any of the
following occurs:

- physical zero cannot be established from fresh supervisor evidence;
- a hard/uncertainty/headroom/reversal/regression gate denies movement;
- Weather or Safety Monitor is connected or a Switch output is altered;
- refraction is disabled or the target is not the true celestial pole;
- the sealed identity, authority, manifest, plugin or log binding mismatches;
- the five-minute deadline expires;
- evidence is missing, stale, ambiguous, modified or excluded after the fact.

Do not repair, relabel or silently restart a sealed denominator attempt.
