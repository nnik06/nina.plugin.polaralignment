# Field Report - 2026-08-01

## Outcomes

- Qualified iPolar in-motion star arcs measured 12.4 and 12.7 arcsec RMS,
  with maxima below 30 arcsec.
- The east-pier DEC load cycle closed by about 8.9 arcsec, below the iPolar
  differential-witness floor; this is a null result, not a flexure magnitude.
- Endpoint-safe mount commands were shown to have unsafe intermediate routes.
  Direct pier changes and large unqualified same-pier slews remain forbidden.
- TPPA 2.2.6.60 field-verified validated TelescopeInfo coordinate capture and
  restored the A/correction pointing after guarded cancellation.
- The safe Az 330/Alt 35 five-position diagnostic passed all reached movement
  gates but exceeded 600 seconds after three forward samples and one reciprocal
  sample. No fit was produced and no UPAS movement occurred.

Detailed evidence is in:

- `docs/IPOLAR_SLEW_STABILITY_FIELD_REPORT_2026-08-01.md`
- `docs/TPPA_5POSITION_FIELD_REPORT_2026-08-01.md`

## Council consensus

Claude Opus 5 High and Gemini 3.1 Pro High agreed that iPolar is a differential
stability witness, not an independent absolute pole authority. They recommended
pre-warming the capture/solver path, preserving partial point receipts, and
budgeting five-position metrology separately from the under-five-minute
operational three-point path. Settling and trajectory guards must not be
weakened.

## Prepared code

TPPA 2.2.6.61 adds report-only capture, solve, and total attempt timing. The
change passed 444/444 tests and was not deployed during this session.

- DLL SHA256: `DC8AF8CEDC25634F2060E5602CA73761A559AA816FD928EB8901726D07349EA4`
- Product version:
  `2.2.6.61+8e4bf3c8e77a0d86949fce03657f75cb520c11a8`

## Shutdown

PHD2 guide output was enabled. The mount was parked with tracking off. The
flat panel was closed with light off. Camera, filter wheel, focuser, rotator,
guider, flat device, mount, and Switch were all verified disconnected.

The reusable Mele shutdown safeguard was corrected after shutdown:

- future logs use a timestamped filename instead of appending to a July file;
- guarded TPPA verification and drift-validation launchers are included in
  diagnostic-process cleanup;
- the prior script is preserved as
  `shutdown_rig_safeguard.ps1.bak-20260801`;
- deployed script SHA256:
  `6DBCC0D683897CD2398646A1CF1A795299E72C6624FFC4011CAB4866ADC570A2`.

## Night campaign preparation

The later daylight preparation block deployed TPPA 2.2.6.67 while NINA, PHD2,
and iPolar were closed. The previous DLL and witness tools were preserved with
rollback stamp `20260801-115845`; every staged and installed file was verified
before use.

- plugin DLL SHA256:
  `CBA3E6C646A1EFB44F560D56603025466592E47F4D6DEDD021FDFC4619CBD69D`;
- blind point-capture script SHA256:
  `FDD935186F63C6285BAF7619C9DD15C7699DE71119D4C9D0E325F17CA46BB441`;
- one-shot observer script SHA256:
  `697B0B5891FDCDC0F25DDF34FA45B2D0F00CECDAF6BB16B3AAB0FD3189C786EE`;
- qualification CLI executable SHA256:
  `A7850396930A8A5A8C769851D3E5B32C371979419BEFB5F107BCD07F7C6C4D38`.

The release passed 518/518 .NET tests, 14/14 witness/CLI PowerShell contract
tests, and 29/29 no-hardware iPolar campaign tests. A remote CLI smoke test
returned the expected authority-free usage contract, and the deployed observer
parsed without errors. These are software-readiness results, not field proof of
absolute TPPA accuracy or iPolar stability.

The guarded nighttime iPolar pier-side campaign is armed for darkness. It must
run in the interactive Windows session, requires a fresh solved iPolar star
frame with the previous dark selected, and remains report-only. No UPAS motion
or absolute polar-alignment decision may be derived from this campaign alone.

TPPA 2.2.6.68 was subsequently prepared and tested locally but deliberately not
deployed before the campaign. It adds atomic witness-request publication and a
persistent, one-request/one-attempt observer service. The local checkpoint
passes 519/519 .NET tests and 18/18 witness service/CLI PowerShell tests. The
Mele remains on the hash-verified 2.2.6.67 deployment above for tonight's field
evidence.

The local 2.2.6.68 work then added the report-only A/B/C/A producer runner and
its exact-equatorial slew child. Focused campaign behavior gates pass 3/3; the
complete point-capture, CLI, one-shot observer, persistent service, and campaign
contract set passes 21/21, and all five operational scripts parse cleanly.
The runner preflights every planned leg at one-degree-or-finer spacing, predicts
constant pier side outside a five-degree meridian exclusion, repeats the full
preflight at realized UTC before every command, watchdogs actual telemetry
during normal and cleanup slews, and binds each stop to one atomic observer
request/outcome. A Dubai balcony geometry sweep found no valid 50-degree arc at
the 40-degree operational altitude floor; the 45-degree contractual minimum has
a narrow valid path around Dec +60 degrees and hour angle +5 to +6 degrees.
This work remains local and undeployed during the pinned 2.2.6.67 iPolar
campaign.

The post-council safety audit resolved the Advanced API RA-unit question against
the official `christian-photo/ninaAPI` source. `WebService/V2/Equipment/Mount.cs`
constructs slew coordinates with `Angle.ByDegree(ra)`, so the runner's RA-degree
contract and its independent post-slew conversion from NINA RA hours are
consistent. The producer's `SignedAngularDelta` is also explicitly wrap-aware;
neither allegation required a code change.

The audit did uncover real safety gaps in the undeployed runner. It now models
the complete immutable 60-second observer window plus dispatch margin, watches
actual mount telemetry during capture/solve as well as slews, issues an explicit
stop and verifies `Slewing=false`, cross-checks the initial RA/Dec/LST projection
against NINA's reported horizontal position within one degree, never force-kills
an active observer, and independently verifies/reconciles final PHD2 app and
guide-output state after the observer exits. The focused end-to-end contract set
passes 24/24 and the complete .NET suite passes 519/519.

With the corrected 65-second dwell model, an exhaustive integer-degree Dubai
balcony sweep found only three safe 45-degree candidates. Their best altitude
headroom was 0.172 degrees (Dec +60, initial HA +5 degrees), with the others at
0.166 and 0.076 degrees. This is not operationally adequate for setup error,
sidereal timing variation, or mount behavior near the meridian. The scientific
45-degree threshold was not weakened to make the campaign fit: the A/B/C/A
witness remains undeployed and unqualified for this balcony. The pinned
2.2.6.67 iPolar campaign remains independent of this local work.

## Post-relocation verification and launcher hardening - 2026-08-02

The rig was relocated and powered before this continuation. Preflight confirmed
NINA PID 2936, PHD2 PID 14208, iPolar PID 4268, the hash-qualified TPPA
2.2.6.70 DLL, mount connected and tracking off, main camera connected and idle,
flat cover open with light off, Switch connected read-only at 12.37 V, and
Weather and Safety disconnected. UPAS remained locked out and no UPAS command
was issued.

A fresh Arc A VerificationOnly run completed all nine solves under run ID
`25c2ae24-0ea5-48ce-af02-850e6c765b29`. Its evidence artifact is
`25c2ae24-0ea5-48ce-af02-850e6c765b29-tppa-evidence.json`, SHA256
`B43519792EF3AADD99FFEAA23F670037ED63D1CC98C9C06AD4A06260E18B85C5`.
The three total-error estimates were 101.026, 101.920, and 102.483 arcmin.
Reciprocity passed at 0.337 arcmin vector separation, but same-direction
repeatability failed at 2.418 arcmin and the reported condition number 171.631
failed. Pooled residual RMS was 3.098 arcsec and maximum residual 7.521 arcsec.
The only defensible alignment claim is gross misalignment of order 1.7 degrees;
no per-axis correction vector, sub-arcminute result, motion authority, or
completion authority is supported.

Arc C at Az 305 / Alt 30 was attempted twice with the guarded external launcher.
The original 600-second run and a hash-qualified 900-second retry both ended as
incomplete and are quarantined. The retry run ID was
`e050a424-f446-490d-bef3-a2e515790b2f`; it produced a structurally explicit 6/9
partial dataset with no authority. Solve totals included 56.4, 66.9, 89.3, and
163.4 seconds. The launcher expired while the repeated-forward A solve was in
flight. Endpoint apparent altitudes near 29.2 and 27.2 degrees also failed the
30-degree refraction-qualification floor report-only. Arc C was therefore
infeasible at planning time under either wall-clock cap and is not an arc-bias
result.

The first launcher revision replaced a two-second tracking check with bounded
retries and a continuous ten-second idle/tracking-off hold. The 900-second field
expiry demonstrated the original race: the first stop attempt was defeated by
late plugin cleanup, while attempt two succeeded; an independent check five
seconds after launcher exit still showed tracking off and slewing false. The
launcher was then hardened again so sequence/plugin cancellation terminality
must remain stable before the final tracking stop. It also emits
`TPPA_RUNTIME_ADMISSION` and rejects infeasible plans before NINA sequence start.
For the current nine-point defaults, the minimum is 1260 seconds: nine points at
120 seconds each plus a 180-second cleanup reserve. A Mele replay with a
900-second cap emitted `Admitted=false`, left the sequence `CREATED`, and left
the mount idle with tracking off.

The final launcher SHA256 is
`190A16AFE1FAF45D3AFCE55F056E4F0EE6D8610A0955BA0E858169A99D391A95`.
Rollback copies were preserved at
`C:\Tools\run_guarded_tppa_verification.ps1.rollback-20260802-0316` and
`C:\Tools\run_guarded_tppa_verification.ps1.rollback-20260802-0350`; activation
also preserved the immediately preceding launcher at
`C:\Tools\run_guarded_tppa_verification.ps1.rollback-20260802-040802`.
Focused launcher tests pass 25/25 and the complete PowerShell safety suite passes
117/117 under PowerShell 7 with Pester 4.10.1. A post-deployment Arc C
preflight-only replay rejected the 900-second cap with `required=1260` before
sequence start; the sequence remained `CREATED` and an independent API query
confirmed `TrackingEnabled=false` and `Slewing=false`.

Claude Opus 5 High and Gemini 3.1 Pro High independently converged on the next
steps: do not substitute a timed hold for observed terminality; do not retry a
nine-point run immediately after a roughly 160-second solve; reject runtime and
geometry failures preflight; move qualification arcs above the refraction floor;
and prioritize a well-conditioned A-B-A revisit that separates time-correlated
walk from sky-position bias. Claude additionally recommended offline atmosphere
perturbation replay and full covariance/confidence-ellipse reporting. Gemini
recommended wider, higher arcs and explicit cancellation terminality. Both
agreed that Arc A supports only an order-of-magnitude PA claim and no correction
authority.