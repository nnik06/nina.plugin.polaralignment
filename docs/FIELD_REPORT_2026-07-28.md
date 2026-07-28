# TPPA/iPolar Field Report - 2026-07-28

## Field build

The field session used TPPA 2.2.6.49 from commit
`1c759e49fa5289ad2b19045bd463ffca3fa12c3a` with DLL SHA-256
`61455BB634A9635ABC1FCFB44FD1ED6F736B7F1987655A232DF54BA3D5E3942A`.
Automatic UPAS correction and pre-seat remained disabled. No UPAS movement was
executed during the reported measurements.

## VerificationOnly settling discriminator

The first forward/reciprocal/repeated-forward block began immediately after
Home-to-A positioning:

| Determination | Azimuth | Altitude | Total |
| --- | ---: | ---: | ---: |
| Forward | +2.288' | -15.641' | 15.808' |
| Reciprocal | +1.118' | -13.146' | 13.193' |
| Repeated forward | -0.961' | -11.886' | 11.925' |

The forward signed-vector separation was 4.97 arcminutes, so repeatability
failed even though the scalar total-error magnitude changed by only 3.88
arcminutes.

After five undisturbed minutes at the same A pointing, the identical block
reported:

| Determination | Azimuth | Altitude | Total |
| --- | ---: | ---: | ---: |
| Forward | +0.301' | -12.753' | 12.756' |
| Reciprocal | +1.444' | -13.259' | 13.338' |
| Repeated forward | -0.099' | -12.265' | 12.265' |

Same-arc repeatability passed with approximately 0.63 arcminute vector
separation. Centered reciprocity narrowly failed the 1 arcminute threshold:
signed deltas were approximately +1.34 arcminutes azimuth and -0.75
arcminutes altitude.

The operational conclusion is that Home or a major prepositioning slew needs a
qualified settling dwell. The stable same-arc estimate was about 12-13
arcminutes total, but this is internal repeatability evidence, not proof of the
absolute true-pole error.

## Report-only drift validation

A guarded A/B/C/A DriftValidationOnly run passed the balcony movement and
closure gates. Mechanical A closure was 0.0011 degrees and solved A closure was
0.0150 degrees.

| Track | Declination drift | Qualification |
| --- | ---: | --- |
| A | -3.80139 +/- 0.05793 arcsec/min | valid |
| B | -2.84603 +/- 0.18589 arcsec/min | invalid: half-window disagreement |
| C | -2.27636 +/- 0.20497 arcsec/min | invalid: uncertainty above limit |
| repeated A | -3.63363 +/- 0.08554 arcsec/min | valid |

The estimator correctly rejected the session and returned no polar-error
vector. The A return was reasonably repeatable, while B and C did not support a
qualified absolute solution. This remains compatible with field-dependent
geometry, load, refraction, or track nonstationarity; the run does not separate
those causes.

## Hardware findings

The UPAS bridge Pi remained unavailable. Direct USB connected the UPAS
controller to Mele as `USB Serial Device (COM5)` after reboot. COM identity is
not encoder feedback and provides no physical-position claim.

After the field shutdown, Windows enumerated the two ZWO cameras and the Huawei
P20 USB composite device, but no iPolar camera-class or image-class device and
no connected problem device. iPolar was therefore unavailable as an
independent witness. Repair its physical USB enumeration before the next
witness campaign.

## Shutdown

PHD2 guide output was restored. The mount was verified parked, the flat panel
closed with light off, and every approved NINA device including Switch was
disconnected.

## Prepared next build

TPPA 2.2.6.50 was committed as
`ae33191c08946433499076dea1a5fcbe4cdf0438`, passed 371/371 tests, and was
deployed after shutdown while NINA was closed. The deployed DLL SHA-256 is
`6B8E23932E8454BB59BA54044F93F15E0DF2DCBDEFF36FD91B8DF888406CA5F4`.
The prior 2.2.6.49 DLL was preserved outside the live plugin tree as a
hash-verified rollback.

The next session must begin with the read-only readiness checker, repair iPolar
enumeration, enforce the five-minute post-positioning dwell, and repeat
interleaved no-motion A/B/A measurements before considering any actuator test.
