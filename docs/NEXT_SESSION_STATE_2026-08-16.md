# TPPA Next-Session State - 2026-08-16

## Deployed Runtime

- Mele plugin: `2.2.6.136`
- Source commit: `5f34c6195ea57b2fa2d3808b13c2ec871fdc583d`
- Plugin SHA-256:
  `39870079B43275728DC6AAC319E06C6B55FA4A43BBB328F0B483DB6AB5C30CBF`
- Qualification-core SHA-256:
  `C893D16247867BDA8BAF1B1D7BBEFF5A75499B6441A2DAF75A2942235970D778`
- Runtime-manifest SHA-256:
  `A6003806A4A0CCD75093664C9022AB97F002DDE5CF23D7AFECC4824DAECE5A4F`
- Rollback:
  `C:\Users\nnik0\Documents\TPPA-rollbacks\20260816-073912499-2.2.6.136-5f34c619`

The application test suite passed 1,144/1,144. The final TRX is
`C:\Users\nnik0\AppData\Local\Temp\tppa-v136-gain-tests\v136-final.trx`
with SHA-256
`BBD0E1926148F5006687A89F45886DED1BC9041822C3BF4167CF4A8CC2D03FEE`.
The fast-run analyzer suite passed 36/36, including the active four-of-five
operational contract.

The matching field-run acceptance analyzer is deployed on Mele at:

`C:\Users\nnik0\Documents\TPPA-Deploy\analyze_tppa_fast_alignment_runs.ps1`

SHA-256:
`6F2F3B445A8373A26593F25E5F1C20FDA7EBC92344CCE4907BF3D71CE933A1B0`

It scores emitted `TPPA_FAST_RUN_EVENT` records for terminality, elapsed time,
move count, post-move evidence, final vector, and campaign reliability. It has
no motion or runtime authority.

For the active operational goal, use the deployed fixed-policy wrapper:

`C:\Users\nnik0\Documents\TPPA-Deploy\analyze_tppa_operational_5run.ps1`

SHA-256:
`A2D1BA72DFCBBD18661F4573A9F58CF0B91C2805CB223631438955BA28E5C466`

It requires five eligible attempts, four passes, an 80-percent pass rate, one
observing night, at most 300 seconds, at most 3 arcminutes, and no more than 12
moves. Larger preregistered campaigns continue to call the general analyzer
with their explicit policy; its historical 20-run defaults are unchanged.

Version 135 fixes a dimensional bug in direct-route preflight: the determinant
of the normal matrix was compared with first-order damping instead of squared
damping. That rejected the real low-gain UPAS response matrix before the
runtime solver could use it. Exact-matrix tests now cover all four sub-degree
corners and all four numerical +/-300 arcminute corners.

Version 136 raises only the qualified measured-matrix direct-route correction
gain from 0.65 to a still-damped 0.75. Probe, remembered-response, and
clamp-recovery gains are unchanged. The exact deployed Dubai matrix now reaches
the 3-arcminute target from the +/-60 arcminute corner in three bounded moves
instead of four, while retaining fresh feedback after every move and the
independent stationary completion determination.

## Mele Readiness

NINA was closed during deployment. The passing core readiness receipt is:

`C:\Users\nnik0\Documents\TPPA-Deploy\readiness-v136-core.json`

Receipt SHA-256:
`72977FB0F1BB2E74454CED513A9FA70CBC65C50FF5030A503F0D133FDA6B0221`

It passed the plugin, NINA-closed, iPolar, COM30, persistent `com2tcp`, and P20
ADB gates. The bridge was established to `10.147.17.129:4001` by `com2tcp` PID
12560. The Pi is multihomed; the readiness checker now verifies either current
DNS or the exact owning-process host/port configuration without opening a
second raw-port connection.

The full readiness probe was also preserved at
`C:\Users\nnik0\Documents\TPPA-Deploy\readiness-v135-final.json` (SHA-256
`B426B3CBEAF042988FE7088BA4EAD26D10C94F2B11D2AA5D76BBE54F4B036283`).
It failed only the ASI2600, ASI220, and EFW gates because those devices were
physically unplugged. Connect and recheck them before sky work. The flat panel
remains absent and must not be connected or operated.

Application-level validation also passed. NINA was launched once through the
known interactive task and its fresh log
`C:\Users\nnik0\AppData\Local\NINA\Logs\20260816-114015-3.2.0.9001.5084-202608.log`
reported `Successfully loaded plugin Three Point Polar Alignment version
2.2.6.136`. The corrected five-second operational sequence loaded through the
Advanced API with HTTP 200 and remained in `CREATED` state; it was not started
and no telescope or UPAS motion was requested.

That validation instance was then closed gracefully through UI Automation,
bound to exact NINA PID 5084. The one-shot task returned 0, independent process
enumeration found no remaining NINA process, and the preserved result
`C:\Users\nnik0\Documents\TPPA-Deploy\close-nina-v136-load-test.json` records
`closeRequested=true`, `method=uia-window-pattern-close`, and
`processStillRunning=false`. Its SHA-256 is
`3A455FFCC05506878B22EFD191F9C782064454D2E062F7BE44C2C7EB7A5D5EEE`.
No forced termination was used.

## Persisted Operational Seed

The current Mele NINA user configuration contains:

- automated Avalon adjustment: enabled;
- direct full-travel route: enabled and confirmed;
- AZ and ALT travel guards: enabled and confirmed;
- physical bounds: +/-5.4 degrees;
- automatic input authority: at most 300 arcminutes per axis and 424.264
  arcminutes total;
- AZ pre-seat: enabled, +24 command units, direction +1;
- calibrated response matrix in degrees per command unit:

  ```text
  [ AZ/X   AZ/Y ]   [ 0.0131762653  0            ]
  [ ALT/X  ALT/Y] = [ 0             0.0155082342 ]
  ```

- maximum command per move: X=80, Y=80.

The `300'` per-axis input ceiling is an admission ceiling, not proof that a
centered actuator can correct `300'` of sky error. With the persisted diagonal
response matrix and the physical scale calibrations X=`0.025` degrees/unit and
Y=`0.022` degrees/unit, centered one-sided travel corresponds to approximately
`2.85` degrees of AZ sky correction and `3.81` degrees of ALT sky correction
before reserve and cross-coupling. The ordinary sub-degree imaging route is
comfortably inside this authority. The full +/-5-degree PA-error claim is not
physically demonstrated by the present calibration and must not be inferred
from the software input cap; qualify that tier only from fresh commanded,
marker, and TPPA response evidence.

The persisted marker positions, AZ `+0.7` degrees and ALT `+0.528` degrees,
predate the next transport and are not current physical evidence. Replace them
with fresh signed scale readings after the rig is stationary on the balcony.
Never substitute GRBL MPos after a reset.

Clamp-limited recovery is currently disabled and response uncertainty therefore
remains unqualified (the default is 100%). This is appropriate until fresh
field responses establish a relative uncertainty no greater than 10%. A normal
sub-degree run does not need clamp-limited recovery. A full-envelope corner run
does.

Refraction adjustment defaults on in v136. The external supervisor defaults
off, so the attended direct route does not require covariance, cadence,
campaign, or physical-zero paperwork.

## Response Evidence

The field response analyzer is now schema v2 and is deployed on Mele at:

`C:\Users\nnik0\Documents\TPPA-Deploy\summarize_tppa_upas_response.ps1`

SHA-256:
`D4CC272062362D7C5A12BC57302D9B9FB07A38EEEDADB6A2D7895E7DCAC70C46`

It reports the isolated 2x2 response matrix, condition number, rejected-probe
evidence, direction consistency, sample spread, and a fail-closed verdict for
clamp-limited recovery. It does not grant motion authority.

The latest accepted full probes contained one isolated sample per axis:

```text
          X command       Y command
AZ     0.01317625 deg   0.00039600 deg
ALT    0.00017783 deg   0.01550825 deg
```

Their condition number is 1.183 and the dominant diagonal terms reproduce the
persisted seed. This supports using the seed for the normal bounded sub-degree
route, but one sample per axis cannot establish response uncertainty.

The exact measured matrix is now admitted by direct-route preflight for the
normal sub-degree start. Numerical solver tests also converge from +/-300
arcminutes, but the deployed physical scale (X=0.025 and Y=0.022 degree per
command unit) denies at least one full-corner case on physical headroom. Do not
claim the complete +/-5 degree tripod-free envelope from numerical convergence
alone. Field calibration must reconcile commanded displacement, physical scale
travel, and sky response before that tier is qualified.

At the extreme `(+60', -60')` sub-degree corner, the deployed non-recovery
`0.75` damping model requires three feedback moves and predicts 285 seconds at
the current 80-second initial pair, 40-second fresh determinations, 15-second
move overhead, and 40-second final confirmation. This now fits the five-minute
model while preserving all fresh-response and completion checks. Field timing
and repeatable response remain required before claiming the performance target.

The older mixed small-probe set has only two X and three Y samples and a
condition number of 27.006. Treat it as deadband/noise evidence, not as a
calibration matrix.

Clamp-limited full-envelope recovery therefore remains unqualified. During the
next stable mechanical epoch, collect at least two more accepted isolated
responses per axis, after the normal pre-seat has loaded the mechanism. Three
accepted samples per axis must retain direction consistency, matrix condition
number <=5, and maximum relative vector deviation <=10% before enabling that
route.

## First Field Block

Use the prepared push-button sequence:

`C:\Users\nnik0\OneDrive\Documents\N.I.N.A\TPPA_FAST_OPERATIONAL_V136_300_20_70_5S.json`

SHA-256:
`4D606D290B2562FB067031228FA7FEF3ACDA98482811509BFC996E7569146F94`

It contains one motion-capable TPPA instruction at Az 300 deg / Alt 36 deg,
22-degree legs, a 5-second exposure, 5-second verification-point settling,
3-arcminute tolerance, the Az 270..010 / Alt 20..70 telescope envelope, and
the five-minute runtime contract. Do not select the older
`TPPA_FAST_OPERATIONAL_300_20_70.json`; it still requests a 2-second exposure,
which field plate solving has shown to be insufficient.

1. Connect the ASI2600, ASI220, and EFW. Start NINA in the logged-in interactive
   desktop and verify the log loaded v136 with the expected plugin hash.
2. Acquire fresh signed AZ and ALT scale readings after transport and enter
   them as the current travel-guard positions.
3. Confirm refraction adjustment on, automatic adjustments on, external
   supervisor off, AutoPause off, five-second exposures, reliable plate solves,
   and at least five seconds of verification-point settling.
4. From the expected sub-degree starting error, run the imaging-ready route to
   <=3 arcminutes. Require fresh feedback after every move and two independent
   fresh determinations for completion.
5. Preserve every `TPPA_UPAS_FRESH_RESPONSE_SAMPLE`. Compare actual and
   predicted X/Y responses, estimate response spread, deadband, and cross-axis
   terms, and update the stored matrix only from accepted samples.
6. If relative response uncertainty is <=10%, enable clamp-limited recovery and
   schedule the broader tripod-free envelope trials. Otherwise retain the
   bounded non-clamped route and collect more response samples.
7. After a confirmed <=3 arcminute result, perform the outcome check with one
   real guided 900-second sub (or a qualified raw Dec-drift check if imaging is
   unavailable).

The 300-second target is measured and reported. It is not a reason to abandon a
safe, freshly observed, converging run. The run still stops on travel denial,
implausible or regressing response, stale measurement, cancellation, or the
finite 12-move ceiling.
