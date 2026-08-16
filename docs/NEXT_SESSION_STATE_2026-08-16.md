# TPPA Next-Session State - 2026-08-16

## Deployed Runtime

- Mele plugin: `2.2.6.135`
- Source commit: `55aa6eebf2463753c95f98a0541d1f7ddbcf8e89`
- Plugin SHA-256:
  `0EB8B500F779C2A3EC43DCFDB84C3EF74B6D23E53F31ABC280B5D96BA647A812`
- Qualification-core SHA-256:
  `D608AEDFCA2812A0C3D0C0DA40A4AB8D46D7D1573B255463019412A3389CBAB7`
- Runtime-manifest SHA-256:
  `7B70E33BF7DCD1D25E269FECF7BF74B3D5AF6B28A8D0F7053DF823E4C28BB744`
- Rollback:
  `C:\Users\nnik0\Documents\TPPA-rollbacks\20260816-070526037-2.2.6.135-55aa6eeb`

The application test suite passed 1,143/1,143. The final TRX is
`C:\Users\nnik0\AppData\Local\Temp\tppa-v135-postdeploy-tests\v135-postdeploy-full.trx`
with SHA-256
`46D832BD6B3473BAE1DDC7C4FD686C910805C07FAF607148BFE4D836D5D6E80E`.
The fast-run analyzer suite
passed 35/35.

Version 135 fixes a dimensional bug in direct-route preflight: the determinant
of the normal matrix was compared with first-order damping instead of squared
damping. That rejected the real low-gain UPAS response matrix before the
runtime solver could use it. Exact-matrix tests now cover all four sub-degree
corners and all four numerical +/-300 arcminute corners.

## Mele Readiness

NINA was closed during deployment. The passing core readiness receipt is:

`C:\Users\nnik0\Documents\TPPA-Deploy\readiness-v135-core.json`

Receipt SHA-256:
`6E9676117530DA0767D8DFF46F8B29048C59E1F1D6DC31F8D48B0DD5B7104813`

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

The persisted marker positions, AZ `+0.7` degrees and ALT `+0.528` degrees,
predate the next transport and are not current physical evidence. Replace them
with fresh signed scale readings after the rig is stationary on the balcony.
Never substitute GRBL MPos after a reset.

Clamp-limited recovery is currently disabled and response uncertainty therefore
remains unqualified (the default is 100%). This is appropriate until fresh
field responses establish a relative uncertainty no greater than 10%. A normal
sub-degree run does not need clamp-limited recovery. A full-envelope corner run
does.

Refraction adjustment defaults on in v135. The external supervisor defaults
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
`0.65` damping model requires four feedback moves and predicts 340 seconds at
the current 80-second initial pair, 40-second fresh determinations, 15-second
move overhead, and 40-second final confirmation. This is convergent but misses
the five-minute performance target. Measure actual cadence and repeatable
response first; do not raise gain from the present one-sample-per-axis dataset.

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

1. Connect the ASI2600, ASI220, and EFW. Start NINA in the logged-in interactive
   desktop and verify the log loaded v135 with the expected plugin hash.
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
