# TPPA field report - 2026-07-23

## No-motion repeatability

Five fresh three-point determinations over 24 minutes, with no UPAS movement:

| Time | Azimuth | Altitude | Total |
| --- | ---: | ---: | ---: |
| 02:06:41 | -30'00" | -10'47" | 31'53" |
| 02:12:46 | -30'43" | -09'53" | 32'16" |
| 02:18:46 | -30'33" | -09'59" | 32'09" |
| 02:24:47 | -30'57" | -09'43" | 32'26" |
| 02:30:47 | -31'15" | -09'43" | 32'44" |

The total spread was 51 arcseconds. The azimuth result drifted by about
1'15" over the series, so short-term repeatability is better than the
longer-term stability.

## Verification-only result

After removing stale rollback assemblies from the live NINA plugin tree,
the intended forward, reciprocal, repeated-forward sequence used nine
plate solves:

- Forward: Az -29'15", Alt -10'25", total 31'03"
- Reciprocal: Az -28'41", Alt -10'46", total 30'38"
- Repeated forward: Az -29'18", Alt -10'21", total 31'04"
- Repeated-forward minus forward: Az -3", Alt +5", total +1"

This is strong evidence that the same-arc measurement is highly repeatable
over a single run. The reciprocal result differed by about 25 arcseconds
in total and remains useful as a geometry/systematics diagnostic.

## Deployment finding

Rollback DLLs had been stored below the live Three Point Polar Alignment
plugin directory. NINA could discover those stale assemblies, reproducing
the historical duplicate-plugin registration and loading old six-solve
behavior even when the nominal live DLL had the current hash.

All rollback DLLs were moved outside the live plugin tree. The deployed
assembly SHA-256 is 16796929CB538682B864FBB633ABAF2EF16E9273C1B83063A298EA76DD29FDC0.

Commit ef28f89 adds a fail-closed install validator that requires exactly
one TPPA assembly in the live tree and optionally verifies its SHA-256.

## Operational notes

- Verification-only must start with altitude margin. A 25-degree target
  drifted below the conservative balcony floor during cleanup; the
  diagnostic copy now starts at 30 degrees.
- The temporary external launcher incorrectly evaluated the balcony guard
  while the mount was slewing and used a POST-only stop request. Replace it
  with repository-owned tooling before reuse.
- No UPAS correction was commanded during this validation.
