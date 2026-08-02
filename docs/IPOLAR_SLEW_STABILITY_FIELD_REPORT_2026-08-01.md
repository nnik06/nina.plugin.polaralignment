# iPolar Slew Stability Field Report - 2026-08-01

## Scope and status

This campaign tested whether the mount-attached iPolar optical axis remains
stable while the telescope changes aim and pier side. It also qualified a
trajectory-aware balcony slew guard. Results are engineering evidence, not an
absolute polar-alignment calibration.

Hard telescope envelope used for normal operation:

- azimuth: 270..360 degrees or 0..10 degrees
- altitude: 25..55 degrees

No UPAS movement was commanded during this campaign.

## Qualified evidence

### Continuous iPolar star arcs

The iPolar app was captured with `PrintWindow` at 1295x735 and nominal 500 ms
cadence while NINA mount telemetry was logged for every frame.

1. First direct cross-pier slew, run `20260731T211615.996Z`:
   - 27 unique in-slew positions of one bright star.
   - Circle residual: 0.402 px RMS, 0.889 px maximum.
   - At 30.87742981 arcsec/px: 12.4 arcsec RMS, 27.5 arcsec maximum.
   - The route itself was unsafe and is quarantined for operations because it
     reached Alt 24.06 degrees and Az 14.31 degrees.
2. Home-to-east-pier slew, run `20260731T221335.220Z`:
   - Endpoint Az 330.15, Alt 50.00, pier east.
   - 15 unique positions of one bright star.
   - Circle residual: 0.410 px RMS, 0.775 px maximum.
   - At the same scale: 12.7 arcsec RMS, 23.9 arcsec maximum.
   - All 141 watchdog samples stayed inside the balcony envelope.
3. East-pier Az 330 to Az 300 slew, run `20260731T222329.287Z`:
   - Only four unique usable bright-star positions; no free circle fit is
     qualified.
   - Relative to the independently fitted screen-plane center, radial scatter
     was 11.5 arcsec RMS with a 31 arcsec full span.

The first two independent long arcs agree: observed in-motion iPolar
screen-plane radial stability is approximately 13 arcsec RMS and below 30
arcsec maximum for these runs. This does not establish absolute iPolar pole
accuracy.

### DEC-only load changes

The iPolar camera is fixed to the RA housing. DEC-only slews therefore change
telescope load without intentionally rotating the iPolar camera.

- West-pier DEC +80 to +70, run `20260731T220029.713Z`:
  - two common stars only;
  - median radial delta -60.2 arcsec;
  - sparse evidence, not qualified as a flexure magnitude.
- West-pier DEC +70 to +80, run `20260731T220428.619Z`:
  - two common stars only;
  - median radial delta +8.6 arcsec, with disagreement between stars;
  - does not reproduce the outward magnitude.
- East-pier DEC +56.1 to +75.1, run `20260731T221750.759Z`:
  - four common stars;
  - median radial delta +14.9 arcsec;
  - radial-delta MAD approximately 3.5 arcsec.
- East-pier DEC +75.1 to +56.1, run `20260731T222017.342Z`:
  - four common stars;
  - median radial delta -6.0 arcsec;
  - radial-delta MAD approximately 1.4 arcsec.
- East-pier cycle net closure: approximately +8.9 arcsec, below the roughly
  12 arcsec differential-witness noise floor established by the slew arcs.

The east-side cycle is therefore a null result: DEC-load hysteresis was not
resolved above the iPolar differential-witness floor. It should be treated as
an upper bound at roughly 15-25 arcsec, not as an 8.9 arcsec flexure
measurement. It does not support tripod flexure at the multi-arcminute scale.
The west-side result remains inconclusive because only two stars were usable.

## Safety findings

### Endpoint-only checks are insufficient

A direct high-declination pier-change target had a safe endpoint, but the
driver routed through unsafe altitude. The watchdog stopped the slew when it
sampled Az 357.49, Alt 17.94. The mount coasted/stopped at approximately Alt
16.14. The observed stop-command latency/overshoot was therefore about 1.8
degrees in altitude.

Operational consequence:

- Never perform a direct pier flip or cross-meridian slew on the balcony based
  only on endpoint checks.
- Maintain at least 2 degrees of dynamic margin from a hard path boundary when
  relying on the API stop route.
- Change pier side through mount Home, then slew from Home to a guarded safe
  endpoint. Home recovery ended at Az 0, Alt 25.116, `AtHome=true`.
- The subsequent Home-to-Az330/Alt50 east-pier route stayed safe.

A later same-pier return from Az 275/Alt 45 to Az 330/Alt 50 also proved that
same-pier endpoints are not enough. The watchdog sampled Az 311.69, Alt 56.16
and issued stop, but the driver continued to the final safe endpoint Az 330.19,
Alt 50.00. This route is operationally failed even though the endpoint command
reported success.

Additional operational consequence:

- Treat the API stop as a best-effort brake, not a safety interlock.
- Do not command unqualified large same-pier slews near the upper altitude
  boundary.
- Qualify short waypoints with dynamic margin before using a route unattended.
- A successful endpoint does not erase an unsafe trajectory sample.

### Trajectory watchdog

`ipolar_trajectory_guard_20260801.ps1`:

- polls NINA mount state at nominal 150 ms cadence;
- logs Az, Alt, pier side, tracking, slew state, and safety state;
- calls `/v2/api/equipment/mount/slew/stop` on the first breach;
- waits for a real `Slewing=true` transition before disarming;
- now requires two continuous idle seconds before recording final state.

The stop endpoint was verified from the deployed Advanced API assembly as:

`/v2/api/equipment/mount/slew/stop`

## Failures and workarounds

1. The first guard dry run disarmed before a delayed slew began.
   - Fix: require observation of `Slewing=true` before accepting idle.
2. NINA can report `Slewing=false` before final coordinate telemetry catches
   up.
   - Fix: require a continuous two-second idle interval before disarming.
3. A direct URL containing query-string ampersands was parsed by the local
   PowerShell shell as background jobs and never reached NINA.
   - Fix: use parameterized remote `.ps1` launchers; do not inline multi-query
     URLs through nested shells.
4. One DEC-only recorder was started too early and expired as the slew began.
   - Fix: launch recorder and watchdog immediately before the movement command;
     retain the failed run only as protocol evidence.
5. iPolar exposure selector values are milliseconds. Selecting `2` means 2 ms,
   not 2 seconds.
   - Fix: restored and verified 500 ms, the app maximum; the 2 ms run is
     quarantined.
6. The iPolar vendor solve state is intermittent during motion and sometimes
   reports too few stars or a failed solve.
   - Workaround: analyze raw bright-star centroids and circular-arc residuals;
     do not treat vendor solve text as the in-motion observable.

## Artifacts

- `ipolar_slew_capture_20260801.ps1`: synchronized app-frame and mount logger.
- `ipolar_trajectory_guard_20260801.ps1`: live route guard and stop action.
- `guarded_ipolar_azalt_slew_20260801.ps1`: guarded Az/Alt target launcher.
- `guarded_ipolar_radec_slew_20260801.ps1`: parameterized RA/DEC launcher.
- `analyze_ipolar_dec_load.py`: matched-star DEC-load analysis.
- `ipolar-dec-80to70-analysis.json`
- `ipolar-dec-70to80-analysis.json`
- `ipolar-east-dec56to75-analysis.json`
- `ipolar-east-dec75to56-analysis.json`

Raw runs are preserved on Mele under:

`C:\Users\nnik0\Documents\UPAS\field-20260801\ipolar-slew-stability`

Watchdog evidence is preserved under:

`C:\Users\nnik0\Documents\UPAS\field-20260801\ipolar-trajectory-guard`

## Dawn repeat attempt and next dark sequence

A later interactive baseline used the reusable `PrintWindow` recorder with
simultaneous NINA mount telemetry. It captured 34 live frames in 20 seconds,
but dawn sky brightness left only a smooth gradient. High-pass analysis found
no repeatable stellar centroids beyond fixed overlay/noise. No slew was
performed because optical-axis stability would have been unobservable.

The mount was then restored with tracking off, parked, the flat panel closed
with its light off, and mount/flat-device connections released. The rejected
baseline remains useful evidence that live frames alone are insufficient: a
dark-sky star-observability gate must pass before spending any trajectory.

The next dark run is prequalified as reciprocal load cycles, not a direct pier
flip:

1. West pier: approximately Az 0.6/Alt 35 to Az 1.2/Alt 45 and back.
2. Return through mount Home and verify Home plus idle.
3. East pier: approximately Az 329.7/Alt 49.6 to Az 349.0/Alt 36.7 and back.
4. Recompute RA/Dec at run time, validate destination pier side, use short
   guarded legs, and keep the synchronized iPolar/telemetry recorder running.
5. Require a dark baseline with stable star centroids before the first slew;
   otherwise reject the campaign without moving the mount.

### Daylight staging at 2026-08-01 06:45 Asia/Dubai

No new stellar slew evidence was collected after dawn. The Mele was reachable,
but iPolar was not running and NINA reported the mount and flat device
disconnected. The rig was therefore not connected or moved.

`tools/run_guarded_ipolar_pier_side_campaign.ps1` now packages each reciprocal
same-pier pair as an immutable phase. It deliberately cannot automate the pier
transition. The tested copies and all dependencies were hash-verified under
`C:\Tools\TPPAField` on MeleQ4C. A live disconnected preflight failed before
motion with `Stage start requires a connected, tracking, unparked, idle mount.`
This proves the deployment and first motion gate, not stellar stability.

## Reproducible next-run evaluation

The guarded field runner now invokes `tools/ipolar_slew_axis_evaluator.ps1`
after each accepted leg. The evaluator verifies every frame hash, uses only the
recorded `Slewing=true` interval, tracks a stellar centroid, rejects sparse or
short arcs, and writes `axis-evaluation.json` with fitted-axis, RMS, maximum,
and angular-span evidence. A separate manifest-based
`tools/ipolar_pier_side_campaign_evaluator.ps1` compares the four reciprocal
leg centers and fails when their maximum separation exceeds 30 arcseconds.

Retrospective machine replay after dawn kept the evidence fail-closed. The
west DEC leg had only four in-slew frames and the east outbound leg had eight,
both below the ten-frame minimum. The east return had eleven frames but only a
0.31-pixel tracked path, consistent with a stationary artifact rather than a
usable stellar arc. The longer Home-to-east leg produced 19 verified points,
54.37 degrees of fitted angular span, 15.01 arcseconds RMS and 25.39 arcseconds
maximum residual; it missed the strict 15.00-arcsecond RMS gate by 0.01
arcsecond and remains unqualified. The runner now defaults to a configurable
200 ms frame cadence so short dark-sky legs can satisfy the evidence count
without weakening any residual threshold.

These tools make the prior one-off circle analysis reproducible. They qualify
only differential optical-axis stability and explicitly cannot certify iPolar
absolute pole accuracy or authorize UPAS movement.

## Windows session-isolation correction

The 07:28 daylight readiness check found a repeatable launch failure that would
otherwise have blocked the next dark campaign:

- NINA and iPolar were running in the logged-in Windows console session 1.
- SSH commands ran in non-interactive session 0.
- `Get-Process` from session 0 could see the iPolar process but reported no
  capturable main-window handle, so an SSH-launched recorder failed closed with
  `iPolar has no capturable main window`.
- The iPolar USB device itself remained present and healthy as
  `iOptron iPolar 1.1`; this was not a camera or cable failure.

The operational fix is `tools/invoke_interactive_ipolar_campaign_stage.ps1`.
It launches each west/east/finalize stage through a bounded Windows scheduled
task using `LogonType Interactive`, verifies a fresh task run and zero task
result, requires the immutable stage artifact, hashes that artifact, and writes
an immutable launch receipt. `ipolar_slew_capture.ps1` now selects only a
visible iPolar window in its own Windows session, preventing a stale or hidden
process in another session from being captured accidentally.

The deployed hashes after the fix were:

- interactive launcher:
  `8574C7479498C8EC6BB4B9C43F7AD59C26D1677800971D3CE6FF4ED7ACA8F0B9`
- session-aware recorder:
  `F52F2CD4DFF6147E75FC95E6219DA0C3521FE4561C2CECDE1290135B78CEE489`

A no-motion functional proof ran the recorder in console session 1 for ten
seconds. It returned task result zero and wrote 17 synchronized samples. The
daylight observability gate then rejected the evidence with zero detected
stars and no UPAS/absolute authority. This is the expected fail-closed result.
Do not invoke the iPolar recorder or campaign runner directly over SSH.

## Interactive iPolar readiness preflight

The same Windows-session boundary also applies to vendor-app controls. The
iPolar 2.92 canvas does not expose its Connect control through UI Automation,
and a session-0 process cannot operate the console-session window reliably.
Two bounded tools were added:

- `tools/invoke_ipolar_ui_action.ps1` restricts automation to named actions,
  selects only the visible iPolar process in its own session, records the
  target window/point and input method, and grants neither mount nor UPAS
  authority.
- `tools/invoke_interactive_ipolar_ui_action.ps1` launches that action with an
  interactive scheduled-task principal, bounded execution time, fresh action
  JSON, step trace, post-action desktop PNG, SHA256 hashes, and task cleanup.

Native-pixel evidence corrected an early coordinate error: the Mele desktop
capture is 1280x720 even when the chat renderer displays it larger. The actual
Connect center is approximately desktop `(149,199)`, corresponding to iPolar
client point `(68,94)`. A traced run placed the pointer at `(151,200)` and
Windows accepted a conventional 120 ms down/up pair. The vendor app did not
change state. Repeating at task run level `Highest` also made no difference,
so Windows integrity-level isolation is not the cause. The read-only final
snapshot still showed Connect, and no previous-dark selection was possible.

Operational consequence: a receipt saying that input was delivered is not an
iPolar-readiness claim. Before any night slew, require a fresh screenshot in
which Connect has disappeared and the preserved previous dark has been
selected. If that proof is absent, stop before mount connection or motion and
request the one manual vendor-app interaction rather than looping synthetic
clicks.

## Current conclusion

Across qualified arcs, the iPolar image axis remained circular to roughly
13 arcsec RMS and below 30 arcsec maximum during slewing. The east-pier DEC
load cycle closed below that differential-witness floor. These results make
tripod/rig flexure of several arcminutes unlikely during the tested motions,
but do not resolve a smaller mechanical term.

The measurements do not reconcile iPolar with TPPA absolute polar error and do
not upgrade either method to sub-arcminute absolute authority.

## 10:25 pre-dark deployment audit

The current reciprocal pier-side campaign implementation passed 27/27 focused
PowerShell tests. MeleQ4C was reachable, but NINA and iPolar were not running,
so no daylight mount connection or movement occurred. All eight executable
campaign dependencies under `C:\Tools\TPPAField` were hash-identical to the
tested canonical copies, including the interactive launcher, campaign runner,
trajectory runner, 200 ms recorder, star gate, per-leg axis evaluator,
four-leg evaluator, and guarded balcony slew helper.

The active after-dark campaign remains `ipolar-pier-side-20260801`. It cannot
start until a fresh console-session frame proves iPolar Connected, previous
dark selected, and distinct stars observable. The west and east phases remain
separated by a verified mount Home transition; neither the campaign nor its
evaluators grants UPAS or absolute-polar-alignment authority.

The complete plugin suite passed 511/511 after the five-minute automated
runtime contract was integrated. A subsequent verification command briefly
failed with WPF `MC1000` because two `dotnet test` processes were launched in
parallel against the same `PolarAlignment/obj` markup cache. The same focused
tests passed 11/11 when rerun serially. `AGENTS.md` now records serial builds as
a standing repository rule so this tooling contention is not mistaken for a
field or plugin failure in a later session.

## Pre-field council and release closure

Claude Opus 5 High and Gemini 3.1 Pro High both accepted the strict runtime
policy as non-blocking and both identified the same blocker in the iPolar
campaign: final evaluation recalculated each leg-result digest but did not
compare it with the digest sealed into the phase manifest. Version 2.2.6.62
closes that gap and adds a mutation test. The complete iPolar suite now passes
28/28 and the plugin suite passes 511/511.

The runtime deadline now has its own cancellation source. Window/user
cancellation remains cancellation; only expiry of the armed deadline is
converted into an explicit sequence failure. The supported claim remains
narrow: an enforcing automated run cannot report success after 300 seconds
from sequence-item entry. A non-cooperative hardware or solver call can delay
the failure response, and the runtime contract does not prove alignment
accuracy.

The iPolar camera is mechanically fixed to the mount body, so changing pier
side does not rotate the sensor frame with the OTA. Raw fitted-axis centers are
therefore comparable in that mount-fixed pixel frame. The four-leg campaign
still cannot distinguish pier-side load from monotonic time drift or the Home
transition using only one west block followed by one east block. Its result is
differential evidence for the tested trajectories under the configured plate
scale, not absolute polar-alignment or UPAS authority.

Mele deployment was performed while NINA was closed. The verified field
artifacts are:

- TPPA 2.2.6.62 DLL SHA256
  `B0EE2459A55FF887B9AD388A345AEB3DFEBA1EDF8FFD95C7EAA0188A0E836EBB`;
- pier-side evaluator SHA256
  `0F5CD69C39BAAB42C6656C9CEFECC4984FB18A320676F5C89C15A05F6F130194`;
- interactive campaign launcher SHA256
  `8574C7479498C8EC6BB4B9C43F7AD59C26D1677800971D3CE6FF4ED7ACA8F0B9`.

Timestamped rollback copies of the prior DLL and evaluator were preserved on
Mele before replacement.

## Axis-center conditioning hardening

The initial circle-fit qualification bounded radial residual but not the
uncertainty of the fitted center. A leave-one-out test demonstrated why that
distinction matters: a synthetic approximately 45-degree arc with only 0.05 px
radial noise still moved the fitted center by 20.08 arcsec RMS and 53.33 arcsec
maximum when one point was removed. It therefore cannot support a 30-arcsec
center claim despite its small radial residual.

Version 2.2.6.63 adds per-leg leave-one-out center stability and rejects values
above 15 arcsec RMS or 30 arcsec maximum. A short low-residual arc is now an
explicit negative test, while an approximately 90-degree synthetic arc is the
positive conditioning oracle. The iPolar suite passes 29/29 and the plugin
suite remains 511/511.

The currently verified Mele artifacts are:

- TPPA 2.2.6.63 DLL SHA256
  `BC7BCB1DE77A422D678E77727C2D51389510941DF15D668655FB7EF7E7E1B60A`;
- slew-axis evaluator SHA256
  `425906D29AE04A4AD278484119D5C9CC92EFEB0B570C9C3832BC48AA77108BBA`;
- pier-side evaluator SHA256
  `0F5CD69C39BAAB42C6656C9CEFECC4984FB18A320676F5C89C15A05F6F130194`.

Tonight's trajectory is not presumed to have enough angular span. It must pass
the new conditioning gate from its own captured stars or remain unqualified.

### Jackknife scaling correction

The follow-up council agreed that leave-one-out influence is a useful
conditioning diagnostic, but Claude identified that raw leave-one-out RMS is
not a center uncertainty and shrinks with sample count. Version 2.2.6.64 now
computes the two-dimensional jackknife center standard error as
`sqrt((n-1)/n * sum(|center_i - mean(center_i)|^2))`. The per-leg gate is 15
arcseconds, conservatively below the 30/sqrt(2) = 21.2 arcsecond equal-share
budget for comparing two independent centers. Maximum one-point influence
remains a separate 30-arcsecond guard.

This statistic measures fit conditioning and single-point leverage. It remains
blind to common-mode bias, optical distortion, an incorrect plate scale, and
time-correlated motion. Those limitations prevent it from becoming absolute PA
authority.

The final verified Mele artifacts superseding the interim 2.2.6.63 pair are:

- TPPA 2.2.6.64 DLL SHA256
  `52161B7B6EA09725483548F5CC4CD7E78F878DCA71FFEA0610D19EAB126ED68E`;
- jackknife slew-axis evaluator SHA256
  `E8D4830D378835C726F9194CB828F0F98D5923BD761FD435C2C5FA37A6689D24`.

The complete plugin count remains 511/511. The 2026-08-01 pre-dark rerun of
every `ipolar*.tests.ps1` file passes 30/30, including 62 campaign-evaluator
permutations.

## Guarded pier-side capture provenance

Before the reciprocal pier-side run, the 200 ms iPolar recorder was hardened
to create an immutable capture profile containing the source process, session,
window handle and title, executable path and SHA256, exact 1295x735 window
rectangle, DPI, declared display mode, and capture method. Every synchronized
sample binds the capture-profile digest, and recording fails if the source
process, session, window, dimensions, or DPI changes.

The recorder passed 30/30 focused PowerShell tests. The tested script SHA256 is
`8EA68A710D6602B90A8CC51E2BB626CCA9E5BDDB556ECD9E18554271ED92EA36`.
That exact artifact is deployed at
`C:\Tools\TPPAField\ipolar_slew_capture.ps1`; a timestamped rollback was
preserved before replacement.

Claude Opus 5 High and Gemini 3.1 Pro High independently approved tonight's
campaign only as report-only differential evidence. Both denied UPAS motion
authority and an absolute 30-arcsecond claim. The supervisor-side receipt was
subsequently hardened so target-circle acquisition and plate-solve success are
independent booleans and both are required continuously. The screen extractor
also now rejects wrong-size captures and blurred red-cross halos without a
stable maroon target core. It seals neutral-star centroids and requires at
least five of the same stars to persist across each evidence window, so
changing noise cannot satisfy liveness through star count alone. Its focused
tests pass 72/72 and the adjacent supervisor suite passes 960 tests with 2
skips. Receipt schema v2 also rejects clustered, duplicate, and out-of-frame
centroids; cross-frame matching is injective and mutual-nearest, closing the
reviewed many-to-one liveness bypass. The loaded calibration binds exact frame
dimensions, while the surviving identity set must retain at least five stars
and qualified two-dimensional spread.

The supervisor now also owns campaign count, chain tip, deadline, per-axis
history, and pending-command state in one persistent controller. It releases a
command only once through a process-local HMAC authorization and faults closed
after failed execution validation. This closes caller-state reset and simple
replay paths in unit tests, but it does not authorize field movement: the real
actuator boundary and restart behavior are not yet commissioned. This campaign
therefore remains report-only.

The active campaign remains
`ipolar-pier-side-20260801`: west reciprocal load cycle, verified Home
transition, then east reciprocal load cycle. Each leg requires a fresh
interactive-session iPolar profile, distinct stars, trajectory compliance,
actual slew observation, two seconds of idle closure, and a well-conditioned
fitted center. Any failed condition rejects the campaign without granting UPAS
or absolute polar-alignment authority.

## 16:26 pre-dark integrity recheck

MeleQ4C was reachable and reported 2026-08-01 16:26 Asia/Dubai. NINA and
iPolar were not running, so no daylight connection, capture, or motion was
attempted. The live TPPA DLL reported version 2.2.6.67 and SHA256
`CBA3E6C646A1EFB44F560D56603025466592E47F4D6DEDD021FDFC4619CBD69D`.
All eight campaign dependencies under `C:\Tools\TPPAField` were present and
hash-identical to the tested canonical files: interactive launcher, campaign
runner, guarded leg runner, 200 ms recorder, star gate, guarded balcony slew,
axis evaluator, and four-leg evaluator. The full local iPolar Pester selection
passed 30/30 before the dark run.

## 17:16 shadow-admission integrity recheck

The supervisor-side iPolar shadow adapter now compares the complete numeric
calibration digest carried by the signed command plan, rejects offset-sign
disagreement explicitly, and binds the derived angular request, uncertainty,
supervisor calibration, and raw-unit comparison into a separate derivation
SHA-256 used by provenance and idempotency. Replay tracking is scoped by
controller key epoch and command binding.

The adapter remains report-only: source checks and architecture contain no
supervisor submission or actuator dependency. Authorization verification and
claim occur atomically; all mutable controller transitions share one re-entrant
lock. A successful shadow evaluation settles without changing physical command
state, while any post-claim rejection faults permanently. Authorization and
plan objects both reject declarative motion authority. Focused iPolar tests
pass 79/79, the adjacent unit suite passes 967 with 2 skips, and Ruff is clean.
Claude Opus 5 High and Gemini 3.1 Pro High both returned APPROVE_SHADOW with no
current P0/P1 finding. Physical motion remains denied pending restart-safe
persistence, calibration reconciliation, and commissioned physical witnesses.

## 17:04 autonomous GUI-readiness deployment

The Mele still had an older session-aware iPolar UI helper and lacked the
interactive scheduled-task wrapper. The older helper was preserved as
invoke_ipolar_ui_action.ps1.rollback-20260801-1710; the tested canonical helper
and wrapper were then deployed without starting iPolar or moving hardware.
Remote hashes now match the canonical files:

- UI helper:
  AC7910D3330BBA922C834776C6BCCA83ACA5413D55135080DAF21CFDF9FB9571
- interactive wrapper:
  BA7EE7A78BA97CC292ECDCD5C3116A8025EEAE5B5E9C91D2C5644F9C6F91C70B

The active heartbeat binds both hashes and may use the wrapper for
interactive-session Connect/Snapshot readiness. It must still fail closed if
the prior dark selection cannot be proved from a fresh screenshot.

## 17:40 true-pole configuration audit

The current plugin source defaults `RefractionAdjustment` to `True`. Automated
UPAS adjustment and drift-validation modes reject apparent-pole operation, and
run provenance records the selected pole target, estimated true/apparent pole
offset, atmosphere source, pressure, temperature, humidity, and wavelength.
A regression test now pins the generated setting default to true-pole mode.

The active Mele NINA user configuration was inspected directly and also stores
`RefractionAdjustment=True`. Tonight's differential iPolar campaign therefore
does not inherit the historical silent apparent-pole default. This establishes
configuration provenance only; it does not make the report-only iPolar result
an absolute polar-alignment measurement.
