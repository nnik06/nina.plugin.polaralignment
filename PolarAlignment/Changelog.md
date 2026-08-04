## Version 2.2.6.108

- Bind each field-readiness packet to an exact supervisor commit and embed the
  coarse-response protocol from that checkpoint under the packet checksum manifest.
- Reject packet generation when the supervisor checkpoint is stale or its
  commissioning protocol is absent.

## Version 2.2.6.107

- Bind the witnessed UPAS physical-zero transaction to the sealed preregistered
  TPPA campaign ID and reject missing or malformed campaign identity before any
  return-to-zero motion.
- Admit that same campaign only after fresh post-return scale evidence confirms
  both physical axes near zero; the five-minute runtime begins afterward.

## Version 2.2.6.106

- Add the missing per-load-profile coarse-response and travel prerequisite to
  every field packet before covariance, cadence, or sealed 0--5 degree runs.
- Bind that prerequisite into packet hashes and test that response fitting and
  the retained one-degree reserve cannot disappear from field preparation.
## Version 2.2.6.105

- Derive the legacy per-axis coarse objective from the 0--300 arcminute
  campaign envelope instead of independently truncating it at 240 arcminutes.
- Derive the uncertainty-expanded operational position limit once from the
  compiled +/-5.4 degree software limit and one-degree reserve, while retaining
  fail-closed endpoint denial for uncommissioned edge-of-envelope corrections.
## Version 2.2.6.104

- Replace the small-sample cadence p95 comparison with an explicit observed-
  maximum comparison, and freeze the changed semantics in cadence-authority
  schema 3 and a new commissioning-policy digest.
- Require each four-attempt starting-error stratum to contribute at least two
  successes, preventing a globally passing campaign from hiding three failures
  in one edge of the 0--300 arcminute envelope.
## Version 2.2.6.103

- Require schema-2 cadence authority to bind the exact runtime manifest, policy,
  mandatory candidate and null campaigns, and an independent timing campaign.
- Qualify candidate cadence only against both a 0.5-arcminute absolute ceiling
  and a 1.5-times same-cadence null p95 ceiling, with direction-order and
  cross-night balance gates.
- Require at least 59 no-motion, true-pole, 30-second-fallback observations of
  the exact fresh-three-point-plus-return-field path; reserve the observed
  maximum plus five seconds and deny authority above 75 seconds.
- Emit self-describing schema-2 runtime timing events and produce receipts only
  from hash-bound NINA log lines and the exact installed runtime manifest.
- Keep every commissioning arm report-only; only the create-once minter can
  produce an exact-build cadence authority.
## Version 2.2.6.102

- Extend guarded automated-correction admission to the full 0--300 arcminute
  UPAS objective while retaining 24 arcminutes as the fine-controller handoff.
- Require a current commissioned cadence authority, bound to the exact plugin
  DLL, hardware configuration, mechanical epoch, load profile, temperature
  range, and source campaign, before a sub-30-second settle can enter the
  five-minute actuator path. The unconditional fallback remains 30 seconds.
- Bind the exact cadence-authority ID, artifact hash, qualified settle, and
  maximum fresh-determination duration into runtime telemetry and sealed
  campaign schema 5; reject mismatched or legacy campaign evidence.
- Keep physical-zero admission ahead of the five-minute timer: the supervisor
  must freshly witness both axes within +/-0.1 degree or perform and re-witness
  a bounded return to physical zero before TPPA acquisition begins.
## Version 2.2.6.101

- Require a sealed preregistered campaign ID before physical-zero admission can authorize UPAS movement.
- Emit and verify that campaign ID separately from the supervisor's per-attempt transaction campaign, preventing runtime evidence from being reassigned to another denominator.

## Version 2.2.6.100

- Seal the current rig mechanical-epoch receipt in every preregistered fast-alignment campaign and runtime start event.
- Reject campaign evidence when the active TPPA mechanical epoch differs from the sealed campaign, even when DLL and covariance-authority identities still match.

## Version 2.2.6.99

- Bind covariance commissioning and runtime authority use to one explicit,
  receipt-backed mechanical epoch rather than minting a different state per run.
- Reject commissioning evidence whose hardware or mechanical-state identity
  differs from the frozen manifest.
- Recompute the active TPPA hardware identity and require the configured
  mechanical epoch to match before fast-alignment movement can begin.
## Version 2.2.6.98

- Bind every preregistered fast-alignment campaign to the exact repository
  checkpoint, plugin DLL SHA-256, commissioned covariance authority and artifact
  SHA-256, and load profile.
- Emit the same exact-build identity in run-start telemetry and reject malformed,
  missing, rebuilt, or swapped-authority evidence even when timing and TPPA
  outcomes otherwise pass.
- Advance the sealed campaign manifest to schema v3; schema-v2 and older
  manifests remain historical and cannot qualify the exact-build verdict.

## Version 2.2.6.97

- Expand the non-actuating preregistered evidence denominator to the full
  0--300' objective while retaining the separately guarded 0--240' planner envelope.

- Use a conservative `24'/sqrt(2)` per-axis no-move threshold so two accepted
  axes cannot silently exceed the `24'` total-vector fine-controller handoff.
- Report conservative lower and upper post-move residual bounds from response
  and fixed-command uncertainty; the nominal residual alone does not authorize
  a fine-controller handoff.
- Version the newly complete fast-alignment preregistration policy as campaign
  manifest schema v2; legacy v1 manifests cannot qualify the sealed-policy verdict.
- Add a create-once, externally hash-anchored fast-alignment campaign manifest
  with a fixed time window, exact log set, expected attempt denominator, and
  every verdict parameter sealed before the first attempt.
- Add `PreregisteredCampaignPassRateMet` as an honestly scoped single-campaign
  point estimate. It counts admission rejections, crashes, cancellations, and
  missing telemetry as denominator failures; it does not claim confidence-
  bounded population reliability or absolute polar accuracy.
- Reject missing or mismatched manifest hashes, backdated windows, omitted
  preregistered logs, attempt-count mismatches, and post-seal policy changes.
- Report a Wilson 95 percent interval beside the sealed campaign point estimate
  so small campaign denominators cannot be mistaken for population reliability.
- Seal the full 0--240 arcminute objective envelope, reserving the final degree of physical travel independently of the
  controller's present 0--24 arcminute motion qualification; in-envelope
  admission rejections remain failed attempts and never widen motion authority.
- Require contiguous preregistered starting-error strata across the envelope,
  exact per-stratum attempt counts, and at least one success in every stratum.
- Add a non-actuating coarse-correction planner for the 24--240 arcminute
  transition. It converts calibrated TPPA response into signed physical-degree
  intents and denies plans that violate the +/-5.4 degree software limit, the
  one-degree reserve, or witnessed/calibration uncertainty.
## Version 2.2.6.96

- Add a run-scoped fast-alignment admission latch at both actuator connection
  and movement boundaries. A rejected or not-yet-admitted fast run cannot reach
  either actuator callback even if later control flow is reordered.
- Add executable callback coverage proving denied fast admission performs zero
  actuator calls while admitted and non-fast paths retain their intended access.

## Version 2.2.6.95

- Reject five-minute fixed-gain actuator starts above the preregistered 24
  arcminute field-test boundary before any UPAS movement. Easier starts remain
  eligible and may complete without movement. The upper bound is motivated by
  an idealized two-move 0.65 response, not a convergence guarantee; wider
  starts require a separately qualified gain schedule.
- Emit and retain structured admission rejections for configuration and
  initial-measurement denials instead of silently dropping non-admitted trials.
- Qualify one- or two-move evidence chains, require every inter-move pre-vector
  to match the previous fresh post-vector, and screen the campaign at 8 of 10
  admitted attempts while keeping absolute accuracy and delivered-exposure
  claims explicitly false. The 8-of-10 result is an admitted-envelope
  screening milestone, not population reliability.
## Version 2.2.6.94

- Bind the passive 900-second sequence, calibrated policy, scout evidence, and
  delivered-star analysis to one exact non-placeholder filter identity.
- Add a fail-closed 10-second/60-second saturation scout that projects the
  measured 900-second background and saturation headroom while exempting only
  bounded sparse bright sources; extended saturation remains a failure.
- Require every scout-critical policy value to be explicit, hash and reproduce
  both FITS inputs and the deterministic scout receipt at readiness, and reject
  stale, separated, tampered, wrong-train, wrong-filter, wrong-gain/offset/bin,
  or authority-expanding evidence.
- Keep the scout authority narrow: passing establishes sensor/background
  headroom only and never predicts guiding or star-shape success, starts a
  sequence, authorizes movement, or claims absolute polar accuracy.
## Version 2.2.6.93

- Reopen and hash-verify the distinct main- and guide-camera WCS sources named
  by every actual-exposure OAG geometry receipt; a hash-shaped claim alone no
  longer satisfies delivered-performance qualification.
- Add create-new sealed OAG receipt output that resolves source paths, computes
  their hashes, rejects mismatches, and preserves the no-motion/no-absolute-PA
  authority boundary.
- Require schema-v2 OAG receipts derived from strict undistorted ASTAP WCS,
  portable bundle-relative source paths, and independent reproduction of every
  parsed and computed geometry field. Keep the hand-entered calculator
  explicitly diagnostic-only.
- Bind each WCS digest to the exact immutable bytes reparsed by qualification,
  reject duplicate or unknown receipt fields, reparse-point/ADS-style paths,
  sheared or implausible WCS matrices, and main/guide observations separated by
  more than 60 seconds.
- Project each geometric sensor centre through its TAN WCS instead of assuming
  `CRVAL` is the sensor centre when `CRPIX` is offset.

## Version 2.2.6.92

- Add a deterministic NINA Advanced Sequencer generator for the actual
  900-second witness: exactly five short controls, one long light, and five
  short controls with no equipment, pointing, guiding, filter, focus, tracking
  or shutdown actions.
- Publish through an exclusive atomic rename, test the complete emitted NINA
  type allow-list and zeroed loop counters, and document first-load schema and
  sampled-interior limitations.

## Version 2.2.6.91

- Replace the asserted schema-1 fixed-state interval with a schema-2 sampled
  continuity witness covering the complete actual-exposure bracket.
- Poll read-only NINA mount, camera, filter-wheel, focuser and rotator state at
  bounded cadence while independently sealing the PHD2 profile, exposure,
  equipment and guide-algorithm parameter digest.
- Independently reparse every state sample and reject transient state changes,
  cadence gaps, clock inconsistency, summary inflation, bracket undercoverage
  or a PHD2 configuration mismatch.

## Version 2.2.6.90

- Anchor actual-exposure stars in all ten short controls and gate explicit
  long-frame attrition so a degraded 900-second frame cannot pass on survivors.
- Require measured FITS WCS scale, two-pixel control-PSF sampling, bounded
  control-frame scatter, finite pixels, and complete PHD2 event provenance.
- Independently reparse the hash-bound PHD2 guide-step CSV for contiguous frame
  numbers, monotonic clocks, bounded cadence gaps, and full bracket coverage.
- Add adversarial regressions for attrition, quadrant-localized degradation,
  wrong WCS scale, nonfinite pixels, undersampling, and unstable controls.

## Version 2.2.6.89

- Add a fail-closed, seeing-inclusive actual 900-second star-shape witness using
  independent pre/post controls, direct FITS adaptive moments and ASTAP sky IDs.
- Bind exact policy, FITS, ASTAP catalog/binary, PHD2 continuity, OAG geometry
  and state-continuity artifacts into a deterministic, verifiable receipt.
- Add separate EdgeHD 9.25 + 0.7x and undersampled GT81 IV + 0.8x policies;
  neither grants absolute PA or UPAS motion authority.

## Version 2.2.6.88

- Replace permissive guided-event percentage coverage with a fail-closed
  continuity gate: GuideStep frame numbers must be contiguous, receive times
  monotonic, and all internal and boundary gaps within three observed median
  cadences.
- Sample lock position throughout the guided interval, reject socket loss and
  unknown PHD2 events, and normalize mount azimuth before wrapped-sector tests.
- Add executable continuity and wrapped-azimuth boundary tests.

## Version 2.2.6.87

- Add a read-only PHD2 evidence client for synchronized guided rotation trials.
  It requires an already calibrated and guiding PHD2 session, observes
  `GuideStep` coverage, and fails closed on guide-output loss, dither, settling,
  lock-position changes, star loss, calibration, or configuration changes.
- Add a schema-3 guided runner and analyzer mode that can qualify only a
  delivered-rotation witness. It cannot qualify an actual 900-second artifact,
  infer the polar-error vector, claim absolute accuracy, or authorize movement.
- Pin the guided event and RPC contract to the inspected official PHD2 source.

## Version 2.2.6.86

- Mark the passive physical-roll witness explicitly non-inferential for polar
  alignment. A low roll rate, especially near the celestial pole, does not
  bound the polar-error vector without a qualified geometry-sensitivity model.
- Pin the transported-roll sign in the synthetic RA-zero-crossing regression.

## Version 2.2.6.85

- Compare solved camera orientation only after exact shortest-geodesic parallel
  transport into the first solve's celestial tangent basis. Raw WCS position
  angle remains diagnostic and can no longer masquerade as physical rotation
  when the solved centre drifts, especially at high declination.
- Record and require stable plate-solve parity, reject antipodal transport, and
  include a configurable one-arcsecond angular measurement floor so a
  numerically perfect series cannot claim zero uncertainty.
- Reclassify the synchronized fixed-point verdict as a passive physical-rotation
  witness. It can never grant guided-exposure qualification; that requires a
  synchronized guided run plus actual 900-second star-shape validation.

## Version 2.2.6.84

- Add a fail-closed analyzer for synchronized passive PA evidence. It verifies
  every manifest artifact hash, unwraps main-camera WCS position angle, fits
  robust full/first/last-window rotation rates, and converts a conservative
  rate into a 900-second guide-to-corner smear bound.
- Keep the new verdict operational only: passing means measured field rotation
  is within the configured imaging budget and still requires a real 900-second
  star-shape validation; it does not establish absolute true-pole accuracy.

## Version 2.2.6.83

- Add an orientation-independent OAG geometry bound from simultaneous main and
  guide WCS centres, sensor dimensions, and measured pixel scales. The result
  replaces the provisional 8000-pixel guide-to-corner assumption with a
  source-bound conservative radius for each optical train.
- Document separate OAG geometry receipts for the reduced GT81 IV and reduced
  EdgeHD 9.25 before judging 900-second field-rotation performance.

## Version 2.2.6.82

- Add an authority-free synchronized PA discriminator that records main-camera
  solves, raw PHD2 guide-star displacement, iPolar window evidence, 1 Hz mount
  telemetry, atmosphere provenance, UTC/monotonic timing, clock probes, and
  immutable artifact hashes in one fixed mechanical epoch.
- Extend the existing iPolar recorder's validated duration and cadence ranges
  for low-cadence 30-40 minute stationary campaigns while preserving its
  existing high-rate slew-capture mode.
- Add a conservative guided-exposure field-rotation budget so the operational
  PA target can be stated in ASI2600 pixels at the measured OAG guide-star
  separation instead of treating an arbitrary arcminute threshold as image
  performance.

## Version 2.2.6.81

- Make the five-minute UPAS correction contract explicitly one-shot instead of
  advertising an unreachable 18-move feedback loop.
- Before the one permitted move, reserve the move plus two independent fresh
  determinations using the observed pre-move runtime as a conservative cadence
  floor. Slow runs now fail before physical movement rather than relying on the
  75-second clean-field minimum for both post-move determinations.

## Version 2.2.6.80

- Exercise the complete GRBL jog-cancellation orchestration with injected
  controller-state sequences, including realtime `0x85` emission, transitional
  motion, two stable `Idle` confirmations, missing telemetry, and timeout.

## Version 2.2.6.79

- Record the actually loaded TPPA plugin and qualification-core paths, assembly and informational versions, SHA-256 hashes, and MVIDs in every qualification run artifact.
- Bind the run pipeline digest to the loaded plugin DLL hash and reject missing, malformed, or mismatched runtime identities before evidence is persisted.
- Exercise GRBL jog-cancellation confirmation as a sequence: non-idle states and changing positions reset the two-stable-Idle requirement.

## Version 2.2.6.78

- Harden GRBL jog cancellation after a movement failure: require two 300 ms-spaced `Idle` reports with stable X/Y/Z positions before cancellation is considered verified.
- Treat only completed GRBL hold/door substates as stationary, preventing `Hold:1`, `Door:2`, or `Door:3` deceleration/resume states from satisfying movement-stop checks.

## Version 2.2.6.77

- Add a fail-closed runtime-package installer that refuses to operate while
  NINA is running, validates the manifest-bound package before changing the
  live plugin tree, archives stale TPPA assemblies and deployment suffix files
  outside that tree, stages and hash-checks every artifact, validates the final
  installation, and restores the prior runtime on any transaction failure.
- Add transaction tests for successful installation, live-NINA refusal,
  invalid-package refusal, and rollback after final-validation failure.

## Version 2.2.6.76

- Make the five-minute automated path explicitly admissible only with the
  qualified 30-second point settle, plate-solve exposures no longer than three
  seconds, and auto-pause disabled; reject an ineligible configuration before
  connecting to or moving UPAS.
- Split clean and retry-capable measurement reserves. Before every physical
  move, reserve the complete terminal chain of movement, independent fresh
  response, and stationary fresh confirmation; deny late moves while retaining
  the hard 300-second deadline.
- Emit GRBL realtime jog cancel on any post-command cancellation or failure and
  require two stopped-status confirmations before unwinding the move call.
- Promote the headless qualification sources to a real `net8.0` project and
  replace plugin/CLI source globs with explicit project references, preserving
  the embedded policy-source digest in the owning assembly.
- Make Bitbucket verification build the complete solution and run the WPF
  NUnit suite on a self-hosted Windows runner before packaging; restore the
  missing Release/Any CPU test-project build mapping and pin that mapping with
  a CI contract test.
- Update the headless CLI contract test to require the project boundary rather
  than the removed source glob, and run it in CI through Pester `-EnableExit`
  so a failed assertion cannot leave the verification step green; CI now runs
  the complete PowerShell contract suite.
- Package, Debug-deploy, and install-validate the qualification-core assembly
  atomically with the main plugin. Generate one build-bound runtime manifest
  containing source commit, version, and both DLL hashes; guarded launchers pin
  that single manifest hash so independently valid artifacts cannot be mixed.
  Document the witnessed physical-axis contract without asserting an
  unverified universal raw-X polarity.

## Version 2.2.6.75

- Add a report-only common-mode bias observability policy for multi-arc TPPA
  calibration campaigns bound to an independent mount-axis witness.
- Require qualified dual-pier geometry, two-dimensional HA/Dec conditioning,
  one mechanical/environment epoch, true-pole refraction, disjoint input paths,
  and conservative witness + arc + field-variation uncertainty below 0.5'.
- Reject large stable offsets such as the unresolved ~2 deg iPolar/TPPA gap;
  the policy grants neither motion nor completion authority.
## Version 2.2.6.74

- Before authorizing an UPAS move under the five-minute contract, reserve time
  for the complete terminal chain: actuator motion, independent fresh feedback,
  and the mandatory stationary completion confirmation.
- Fail before physical movement when feedback alone would fit but the required
  confirmation would necessarily exceed the runtime ceiling. Under the current
  300-second contract, the existing path must reach the move boundary within
  35 seconds, so field runs will normally refuse motion until a faster terminal
  protocol is separately implemented and qualified.

## Version 2.2.6.73

- Keep sub-30-second sequence-local settling available for measurement-only
  cadence experiments, but deny automated UPAS movement unless the effective
  profile or sequence settling interval is at least the field-qualified 30 seconds.
- Enforce the settle-authority gate both during sequence validation and again
  immediately before actuator-capable execution so stale UI state cannot bypass it.

## Version 2.2.6.72

- Emit one report-only `TPPA_MOVE_TIMING` JSON record for every automated RA
  leg, including actual motion, stop-wait, requested and observed settle,
  total duration, direction, adjusted rate, and terminal outcome.
- Preserve all existing movement, timeout, settle, tracking, emergency-stop,
  and acceptance behavior while making the five-minute execution budget
  attributable from field logs.
## Version 2.2.6.71

- Require every solved TPPA leg to meet the same 15-degree on-sky span floor
  used by the absolute-evidence contract before automated correction can move
  UPAS; configured RA travel and triangle quality cannot substitute for span.
- Add a declination-aware preflight that predicts on-sky separation from the
  configured RA leg and rejects foreshortened high-declination arcs before any
  UPAS preparation or movement.
- Deny the legacy UPAS pre-measurement azimuth pre-seat unless a fresh solved-
  geometry qualification is already bound, closing the preflight-pass/post-
  solve-fail path that could otherwise move before solved geometry was known.
- Synchronize geometry-qualification authority and its denial reason as one
  snapshot so asynchronous continuations cannot observe stale movement authority.

## Version 2.2.6.70

- Allow the existing bounded, sequence-local point-settle override to apply to
  ordinary automated TPPA as well as diagnostic modes, enabling controlled
  standard-versus-guarded cadence tests without mutating the active NINA profile.
- Report the source as a sequence override in movement-settle telemetry.

## Version 2.2.6.69

- Add report-only VerificationOnly point context evidence with explicit
  per-point approach direction, requested RA travel, settle interval, exposure-
  midpoint hour angle, apparent altitude, refraction drift, and the exact
  atmosphere used. Unknown initial pre-positioning remains explicit, and the
  evidence grants neither motion nor completion authority.
## Version 2.2.6.68

- Add an undeployed report-only A/B/C/A RA-witness runner with whole-trajectory
  one-degree sampling, constant-pier prediction, a five-degree meridian
  exclusion, realized-time re-preflight before every command, live NINA
  trajectory watchdogs, atomic one-attempt observer requests, and a
  live-watched return-to-A cleanup.
- Add an exact-equatorial NINA slew child that emits immutable issue/completion
  receipts and grants no UPAS or completion authority.
- Set the default witness arc to the qualified 45-degree minimum. A geometry
  sweep showed that a 50-degree arc cannot retain the 40-degree operational
  altitude floor in the north-balcony opening, while a narrow 45-degree path
  can.

## Version 2.2.6.68

- Add an undeployed report-only A/B/C/A RA-witness runner with whole-trajectory
  one-degree sampling, constant-pier prediction, a five-degree meridian
  exclusion, realized-time re-preflight before every command, live NINA
  trajectory watchdogs, atomic one-attempt observer requests, and a
  live-watched return-to-A cleanup.
- Add an exact-equatorial NINA slew child that emits immutable issue/completion
  receipts and grants no UPAS or completion authority.
- Set the default witness arc to the qualified 45-degree minimum. A geometry
  sweep showed that a 50-degree arc cannot retain the 40-degree operational
  altitude floor in the north-balcony opening, while a narrow 45-degree path
  can.

## Version 2.2.6.64

- Replace the sample-count-dependent raw leave-one-out center RMS gate with the
  correctly scaled two-dimensional jackknife standard error of the fitted
  iPolar axis center. Keep maximum single-point leave-one-out center shift as a
  separate leverage guard.
- Require each leg's jackknife center standard error to remain at or below 15
  arcseconds, conservatively below the 21.2-arcsecond equal-share uncertainty
  budget for comparing two centers within 30 arcseconds.

## Version 2.2.6.63

- Add leave-one-out fitted-axis center stability to each iPolar slew leg. A
  small radial residual can no longer qualify a short or ill-conditioned arc
  whose inferred rotation center changes by more than 15 arcseconds RMS or 30
  arcseconds maximum when one tracked point is removed.
- Persist the center-stability values in pixels and arcseconds so the 30-arcsec
  differential campaign threshold is backed by a measured per-leg
  conditioning diagnostic rather than residuals alone.

## Version 2.2.6.62

- Identify the five-minute deadline by its own cancellation token instead of
  inferring deadline expiry from a 250 ms elapsed-time window. User/window
  cancellation remains cancellation; only the armed runtime deadline becomes
  an explicit sequence failure.
- Seal every four-leg iPolar pier-side campaign result to the SHA256 recorded
  in its immutable phase manifest, and fail closed when a hash is absent or a
  result changes before final evaluation.
- Record that the five-minute contract prevents success after 300 seconds from
  sequence-item entry; it cannot force a non-cooperative driver call to return
  by that wall-clock instant and does not itself prove alignment accuracy.

## Version 2.2.6.61

- Replace absolute-evidence schema v2 with v3. Bind every independent-witness
  solve to its mount command, stationary tracking state, disabled PHD2 guide
  output, exposure midpoint, FITS time, raw guider-image digest, external
  solver output/binary digests, solved coordinates, horizontal telemetry, and
  pier side; reject detached or incomplete acquisition provenance.
- Persist the raw FITS DATE-OBS value and its explicit exposure-start or
  exposure-midpoint convention. Normalize to the exposure midpoint under
  separate clock and FITS timestamp uncertainty bounds instead of ambiguously
  treating DATE-OBS as a midpoint.
- Require the witness evidence to bind a qualified full A/B/C/A trajectory,
  monotonic fixed-declination commands, at least 45 degrees total RA arc,
  minimum 40-degree altitude, one-degree trajectory sampling, bounded
  conditioning, and verified PHD2 guide-output restoration.
- Move the report-only RA-rotation witness producer into the shared headless
  qualification core and expose a strict produce-witness CLI command. The CLI
  consumes immutable metadata and point-receipt JSON, writes with create-new
  semantics, and grants no motion or completion authority.
- Add a fail-closed full-trajectory preflight for the report-only RA-rotation
  witness. It samples every slew leg at no more than one degree, enforces the
  wrapped mount envelope, a separate operational altitude floor, known constant
  pier side, minimum total arc, and a bounded design-conditioning proxy.
- Log capture, plate-solve, and total elapsed time for every non-cancelled TPPA
  solve attempt so field runtime can be optimized from measured phase latency.
- Keep the timing telemetry report-only; estimator results, settling, movement
  authority, and safety gates are unchanged.
- Emit an immutable receipt for every completed VerificationOnly solve and a
  final complete or partial dataset receipt even when cancellation or timeout
  interrupts the run; these receipts never grant motion or completion authority.
- Add an opt-in guarded launcher pre-warm that performs one separately timed,
  no-slew capture and solve before sequence start. Pre-warm duration is recorded
  explicitly and excluded from the VerificationOnly runtime claim.
- Correlate every VerificationOnly solve timing, final/partial dataset receipt,
  and terminal run summary with one run ID so a field certificate cannot mix
  evidence from separate executions.
- Add an offline five-run operational qualifier that requires five consecutive
  warm, first-attempt, nine-solve runs below 300 seconds with true-pole
  refraction enabled and every internal verdict passing. It reports speed and
  internal consistency separately and never upgrades them to absolute accuracy.
- Add an interactive iPolar slew recorder, generated-image star-observability
  gate, and same-pier trajectory-guarded runner. These tools reject starless
  dawn frames and pier-side/envelope violations and never grant UPAS authority.
- Automatically fit a stellar rotation circle from hash-verified in-slew
  frames, reject sparse or short arcs and residuals above the configured
  15-arcsecond RMS / 30-arcsecond maximum bounds, and preserve a per-leg axis
  receipt. Add a manifest-based four-leg evaluator that requires unique named
  reciprocal legs and run IDs on the correct pier sides and rejects fitted-axis
  disagreement above 30 arcseconds; neither evaluator grants UPAS or
  absolute-accuracy authority.
- Add one shared, headless absolute-evidence binder compiled into both the
  plugin and a small CLI. It binds separate immutable TPPA and independent
  witness files, rejects stale, aliased, wrong-frame, tampered, or circular
  calibration provenance, and emits no receipt for structurally invalid input.
- Distinguish valid-but-not-qualified evidence from invalid evidence with
  process exit codes and immutable receipts. The binder and CLI never grant
  telescope or UPAS motion authority.
- Replace absolute-evidence schema v1 with v2. Persist every raw three-point
  solve vector plus UTC, source digest, solved coordinates, pier side, site,
  atmosphere, clock uncertainty, and vector-frame metadata; schema v1 is no
  longer eligible for an absolute claim.
- Make the headless binder independently recompute the plane fit, hemisphere
  orientation, arc geometry/span, returned-A closure, site-derived pole,
  atmosphere freshness/ranges, clock bound, and coordinate-frame gates. Treat
  producer booleans and fitted vectors as assertions that must agree.
- Require an explicit `absolute-true-pole` witness basis and a disjoint raw
  four-solve RA-rotation A/B/C/A arc. Recompute its axis, span, and closure;
  reject differential stability evidence as an absolute witness.
- Add a fail-closed, report-only A/B/C/A RA-rotation witness producer for a
  disjoint plate-solving instrument. Require returned-A closure in both sky
  coordinates and persisted topocentric vectors so one representation cannot
  conceal a failed return.
- Require the entire independent witness arc, not only its final timestamp, to
  occur after TPPA completion, and bind witness uncertainty metadata to the
  same declared independent calibration digests before evidence is written.

## Version 2.2.6.60

- Capture VerificationOnly A/restoration coordinates from validated
  TelescopeInfo telemetry instead of GetCurrentPosition, which returned a
  zero right ascension during the 2026-08-01 field diagnostic.
- Fail closed on missing or non-finite mount telemetry before constructing the
  verification arc or attempting cleanup.

## Version 2.2.6.59

- Add an opt-in, report-only five-position small-circle model check to
  VerificationOnly by sampling two reciprocal half-leg positions while keeping
  the legacy polar-error result tied to the original outer three points.
- Report fit residuals, five distinct-position coverage, legacy-versus-fit axis
  separation, and leave-one-out axis stability without granting actuator or
  completion authority.
- Preserve the default nine-solve VerificationOnly contract when the shadow
  option is disabled, and fail validation when the option is selected outside
  VerificationOnly mode.
## Version 2.2.6.58

- Seal the complete independent-witness uncertainty payload with a canonical
  SHA-256 evidence digest and reject any post-derivation mutation.
- Bump the qualification receipt schema to version 3 so persisted receipts
  cannot silently omit the sealed uncertainty payload.
- State explicitly that the content digest is tamper evidence, not producer
  authentication or proof that field calibration bounds are physically valid.
## Version 2.2.6.57

- Require quantitative independent-witness uncertainty evidence instead of
  accepting qualification booleans and calibration digests alone.
- Add a conservative 95% witness budget: two-sigma measurement uncertainty
  plus linearly summed calibration, orientation, closure, frame, distortion,
  and mechanical bounds. Include that budget in the independent-error,
  agreement, and combined absolute-error gates.
- Bind the runtime uncertainty model to the same content-addressed witness
  input path and fail closed on missing, zero, non-finite, undersampled, or
  over-budget evidence.
## Version 2.2.6.56

- Make runtime qualification derivation total and fail closed for malformed or
  non-unit vectors, while defensively snapshotting asynchronous evidence before
  hashing or evaluation.
- Distinguish ICRS observation coordinates from topocentric north-west-up
  mount-axis vectors and reject out-of-range RA or mixed/unknown pier-side solve
  evidence.
- Let offline receipt verification bind the recorded source polar-error vector
  to an independently supplied expected digest, and state explicitly that the
  unsigned receipt proves content integrity rather than signer authenticity.

## Version 2.2.6.55

- Add a read-only runtime adapter that derives the fast true-pole qualification
  input from UTC solve evidence, fitted mount-axis vectors, returned-A closure,
  qualified site/weather provenance, and an independent calibrated witness.
- Compute repeatability, final error, and witness agreement as exact spherical
  vector separations while rejecting reused solves, stale inputs, physical
  adjustment commands, unknown pier side, and incomplete runtime identities.

## Version 2.2.6.54

- Add an independent parser and verifier for persisted fast true-pole
  qualification receipts.
- Recompute the frozen policy verdict plus the input, policy, and complete
  receipt hashes while permanently rejecting any receipt that grants motion.

## Version 2.2.6.53

- Add a guarded no-slew main-camera plate-solve series that records fixed-field
  solve motion while continuously enforcing tracking, pier-side, equatorial
  pointing, and balcony-envelope gates.
- Report tangent-plane RA/Dec drift slopes, vector slope, span, and linear-fit
  residual RMS directly in each stationary-series artifact.
- Extend the guarded telescope slew helper across the full wrapped balcony
  azimuth window while retaining the altitude and post-slew pointing gates.

## Version 2.2.6.52

- Deny automated polar-alignment actuator correction when the fresh initial
  error is non-finite or exceeds two degrees, while preserving measurement-only
  operation for diagnosis and manual coarse alignment.

## Version 2.2.6.51

- Let the next-session readiness checker require an existing UPAS bridge TCP
  session owned by the expected local client process. This avoids opening a
  second serial-over-TCP connection merely to test a bridge that is already in
  use, and reports that transport evidence does not verify GRBL health.
- Let the next-session readiness checker use and report an explicit ADB
  executable path, so Mele's fixed platform-tools installation is not confused
  with an absent P20.
- Extend the read-only next-session readiness check with independent fail-closed gates for the main camera, guide camera, and filter wheel instead of allowing iPolar and the UPAS bridge to mask an absent imaging train.
- Match the expected ZWO devices by friendly name or stable USB VID/PID so an unlabelled HID-class EFW still satisfies the correct gate.
- Suppress asynchronous TCP connection pipeline output and validate the bridge probe result schema before reading it.

## Version 2.2.6.50

- Compare reciprocal VerificationOnly results at the reciprocal exposure midpoint by interpolating the two forward signed error vectors using their frozen exposure-midpoint UTC timestamps.
- Distinguish signed-vector separation from scalar total-error magnitude change in logs and operator summaries.
- Fail reciprocity closed on non-UTC, unordered, degenerate, or non-finite observations and report the precise rejection reason.
- Add a read-only next-session readiness check for the tested plugin hash, NINA process state, iPolar USB enumeration, direct-USB UPAS serial presence, and optional P20 ADB visibility.

## Version 2.2.6.49

- Make true-celestial-pole targeting the default for new profiles and fail closed when automated axis correction or drift validation is requested with apparent-pole targeting.
- Emit structured `TPPA_RUN_PROVENANCE` with the target pole, estimated true/apparent offset, atmospheric inputs and source, automation scope, and run correlation ID.
- Preserve intentional apparent-pole measurement-only operation while removing its ability to silently satisfy true-pole automation claims.
- Report ordinary and verification-only three-point geometry scale/shape diagnostics and reject exactly degenerate plane fits; thresholds remain report-only pending field qualification.
- Rename the drift estimator's reported condition metric to weighted normal-matrix condition number and document its relationship to the design-matrix condition.
- Add a quantitative known-term inventory without combining unresolved systematic effects into a false RSS uncertainty.

## Version 2.2.6.48

- Migrate the Claude council seat from Fable to pinned Claude Opus 5 (`claude-opus-5`) at high effort while preserving safe plan/no-tools execution.
- Delegate generic bridge invocation and health checks to the codex-wide runbook and preflight, leaving the repository document as a TPPA-specific provenance and disclosure supplement.
- Replace the duplicate TPPA bridge health script with a thin repository-root wrapper so future global bridge repairs cannot drift from project-local checks.

## Version 2.2.6.47

- Replace unreliable Antigravity headless @path attachments with bounded inline file context and fail closed before the Windows command-line limit.
- Extend the council preflight to prove that the active Gemini bridge actually receives a nested plugin project file, preventing false-positive CLI-only health checks.
- Document recovery for missing Gemini file context and denied headless command attempts without weakening read-only permissions.

## Version 2.2.6.46

- Add the canonical Claude Fable and Gemini High council-bridge runbook, including repository provenance, read-only invocation, completion, troubleshooting, and update contracts.
- Add a one-command bridge preflight that verifies the canonical repository checkpoint, configured bridge sources, Python syntax, CLI versions, Claude authentication, and live read-only pings for both council seats.
- Require council sessions to repair and rerun failed, incomplete, timed-out, or stale seats instead of silently substituting or omitting a requested reviewer.

## Version 2.2.6.45

- Preserve the session rejection reason in incomplete report-only drift-validation results and mark unavailable estimates as `NaN` instead of emitting misleading zero-error values.
- Add regression coverage for missing refraction, explicit invalidation, and failed per-track qualification; drift-fit and acceptance thresholds are unchanged.

## Version 2.2.6.38

- Fail VerificationOnly validation and runtime qualification when a leg is shorter than the field-qualified 15-degree conservative floor or the effective point settle is shorter than 30 seconds.
- Validate live stationary mount telemetry after every VerificationOnly absolute slew and before any solve or next leg; reject disconnected, still-slewing, non-finite, or out-of-envelope telemetry.
- Preserve the existing predictive destination and pier-side preflight as an independent first gate, while treating actual post-slew altitude and azimuth as authoritative.
- Add regression coverage for the observed 56.44-degree altitude violation, envelope boundaries, unavailable telemetry, non-finite telemetry, and the qualified 15-degree/30-second configuration.

## Version 2.2.6.37

- Refuse `Evaluate -OutputPath` anywhere inside the campaign directory. The report was written after verification, so naming a preserved artifact, the event log, or the header destroyed the evidence the returned verdict had just certified as intact while still reporting `IntegrityValid=true`.
- Refuse `Finalize -ReportDirectory` when it is the campaign root or sits under `artifacts/`, so reports can never land on preserved evidence.
- Normalize every ISO 8601 date-time shape PowerShell 7 coerces, not only the `Z` form. An offset-bearing or zoneless timestamp pasted into any free-text field previously hashed differently on Windows PowerShell 5.1 and PowerShell 7, surfacing as a false "event was edited after it was written" rejection.
- Fail closed on evidence-verification facts: reject when facts are withheld while evidence exists, when they do not cover every recorded evidence event exactly once, and when a preserved artifact is relabelled external to escape the hard failure. Derive every reported count from the entries instead of trusting caller-supplied totals.
- Compare finalized report output byte for byte rather than as decoded text, so a report re-encoded with a byte-order mark or as UTF-16 is no longer accepted as identical.
- Pin finalization resume to the sealed `CampaignFinalized` event (qualification level, policy id, failed-gate count) and refuse resume on a non-terminal chain, so a lost report set cannot be rebuilt into reports that contradict the sealed result.
- Refuse linking a TPPA artifact whose SHA256 already appears in the campaign, matching the rule already applied to recorded artifacts.
- Remove a staging file when the copy or its verification throws inside the reservation, where the caller's cleanup could not yet run.
- Repair the concurrent-append regression test, which gave writers 2-6 a creation time later than their event timestamp and so rejected them before they reached the lock, swallowed every child exception, and never asserted that any writer committed: it passed with one writer and would have passed with the lock removed. Writers now report outcomes, all six eligible writers must commit, and event growth must equal the reported successes.

## Version 2.2.6.36

- Verify iPolar preserved evidence byte for byte: rehash and restat the staged and promoted copies at record time, and recheck every recorded dark frame and artifact against the bytes on disk during filesystem-aware evaluation and finalization.
- Reject a campaign and force `IntegrityValid` false when preserved evidence is missing, modified, size-changed, or unreadable, and refuse to finalize while any evidence fails verification.
- Classify linked TPPA run artifacts as external unpreserved evidence: later absence is a recorded limitation, while a still-present file that no longer matches its recorded hash fails closed.
- Keep `Invoke-IPolarCampaignEvaluation` pure by moving the filesystem check into a separate `Test-IPolarPreservedEvidence` verifier whose facts the evaluator consumes through `-ArtifactIntegrity`.
- Make finalization terminal: refuse every recording command once `CampaignFinalized` exists without mutating the log, and reject a duplicated or non-tail finalization during evaluation. Finalize resume remains the only permitted post-finalization operation and stays byte identical.
- Record artifacts transactionally through a per-invocation staging file promoted only after the event is appended, so a rejected timestamp or interrupted import leaves no orphan at the committed path and a corrected retry succeeds without manual cleanup.
- Serialize every campaign mutation with a bounded campaign-scoped interprocess lock, revalidate state under the lock immediately before append, and write event lines as durable UTF-8 without a BOM.

## Version 2.2.6.35

- Add a fail-closed external UPAS supervisor boundary shared by automated pre-seat and correction movement.
- Accept only the frozen authenticated HTTPS status V1 contract and reject unknown schemas, properties, modes, invalid sessions, locks, unavailable axes, and `physicalMotion=false`.
- Require the external supervisor by default for automated movement; retain legacy direct actuation only as an explicit compatibility opt-out, with no fallback from supervisor-required mode.
- Deliberately omit correction submission in this commissioning release, so even a future success-shaped capability response cannot enable physical motion.
- Add bypass, cancellation, malformed-contract, capability, credential, and dry-run honesty tests plus a threat-model handoff for the later transaction-client increment.

## Version 2.2.6.34

- Add a report-only robust timestamped trend-span diagnostic and frozen replay test that catches the July 2026 75-arcsecond same-arc walk hidden by median/MAD repeatability; runtime movement authorization remains unchanged pending field qualification.
- Bound each TPPA capture/plate-solve operation to five attempts and fail the sequence instead of retrying indefinitely.
- Emit schema-versioned JSON provenance for completed, failed, and cancelled solve attempts, using null rather than a speculative observation time when capture fails.
- Add physical field-centre drift sign invariants, correct stale drift-validation documentation, and classify Astropy/Rodrigues fixtures as model-conformance rather than independent absolute-accuracy evidence.
- Freeze the ccc3720 legacy estimator baseline and document claim levels, experiment isolation, provenance, and the external-witness qualification boundary.
- Add an offline, append-only iPolar corroborating-witness campaign recorder and evaluator with explicit no-actuation authority, hash-chained evidence, bounded qualification levels, and no ground-truth or absolute-certification claim.
- Make iPolar campaign hashes byte-identical across Windows PowerShell 5.1 and PowerShell 7, reject unsafe and reserved campaign identifiers, preflight every report path before finalization, and safely resume missing report output without appending another immutable event.

## Version 2.2.6.33

- Add a bounded, sequence-specific VerificationOnly point-settle override so directional-bias tests can change cadence without mutating the global mount profile.
- Reject every VerificationOnly absolute slew whose destination leaves the configured mount-motion envelope or predicts a known pier-side change.
- Make a failed VerificationOnly repeatability or reciprocity verdict fail the sequence item instead of completing with only a warning.
- Require the guarded launcher sequence to contain exactly one TPPA instruction and no trigger/condition nodes, reject parked or already-slewing mounts, bound guard arming, structurally parse compact status, and accept only a positive FINISHED result.

## Version 2.2.6.32

- Serialize report-only diagnostics against every in-flight UPAS/OAPA serial operation with a bounded, process-wide handshake; deny new discovery and movement and drain active polling before measurements start.
- Make actuator connection re-entrant-safe and bind each polling loop to its own controller instance and cancellation lifetime.
- Hard-bound every guarded-launcher NINA REST call, including mount checks and emergency sequence stop, with a reusable HttpClient plus an independent task deadline.
- Require PowerShell 7 for the guarded launcher and prevent a racing Disconnect from leaving stale connected UI state.
- Poll the compact Advanced API sequence JSON route during guarded runs, avoiding the image-heavy state payload that can block for minutes.

# Changelog

- Added a fail-closed guided-900-second readiness gate that validates the exact
  passive NINA sequence and live runtime manifest, rejects nominal policy
  templates, reproduces OAG geometry from immutable WCS sources, and requires a
  fresh passing TPPA operational report before acquisition.

## 2.2.6.68

- Add a strict request-specification record and an authority-free CLI command
  that creates each witness request through a create-new temporary file followed
  by an atomic `.witness-request.ready.json` publication.
- Add a persistent external observer service with a global single-instance
  lease, create-new per-request claim ledger, core validation before any
  request-derived path is used, and exactly one invocation of the one-shot
  observer. Service restart cannot replay a claimed request.
- Require a physical outcome artifact before the service may record `captured`;
  malformed, failed, late, or missing-outcome requests are preserved without
  motion or completion authority.

## 2.2.6.67

- Version the independently captured witness point receipt as schema 3 after
  adding its bound site coordinates and ASTAP field of view. The PowerShell
  producer and C# consumer now agree on the exact receipt contract.
- Add fail-closed tests for acquisition-site and solver-field-of-view mismatch
  so a derived topocentric vector cannot be bound to undeclared transform
  inputs.
- Add a one-shot out-of-process witness observer. It validates the immutable
  request, verifies observer and capture-script hashes, creates an attempt
  marker before capture, invokes the PHD2 path exactly once, and delegates
  canonical outcome creation and validation to the authority-free CLI.
- Preserve failed attempts without an outcome and prohibit reuse of their
  evidence paths; neither the observer nor the CLI grants motion or completion.

## 2.2.6.66

- Add a report-only request/outcome handshake contract that binds every witness
  point to one run, waypoint, mount command, nonce, observer hash, UTC deadline,
  and exactly one blind acquisition attempt. Late, replayed, retried, tampered,
  or mismatched outcomes fail closed and never grant motion or completion.
- Expose deterministic request/outcome validators through the headless
  qualification CLI so an out-of-process observer can enforce the same contract
  without loading NINA or receiving any movement authority.
- Bind site coordinates, exposure, ASTAP field of view, FITS timestamp bound,
  and the exact point-capture script digest into every request so the observer
  cannot silently change scientifically relevant acquisition settings.
- Persist the site coordinates and ASTAP field of view in every point receipt;
  the producer and binder now reject derived topocentric vectors whose declared
  transformation inputs differ from the bound witness metadata.

## 2.2.6.65

- Make the independent PHD2/ASTAP RA-rotation witness solve blind: commanded
  mount coordinates remain provenance only and are no longer passed to ASTAP
  as search hints.
- Version the witness point/acquisition schema and reject any receipt that does
  not declare the `blind-no-mount-hint` solver policy.

- Added a persisted, default-on five-minute runtime contract for automated
  UPAS alignment. It counts the complete sequence-item execution, reserves time
  before fresh determinations and moves, cancels at 300 seconds, and fails
  closed instead of merely warning and resetting the correction timer.

- Added a report-only PHD2/ASI220 plus external-ASTAP witness-point acquisition
  boundary for absolute TPPA qualification. It persists raw FITS timing and
  hashes, enforces stationary unguided capture state, uses unrefracted
  timestamped ASCOM/NOVAS topocentric vectors, restores guide output, and has no
  mount-motion endpoint.

## Version 2.2.6.31
- Added structured verification-point telemetry with exposure-midpoint UTC, mount azimuth/altitude, solved RA/Dec, traversal direction, and per-arc observation span.
- Clarified that the configured TPPA point distance applies to each of the two RA-axis legs in a three-point sweep.
- Hardened the guarded verification runner to require an explicitly enabled mount-motion envelope matching the requested balcony limits.

## Version 2.2.6.28
- Added a configurable mount-motion safety envelope that rejects TPPA arcs outside the known balcony azimuth/altitude opening.
- Added robust multi-run measurement-set evaluation using per-axis and total-error median/MAD gates.
- Exposed the mount-motion envelope controls in plugin settings.
- Added structured polar-error vector and phase diagnostics for fresh, reciprocal, and repeated-forward determinations.
- Added fail-closed same-arc cross-block consistency evaluation with independent component, magnitude, and circular phase thresholds.

## Version 2.2.6.27
- Made a rejected report-only drift estimate a hard sequence failure. Invalid geometry, track quality, global fit, uncertainty, stationarity, or repeated-position results can no longer display a rejection and then let the sequence item complete successfully.

## Version 2.2.6.26
- Hardened drift-validation tracking restoration. A restore failure no longer masks an existing diagnostic exception, while a restore failure after an otherwise successful acquisition now fails the sequence item instead of being silently ignored.

## Version 2.2.6.25
- Added independent plate-solve verification of the B and C arc legs. Each solved sky displacement must satisfy the same bounded RA travel and declination cross-axis gates as mount telemetry before that track can enter the drift estimator.

## Version 2.2.6.24
- Added current-time safety preflight immediately before each B/C relative RA move and the absolute return to A. Every leg now rechecks the destination's full five-minute track against the 30-degree altitude floor and known destination-side continuity, preventing the initial preflight from becoming stale during long dwells.

## Version 2.2.6.23
- Moved the rate-based RA-axis stop into a non-masking `finally` block. Success, timeout, cancellation, telemetry rejection, and unexpected exceptions now all attempt to send `MoveAxis(Primary, 0)` without replacing the original failure if the emergency stop command itself fails.

## Version 2.2.6.22
- Added independent plate-solve closure validation for report-only drift acquisition. The final A solve must return within 0.25 degrees of the first A solve before its metadata or samples can enter the estimator.

## Version 2.2.6.21
- Added fail-closed A-B-C-A closure verification for report-only drift validation. The final return must be within 0.25 degrees of the captured A pointing by great-circle separation and must not change a known destination pier side.

## Version 2.2.6.20
- Added fail-closed post-move verification for report-only drift arcs. Each relative RA move must now reach the expected distance without excessive overshoot, unexpected declination travel, or a known destination-side change; movement timeout now aborts instead of silently continuing.

## Version 2.2.6.19
- Wired conservative drift-arc preflight into report-only runtime. Before any movement it predicts both possible RA-axis sign interpretations through A-B-C-A, covers each five-minute track's start and end altitude, and refuses either sub-floor altitude or a known pier-side change.

## Version 2.2.6.18
- Added a pure fail-closed A-B-C-A drift-arc safety policy that rejects invalid position order, non-finite or sub-floor predicted altitude, and any known pier-side change before runtime movement wiring.

## Version 2.2.6.17
- Added synthetic east/west-hour-angle recovery coverage for every polar-error sign quadrant at both northern and southern site latitudes.

## Version 2.2.6.16
- Made the five-minute drift-validation defaults achievable with the runtime's 30-second solves and five-second cadence by requiring eight accepted samples instead of an impossible thirty, while retaining all duration, uncertainty, stationarity, geometry, and repeated-position gates.

## Version 2.2.6.15
- Made report-only drift validation restore the telescope's initial tracking-enabled state, reject active non-sidereal tracking instead of silently replacing it, and settle at both A arrivals before retaining the first solve.
- Documented in the runtime log that NINA 3.1 does not expose prior guiding-active state, so an Advanced Sequence must explicitly start guiding after this diagnostic.

## Version 2.2.6.14
- Made drift-validation hour-angle reconstruction epoch-consistent by combining of-date topocentric geometry with JNOW declination instead of catalog-epoch declination.

## Version 2.2.6.13
- Added the opt-in, report-only TPPA drift-validation instruction path. It performs four five-minute stationary solve tracks in A-B-C-A order using telescope RA movement only, reuses each metadata solve as the first drift sample, reports the independently fitted polar-error vector, and cannot configure, connect to, or move UPAS.

## Version 2.2.6.12
- Added a solve-derived runtime metadata factory for report-only TPPA drift validation. It uses the exposure-midpoint timestamp, reconstructs hour angle from vacuum topocentric geometry, computes atmospheric-refraction drift, and rejects tracks below the qualified altitude.

## Version 2.2.6.11
- Added fail-closed execution policy and validation for an upcoming report-only A-B-C-A drift-validation instruction mode. The mode is automated-mount-only, mutually exclusive with verification-only mode, and bypasses every UPAS actuator phase.

## Version 2.2.6.10
- Added a fail-closed, diagnostic-only A-B-C-A drift-validation coordinator. It enforces telescope-position order, captures metadata only after arrival, stops immediately on failed or cancelled tracks, and has no UPAS actuator dependency.

## Version 2.2.6.9
- Added a bounded, cancellation-safe, report-only stationary TPPA drift-track runner that uses exposure-midpoint solve times, preserves raw solves, and invalidates interrupted or failed acquisitions. It cannot slew the telescope or move UPAS.

## Version 2.2.6.8
- Added the first report-only TPPA A-B-C-A declination-drift validation components, including qualified raw-track fitting, fail-closed acquisition state, and a coordinate-transform-based atmospheric-refraction drift calculator. These components cannot move UPAS and are not yet wired into runtime acquisition.

## Version 2.2.6.7
- Added a passive PHD2 Polar Drift Align estimator and automatic read-only JSON/CSV/Markdown result artifacts based on raw GuideStep camera displacement, with PHD2-equivalent least-squares geometry, uncertainty and half-window consistency gates, and fail-closed rejection of guide pulses, guide-star/lock discontinuities, unresolved pixel scale, and short captures. This phase is measurement-only and cannot move UPAS.
- Added an automated-mount-only per-instruction verification mode that performs exactly two independent determinations over the same three-point arc, publishes their values and delta, restores A even after cancellation or failure (falling back to the pre-run pointing if A was not captured), and avoids polar-alignment actuator configuration, connection, movement, and disconnect side effects.
- Conservatively charged failed UPAS X commands to the azimuth travel budget and discarded stale seating/engagement state without learning from the failed move.
- Added fixed-window PHD2 DEC drift consistency diagnostics and withheld TPPA comparison slopes from curved or nonstationary captures.

## Version 2.2.6.6
- Changed Avalon UPAS automation to learn and plan only from independent fresh three-point measurements taken before and after each actuator move.
- Kept the continuous correction estimate display-only for UPAS automation so projection drift cannot train or steer the actuator response model.
- Prevented continuous-estimator instability from suppressing a UPAS move planned from the last valid fresh measurement.
- Required two consecutive independent fresh three-point results for automated UPAS completion, bypassing overlay-based completion gating.
- Added a fail-closed twelve-move limit for fresh-measured UPAS correction attempts.
- Hardened fixed-tripod diagnostics against stale fresh-result log markers and duplicate measurement rows.
- Preserved the original fresh-measurement failure when returning to the correction field also fails.
- Retried transient NINA sequence reload failures in the passive diagnostic supervisor.

## Version 2.2.6.5
- Added reproducibility context to passive same-solves diagnostics: observation time, latitude, target-pole altitudes and separation, and atmospheric parameters.
- Kept true/apparent-pole comparison diagnostic-only with no controller or target-selection behavior change.

## Version 2.2.6.4
- Added passive same-solves diagnostics for true-pole versus refracted-apparent-pole alignment targets.
- Preserved the existing fresh-result log contract and made alternate-target diagnostics non-fatal.

## Version 2.2.6.3
- Hardened UPAS/OAPA serial status parsing and command acknowledgement handling on slow or lossy links.
- Improved automated adjustment learning so sub-noise probes do not poison the response model.
- Improved automated correction scoring to avoid sacrificing an already-solved axis for a small cross-axis improvement.
- Prevented reference-star selection from feeding false motion samples into automated adjustment learning.
- Added a stateful UPAS azimuth engagement controller with bounded direction acquisition and response memory.
- Required consecutive above-noise evidence before early UPAS engagement confirmation or reversal, with at most one early reversal per acquisition episode.
- Warn when refraction adjustment is disabled and the apparent-pole offset exceeds the selected automated alignment tolerance.
## Version 2.2.6.2
- Fixed TPPA cancellation during plate solving so skipping the sequence item does not surface ASTAP sidecar cleanup errors.

## Version 2.2.6.1
- Fixed a continuous-solver correction-loop failure when star detection returns no star list while reacquiring the reference star.

## Version 2.2.6.0
- Reworked the plugin options page into tabbed sections with built-in workflow, accuracy, warning-state, and troubleshooting guidance.
- Added descriptive tooltips for plugin settings and supported hardware adjustment panels.
- Added a run checklist and contextual guidance to the polar alignment workflow.
- Improved the FAQ and plugin description to better explain prerequisites, recommended sky positions, correction behavior, and troubleshooting.
- Added an experimental continuous error estimator option while keeping the legacy live error calculation as the default.
- Improved the live correction overlay so target and component lines stay anchored correctly on the selected reference star.
- Added a warning for correction fields near exact east or west when the experimental continuous estimator is enabled.
- Improved automated hardware adjustments, including direction handling, backlash behavior, movement timing, and recovery from failed moves.
- Hid manual hardware controls while automated adjustments are active.
- Corrected the polar-alignment log path documentation.

## Version 2.2.5.0
- Replaced AAPA/Avalon checkboxes with a single ComboBox selector (None / UPAS / AAPA) per code review feedback
- Common settings (reverse axes, backlash, automated adjustments) now displayed based on the selected system
- Eliminated code duplication by extracting shared base classes and interfaces for polar alignment systems
- Removed redundant UsePolarAlignmentSystem boolean in favor of enum-based selection

## Version 2.2.4.3
- Polar alignment tab in imaging now correctly pulls the binning settings from the plate solve settings on startup

## Version 2.2.4.2
- When polar alignment is started, guiding will be stopped automatically

## Version 2.2.4.1
- Polar alignment progress is now sent via message broker using message topic `PolarAlignmentPlugin_PolarAlignment_Progress` for other plugins to consume.

## Version 2.2.4.0
- Removed the position angle spread warning as it was not giving any useful information
- Instead the declination spread that the driver is reporting is now measured and a warning is shown if it exceeds 2 arcseconds. The declination axis should not move at all during measurements.

## Version 2.2.3.8
- Log mount position when connected on each measurement point

## Version 2.2.3.7
- Fix messagebroker message parsing for filter name

## Version 2.2.3.5
- Fixed the window popout not closing automatically after the polar alignment was within the set tolerance

## Version 2.2.3.4
- Fixed manual mode to work again without a mount being connected

## Version 2.2.3.2
- `PolarAlignmentPlugin_DockablePolarAlignmentVM_StartAlignment` will now process the message content to be able to adjust parameters as needed

## Version 2.2.3.1
- Added message broker subscription to message topic `PolarAlignmentPlugin_PolarAlignment_ResumeAlignment` to resume the procedure
- Added message broker subscription to message topic `PolarAlignmentPlugin_PolarAlignment_PauseAlignment` to pause the procedure

## Version 2.2.3.0
- Added an option to auto pause between continuous exposures

## Version 2.2.2.2
- Fixed an issue when multiple polar alignment instructions were placed in the sequence with custom binning

## Version 2.2.2.1
- Fixed an issue when the UPA Gear Ratio is changed that it will not be initialized with the changed ratio in the next session

## Version 2.2.2.0
- Fixed an issue when a weather device is connected but reporting 0 hPa pressure

## Version 2.2.1.0
- Added message broker broadcast for alignment error using message topic `PolarAlignmentPlugin_PolarAlignment_AlignmentError`
- Added message broker subscription to message topic `PolarAlignmentPlugin_DockablePolarAlignmentVM_StartAlignment` to start the procedure
- Added message broker subscription to message topic `PolarAlignmentPlugin_DockablePolarAlignmentVM_StopAlignment` to stop the procedure

## Version 2.2.0.1
- After slewing to the first point, added an explicit wait for the dome synchronization if a dome is connected

## Version 2.2.0.0
- Refraction correction will now be properly applied and the option `Adjust for refraction` should now correctly align to the true pole
- Observer elevation is now considered for all transformations

## Version 2.1.0.2
- Fixed an issue when using the UPA that the direction would constantly be reversed on each adjustment.
- When using the UPA it will no longer move a last time without re-evaluation when the alignment threshold has already been reached.
- Added options for UPA to reverse azimuth and altitude axes

## Version 2.1.0.1
- Polar Alignment Tolerance can now be set on instruction level. For example when you are running an automated polar alignment run and want to dial in the polar alignment in multiple phases and getting more precise in each step.
- Now showing UPA positions in automatic mode in addition to the already existing nudge direction

## Version 2.1.0.0
- The position angle spread between the three measurements is now measured. If it is too large, a warning will be shown.

### Integration for the [Avalon Universal Polar Alignment System](https://www.avalon-instruments.com/products-menu/accessories/universal-polar-alignment-system-detail)

#### New Setting: `Use Avalon Polar Alignment System?`
- When activated, the polar alignment routine will connect to the unit automatically after the third step, allowing you to remotely adjust the altitude and azimuth of your system.

#### New Setting: `Do automated adjustments?`
- When activated, this will connect to the UPA and slowly nudge the UPA to the target position automatically after the error has been determined. The control panel will not be shown as movements are done automatically.
- Ensure your gear ratio settings are roughly matched so that one step in the UPA results in an arcminute of movement. The default settings should work fine for the standard version of the UPA.
- Make sure your mount is roughly leveled.
- *Note: For this setting to work, you also need to set the `Polar Alignment Tolerance` to a non-zero value.*

## Version 2.0.2.0
- Automatically increase search radius on plate solve by 5 during solving of the first three points each time it fails

## Version 2.0.1.0
- After automated move to next point, wait for the telescope to indicate it is no longer slewing
- Use Snapshot mode for taking images during polar alignment

## Version 2.0.0.3
- Fixed issue where the TPPA instruction with a filter set would override the autofocus exposure time

## Version 2.0.0.1
- Fixed issue with serilog when PA error logging was enabled

## Version 2.0
- Updated plugin to work with latest major N.I.N.A. version

## Version 1.7.2.0
- It is now possible to pause in between the steps and continue after making the adjustments. Useful in case your image downloads and solves take a while.

## Version 1.7.1.0
- Add an option to continue tracking when TPPA is done. Use with caution to not run into pier collisions!
- Prepopulate the filter with the platesolving filter for defaults
- When refraction correction is enabled, the pole will now also be corrected for it to determine the initial error

## Version 1.7.0.0
- Show a loading spinner while a new image is waiting for a solve to update the error details. The spinner is shown in the total error details.
- Changed the error circle indicator to draw based on the image scale at 30 arcseconds, 1 arcminute and 5 arcminutes
- When latitude and longitude is set to 0 it was most likely never set (as these coordinates are inside the Atlantic ocean). A validation will now check for this and notify to set these values.
- Add a warning when initial error exceeds 2 degrees, that the adjustment phase will be error prone and that it is advised to run it again once the error was reduced
- A further warning when the error exceeds 10 degrees is shown, that the mount is too far off, the location is incorrect or that the RA axis was not moved exclusively

## Version 1.6.3.0
- Added a reset to defaults button
- Added an alignment tolerance to automatically finish polar alignment when below the given threshold

## Version 1.6.2.0
- Fixed an issue where the polar alignment would fail when output logging was enabled

## Version 1.6.0.0
- Enhanced the scaling of the error text for smaller resolutions
- Added an option to account for refraction (which needs further testing in live conditions)

## Version 1.5.3.0
- Gain should now be prepopulated by plate solve gain setting

## Version 1.5.1.0
- Added dome support by waiting for the dome to sync after moving the axis for both automated mode as well as manual mode when both the mount and dome is connected
- Improved manual mode when mount is connected to only get a plate solved image after movement is complete
- Adjusted status report slightly

## Version 1.5.0.0
- When moving near the pole in automated mode and having multiple degrees of PA error, the warning that the mount did not move far enough was shown, even when the mount did indeed travel far enough
- This was caused by comparing the actual solved image RA with the starting RA, but now it will compare the drivers reported RA where the mount thinks it is
- Comparing the actual solved RA does lead to this error, as the axis of the mount is shifted and the circle is not perfectly aligned with the pole
- Fixed an issue when solving succeeded, but star detection did not detect any stars, that the algorithm should no longer fail but use the center of the image instead

## Version 1.4.1.0
- With nightly 1.11 #165 the star detector became incompatible. This version will make it compatible again.

## Version 1.4.0.0
- The plugin now logs the amount of error into `User Documents >> N.I.N.A >> PolarAlignment` when activated in the options
- Added validation when telescope is connected but at park
- Fixed that filter is not saved when saving the instruction as part of an advanced sequence

## Version 1.3.7.0
- In addition to left/right the error display will also include east/west
- Fixed that the altitude error for southern hemisphere was flipped
- Added a toggle to be able to start from the current mount position instead of slewing to a specific alt/az
- Added an expander to the imaging tab tool panel to collapse the options

## Version 1.3.6.0
- Added the individual steps as progress and mark them visually as completed to give the user a better indication of the completion of individual steps
- Added a new color option for the completed steps color

## Version 1.3.5.0
- The manual mode now also works in full blind mode without any telescope connection. A blind solver needs to be setup.
- Added the validation messages to imaging dock to see why the routine cannot be started

## Version 1.3.4.0
- Adjusted plugin description with new markdown syntax

## Version 1.3.3.0
- Fix DefaultAzimuthOffset to be correctly applied in the southern hemisphere as azimuth 180° + offset (instead of 0° + offset)

## Version 1.3.2.0
- Remove the compensation when the automated slew did not reach the expected distance. The various mount drivers differ too much to determine a clever compensation model
- Instead the slew timeout factor can be adjusted. See the [FAQ for details](https://bitbucket.org/Isbeorn/nina.plugins/src/master/NINA.Plugin.Notification/NINA.Plugins.PolarAlignment/FAQ.md)
- In manual mode, wait for the telescope to not report *slewing* before trying to solve

## Version 1.3.1.0
- Improved the target distance check for more tolerance and better compensation

## Version 1.3.0.0
- Added a new "Manual Mode", for mounts that are either no goto mounts or do not implement the necessary interfaces for automated point retrieval
- Further refactoring to reduce code duplication

## Version 1.2.2.0
- Added a check, when the target distance was not reached within one degree to reslew again until the target distance is reached. This can happen when the move rate is less than advertised inside the mount driver.
- Fix an issue when running Three Point Polar Alignment on the imaging tab that it won't be started again after the first iteration.

## Version 1.2.1.0
- Reveal "Default Altitude Offset" and "Default Azimuth Offset" to alter the initial coordinates that are getting preset
- Optimize some of the default settings
- Internal refactorings to reduce code duplications as well as layout improvements
- Check if the camera is free to use when starting the routine out of the imaging tab. If the camera is in use, the play button will be disabled.
- When starting the polar alignment out of framing the camera will be blocked during the routine, to not allow other areas to take control of the camera.

## Version 1.2.0.1
- Fixed an issue when moving the axis would traverse over 24h right ascension - leading to an incorrect distance moved

## Version 1.2.0.0
- The plugin is now also available in the imaging tab to be started directly there instead of inside the sequence.
- A new button inside the tools pane in the imaging tab on the top right is available to open the polar alignment tool

## Version 1.1.0.0
- Complete rewrite of the error determination and correction logic to allow for locations further off from celestial pole and meridian
- Show the initial error amount in smaller numbers below the adjusted error
- Display a shadow rectangle showing the original error for reference behind the adjustet error rectangle

## Version 1.0.0.8
- Added a dedicated changelog file to the repository
- Fix: When using debayered images the plugin would close on the final step with an error

## Version 1.0.0.7
- Fix: Azimuth error could sometimes exceed 180° instead of showing a negative error instead

## Version 1.0.0.6
- Fix: Azimuth error for southern hemisphere was calculated incorrectly

## Version 1.0.0.5
- Initial release using the new plugin manager approach, making the plugin available for download inside N.I.N.A.
