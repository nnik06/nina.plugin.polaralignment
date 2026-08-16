# TPPA Next-Session State - 2026-08-16

## Deployed Runtime

- Mele plugin: `2.2.6.134`
- Source commit: `8e23716faf34ea58dbe96572ed1903d7249db35a`
- Plugin SHA-256:
  `244B39B2242150079D452616C18C94832A393626885227FC5A2751CE97E10F7B`
- Qualification-core SHA-256:
  `C60E7DD5D500E6BE7D216CD0CA7FDA7D7CC16B162E13564B2D82E8234950492D`
- Runtime-manifest SHA-256:
  `5F6A0D3E186F79A9B019AF84C8B1ADC56CD5CD0364CDB342CBC2A246E24D237E`
- Rollback:
  `C:\Users\nnik0\Documents\TPPA-rollbacks\20260816-061317658-2.2.6.134-8e23716f`

The application test suite passed 1,132/1,132. The fast-run analyzer suite
passed 35/35.

## Mele Readiness

NINA was closed during deployment. The final read-only readiness receipt is:

`C:\Users\nnik0\Documents\TPPA-Deploy\readiness-v134-final.json`

Receipt SHA-256:
`9C8A0638ED40EF6356C8407F92F598300FC9AE10444E7F30EF2D86C00BAF4CF1`

It passed the plugin, NINA-closed, iPolar, COM30, persistent `com2tcp`, and P20
ADB gates. The bridge was established to `10.147.17.129:4001` by `com2tcp` PID
12560. The Pi is multihomed; the readiness checker now verifies either current
DNS or the exact owning-process host/port configuration without opening a
second raw-port connection.

The ASI2600, ASI220, and EFW were physically unplugged during this readiness
check and were not required. They must be connected and checked before sky
work. The flat panel remains absent and must not be connected or operated.

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

Refraction adjustment defaults on in v134. The external supervisor defaults
off, so the attended direct route does not require covariance, cadence,
campaign, or physical-zero paperwork.

## First Field Block

1. Connect the ASI2600, ASI220, and EFW. Start NINA in the logged-in interactive
   desktop and verify the log loaded v134 with the expected plugin hash.
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
