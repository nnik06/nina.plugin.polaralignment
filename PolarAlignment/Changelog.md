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
