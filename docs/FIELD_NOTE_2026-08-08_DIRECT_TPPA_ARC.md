# Direct TPPA Arc Safety and Stability Note (2026-08-08)

## Scope

This note records the live direct-TPPA session at commit
`a73466f0aacae6908b91b4a80ee48eace29b9598`, followed by the tested safety
repair in `67c92eabe23e3857b54de2cd1b6c7f6c985571ba`. It is an operational
evidence note, not an absolute polar-alignment accuracy claim.

## Motion evidence

At the usable Azimuth approximately 310 degree arc and initial altitude
approximately 39 degrees, independent fresh pre-move determinations agreed:

| Determination | Azimuth error (arcmin) | Altitude error (arcmin) | Total (arcmin) |
|---|---:|---:|---:|
| A | +61.714 | -0.217 | 61.715 |
| B | +61.360 | +0.125 | 61.361 |

Their signed-vector separation was 0.492 arcmin. The bounded coarse route
therefore admitted X-only UPAS correction. The same-direction X-only path
reduced fresh totals approximately `61.5 -> 59.1 -> 53.6 -> 25.497 -> 8.321`
arcmin. Altitude was not commanded.

After the final X move, the next fresh pair was 8.321 and 9.334 arcmin (vector
separation 1.140 arcmin), so the strict one-arcmin completion gate correctly
declined further motion. The following no-motion fresh pair was 4.617 and
7.620 arcmin, a 6.193 arcmin signed-vector separation. UPAS actuation was
stopped at that point.

The valid conclusion is that coarse X correction was observed, but this field
did not provide repeatable low-error authority. No sub-arcminute or single
final polar-error claim is supported.

## Safe-arc observations

* A start near altitude 29 degrees produced insufficient-star solve failures.
* A start near altitude 49 degrees sent the third TPPA sample to altitude
  70.23 degrees, above the active 69 degree ceiling. The mount was returned;
  no UPAS command followed.
* The existing mount-motion envelope guarded VerificationOnly waypoints, but
  direct automated RA-axis motion did not preflight the full B/C arc.

Commit `67c92ea` adds an opt-in preflight for direct automation. When the
existing envelope is enabled, it predicts B and C from the current pointing
and rejects the complete arc before the first RA-axis move if either sample is
outside the configured envelope. Unit tests cover the observed 70.23 degree C
sample, an admissible arc, and the disabled-envelope compatibility path.

## Stationary camera/solver series

Five guarded, non-moving 3-second plate-solve captures ran at the safe pose;
the artifact is:

`C:\Users\nnik0\Documents\TPPA-PHD2-tests\static-stability-20260808-022702\samples.json`

All five solves succeeded and each capture passed before/after no-slew checks.
After a linear time detrend, plate-centre residual RMS was 0.59 arcsec
(RA 0.21 arcsec, Dec 0.57 arcsec). The raw field centre drifted gradually, but
ordinary per-frame plate-solving noise cannot explain the 6.193 arcmin TPPA
fresh-determination disagreement.

## Council result

Claude Opus 5 High and Gemini 3.1 Pro High both advised stopping UPAS
actuation. They differed only in their optional passive diagnostic:

* Claude recommended five fresh stationary TPPA determinations.
* Gemini recommended observing the continuous estimator while stationary.

The direct plate-solve series was run first because the TPPA sequence leaf was
already terminal `FAILED`; it established that solve noise was not the source
of the discrepancy. A fresh TPPA series requires a saved-sequence reload (or,
as a less desirable fallback, a graceful NINA restart) because NINA retains a
failed leaf as terminal.

## Next field start

1. Reload a saved TPPA sequence and verify its leaf is nonterminal before
   pressing Start.
2. Enable and record the configured mount-motion envelope before direct
   automation.
3. Use the `67c92ea` build only after normal hash-verified deployment.
4. Perform a no-motion fresh TPPA repeatability series on an envelope-qualified
   arc before granting low-error actuation authority.
5. Retain the strict one-arcmin post-move completion threshold; do not relax it
   to chase a noisy result.
