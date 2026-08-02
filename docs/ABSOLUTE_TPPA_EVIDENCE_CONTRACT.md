# Absolute TPPA Evidence Contract

## Purpose and status

This contract is the only software path in this repository that may turn a
fast TPPA run plus an independent true-pole witness into an auditable absolute
qualification receipt.

It does not itself prove the project goals. As of 2026-08-01:

- TPPA has not yet supplied a same-session field evidence file that passes this
  contract and establishes less than one arcminute absolute total error in less
  than five minutes.
- iPolar has not yet supplied a calibrated independent-witness evidence file
  with a defensible 95% uncertainty bound at or below 30 arcseconds.
- No binder or receipt grants telescope or UPAS motion authority.

## Architecture

The policy and binder are BCL/Newtonsoft-only sources under
`QualificationCore`. The same physical source files are compiled into the
NINA plugin and the headless `TppaQualificationCli`; policy logic is not
reimplemented in PowerShell.

The producer boundary is deliberate:

1. The TPPA runtime writes one immutable TPPA evidence file.
2. A separate instrument and pipeline write one immutable witness evidence
   file after TPPA completes, without a physical adjustment.
3. The headless CLI validates and binds the two byte streams.
4. Only structurally valid evidence creates a receipt. A valid pair may still
   produce a not-qualified receipt when a numerical policy limit is exceeded.

## Strict JSON rules

Both files use schema version 3, UTF-8 JSON, camel-case property names, no
duplicate names, and no unknown fields. Each contains `evidenceDigest`, the
lowercase SHA-256 of canonical JSON after removing that property and sorting
object properties recursively. The binder also records the SHA-256 of each
original byte stream in the receipt.

Schema versions 1 and 2 are intentionally ineligible for an absolute claim.
Version 1 persisted producer conclusions without enough raw data for the
headless binder to reproduce the critical fit and environmental gates.
Version 2 added those raw vectors but did not bind the complete mount-command,
PHD2, raw-image, solver, FITS-time, and trajectory-preflight chain required to
prove that the independent witness was stationary, unguided, and physically
qualified.

TPPA evidence records:

- run, session, producer, producer kind, pipeline, hardware epoch, mechanical
  state, clock domain and uncertainty, instrument, solver, raw site latitude,
  longitude and elevation, coordinate frames, pole target, raw atmospheric
  observation time/pressure/temperature/humidity, refraction state, and the
  producer's atmosphere/site/frame qualification assertions;
- at least three non-overlapping UTC determinations with unique IDs;
- one unchanged correction sequence and mechanical-state digest;
- exactly three raw solves per determination: strict UTC, content digest,
  solved RA/Dec, pier side, and topocentric NWU unit vector;
- fresh/uncached, geometry, minimum-span, and closure assertions for every
  determination, each independently recomputed by the binder;
- at least three unique source-vector digests per determination, matching the
  raw solve digests; and
- a declared unit mount-axis vector per determination plus the declared
  true-pole vector, both checked against binder recomputation.

The binder recomputes solve uniqueness and ordering, the three-point plane
normal, hemisphere orientation, geometry degeneracy, the 15-degree minimum
pairwise span, returned-A closure, the site-derived true-pole vector, atmosphere
freshness and physical ranges, the one-second clock bound, and coordinate-frame
qualification. Producer flags are cross-checks only; disagreement invalidates
the evidence rather than choosing either side.

Witness evidence records:

- the bound TPPA run and session, a distinct producer, independent instrument,
  and pipeline;
- the exact same hardware, mechanical state, raw site coordinates/elevation,
  coordinate frames, true-pole convention, and correction sequence;
- a declared `absolute-true-pole` evidence basis; differential-stability-only
  iPolar or slew evidence is structurally ineligible;
- an explicit UTC clock domain with uncertainty no greater than one second;
- a witness arc whose earliest raw solve starts no earlier than TPPA completion
  and whose final observation is no later than 300 seconds afterward;
- a qualified full-trajectory preflight digest, with all A/B/C/A slew legs
  sampled at no more than one degree, a minimum operational altitude of 40
  degrees, at least 45 degrees total monotonic RA arc, known constant pier side,
  and a bounded design-conditioning proxy;
- initial and final PHD2 application states plus explicit proof that guide
  output was disabled for every exposure and restored afterward;
- a qualified `ra-rotation-circle` measurement containing exactly four raw,
  strictly ordered, same-pier solves A/B/C/A with unique digests, topocentric
  NWU unit vectors, at least 15 degrees pairwise span in the fitted triad, and
  returned-A closure within 0.25 degree in both RA/Dec and the persisted
  topocentric-vector representation;
- exactly four immutable acquisition receipts binding unique mount-command
  identities and issue/completion times, fixed commanded declination,
  monotonic commanded RA, tracking and slewing state, PHD2 state, exposure
  start/midpoint, raw FITS DATE-OBS value, explicit exposure-start or
  exposure-midpoint convention and uncertainty, raw guider-image
  SHA-256, solver-output SHA-256, solver identity and binary SHA-256, solved
  RA/Dec/position angle, horizontal telemetry, coordinate frame, and pier side;
- unique source-vector digests matching those raw solves and disjoint from all
  TPPA inputs;
- a current calibration payload with a producer and source digest disjoint
  from both TPPA and the live witness observation, with the uncertainty payload
  cryptographically bound to those same declared calibration digests; and
- a declared unit mount-axis vector that matches the binder's fit to the raw
  witness arc, plus all positive finite uncertainty terms, at least three
  calibration samples, and at least two closure samples.

Producer IDs and digests are provenance assertions and tamper evidence. They
are not signatures. Field custody, clock discipline, and instrument
commissioning remain required.

`TppaRaRotationWitnessEvidenceProducer` creates the report-only witness artifact
from four immutable point receipts emitted by the disjoint ASI220/PHD2 plus
external-ASTAP acquisition path. It validates full-trajectory qualification,
mount-command ordering, stationary unguided capture state, exposure/FITS
timing, raw-image and solver provenance, monotonic fixed-declination geometry,
dual-representation closure, calibration provenance, and measured uncertainty
before writing with create-new semantics. It grants no motion, completion, or
absolute-accuracy authority; only the independent binder can combine it with
matching TPPA run evidence.

`tools/capture_phd2_astap_witness_point.ps1` is the fail-closed point-acquisition
boundary used at each externally guarded A/B/C/A stop. It contains no mount
slew endpoint. It requires NINA to report a connected, tracking, unparked,
stationary mount on the expected pier side inside the qualified balcony
envelope; requires PHD2 to be stopped and connected; disables and verifies
guide output; captures one ASI220 FITS with `capture_single_frame` and
`save_image`; preserves and hashes that image; solves it with the external
ASTAP CLI while hashing both the solver binary and WCS output; interprets PHD2
`DATE-OBS` explicitly as exposure start; and transforms the J2000 solve at the
exposure midpoint to an unrefracted topocentric NWU vector using the installed
ASCOM/NOVAS transform. It restores and verifies PHD2 guide-output state in a
`finally` block. Any missing telemetry, time, FITS card, solve, hash, coordinate
transform, or restoration proof fails closed and produces no point receipt.

`tools/run_tppa_ra_witness_observer.ps1` is the one-shot request/receipt
boundary around that capture script. Before touching PHD2 it validates the
immutable request with the headless qualification CLI, verifies its own script
digest and the exact point-capture digest, requires enough deadline budget for
one attempt, and writes a create-new attempt marker. It invokes the capture
script exactly once. A successful point is converted to a canonical outcome
and independently revalidated by the CLI. A failed or late attempt is preserved
as a failure artifact, creates no valid outcome, and cannot be retried under the
same evidence paths.

The observer deliberately has no slew, UPAS, completion, or retry endpoint. It
does not terminate a running capture from a parent timeout; PHD2 guide-output
restoration remains owned by the point-capture script's `finally` block. This
protects ordinary process failures, but it is not proof of restoration after
power loss or forceful process termination. Field operation must therefore
retain an external guide-output state check before and after the witness block.

`tools/run_tppa_ra_witness_observer_service.ps1` keeps this boundary available
without opening a new SSH process for every A/B/C/A stop. A producer publishes
only a fully written `*.witness-request.ready.json`; the qualification CLI
creates that file by writing a create-new temporary file and atomically renaming
it. The service holds a global single-instance lease, validates the ready request
before using any request-derived path, writes a create-new digest claim, and
invokes the one-shot observer exactly once. Claims survive service restart, so
an interrupted or failed request cannot be replayed. The service requires the
outcome file before recording success and has no mount, UPAS, retry, or
completion endpoint.

The report-only producer side is
tools/run_guarded_tppa_ra_witness_campaign.ps1. It samples the complete planned
A/B/C/A trajectory at one-degree-or-finer spacing before motion, requires the
north-balcony envelope and 40-degree operational altitude floor, predicts a
constant pier side with a five-degree meridian exclusion, and re-runs the full
preflight at realized UTC before every command. Its parent process watchdogs
actual NINA mount telemetry throughout every exact-equatorial child slew. At
each stationary stop it publishes one atomic immutable request and accepts only
a validated one-attempt outcome. A partial run attempts the same live-watched
return to A unless an actual envelope violation was observed. Neither runner
grants UPAS, completion, or absolute-accuracy authority.

The report-only producer side is
tools/run_guarded_tppa_ra_witness_campaign.ps1. It samples the complete planned
A/B/C/A trajectory at one-degree-or-finer spacing before motion, requires the
north-balcony envelope and 40-degree operational altitude floor, predicts a
constant pier side with a five-degree meridian exclusion, and re-runs the full
preflight at realized UTC before every command. Its parent process watchdogs
actual NINA mount telemetry throughout every exact-equatorial child slew. At
each stationary stop it publishes one atomic immutable request and accepts only
a validated one-attempt outcome. A partial run attempts the same live-watched
return to A unless an actual envelope violation was observed. Neither runner
grants UPAS, completion, or absolute-accuracy authority.

## Frozen numerical policy

The default policy requires:

- total TPPA duration at most 300 seconds;
- at least three fresh determinations;
- maximum pairwise TPPA vector separation at most 0.5 arcminute;
- final TPPA true-pole error at most 1.0 arcminute;
- independent witness true-pole error plus its 95% uncertainty at most 0.5
  arcminute;
- TPPA-to-witness separation plus witness uncertainty at most 0.5 arcminute;
- witness error plus disagreement plus uncertainty at most 1.0 arcminute;
- true-pole refraction adjustment using fresh qualified local pressure,
  temperature, and humidity; and
- the ICRS observation-epoch coordinate frame with qualified site, elevation,
  epoch, and clock provenance.

The uncertainty bound is:

`2 * measurement standard uncertainty + calibration residual + orientation
residual + closure residual + frame bound + distortion bound + mechanical
bound`.

## CLI

Build:

```powershell
dotnet build tools/TppaQualificationCli/TppaQualificationCli.csproj -c Release
```

Bind:

```powershell
dotnet tools/TppaQualificationCli/bin/Release/net8.0/NINA.Plugins.PolarAlignment.Qualification.Cli.dll bind `
  --tppa C:\evidence\tppa-run.json `
  --witness C:\evidence\independent-witness.json `
  --receipt-out C:\evidence\qualification-receipt.json
```

Produce the immutable witness artifact from separately acquired point receipts:

    dotnet tools/TppaQualificationCli/bin/Release/net8.0/NINA.Plugins.PolarAlignment.Qualification.Cli.dll produce-witness `
      --metadata C:\evidence\witness-metadata.json `
      --points C:\evidence\witness-points.json `
      --output-dir C:\evidence

Verify:

```powershell
dotnet tools/TppaQualificationCli/bin/Release/net8.0/NINA.Plugins.PolarAlignment.Qualification.Cli.dll verify `
  --tppa C:\evidence\tppa-run.json `
  --witness C:\evidence\independent-witness.json `
  --receipt C:\evidence\qualification-receipt.json
```

Exit codes:

- `0`: bind qualified and wrote a receipt, or verify succeeded;
- `1`: evidence was structurally valid but numerical policy did not qualify;
- `2`: evidence/provenance invalid; no receipt is written;
- `3`: usage, environment, or internal failure; no receipt is written; and
- `4`: receipt verification failed.

The CLI output directory must not contain the NINA plugin, NINA core, WPF, or
WindowsBase assemblies. This is a headless evidence tool, not an alternate NINA
runtime.
