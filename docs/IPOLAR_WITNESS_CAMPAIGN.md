# iPolar Witness Campaign

## Status

The iOptron iPolar is a **corroborating witness** in the TPPA absolute-accuracy
campaign. It is not ground truth, it is not a reference standard, and it has no
control authority over anything.

This document and the tooling under `tools/ipolar_witness_campaign.ps1` cover
evidence collection and qualification only. Nothing here changes TPPA geometry,
the estimator, or completion behaviour, and nothing here can move the UPAS.

## Why iPolar was added

TPPA has demonstrated good short-term internal repeatability. Its absolute
accuracy below 60 arcseconds remains unproven. Same-arc TPPA repeats share the
same plate-solving geometry and therefore share systematic errors; repeating them
cannot expose a common-mode bias.

iPolar is useful because it is independent in the places that matter: separate
optics, separate sensor, separate pole-region geometry, and separate vendor
software. A disagreement between TPPA and iPolar is informative. An agreement is
weak evidence in the same direction, and nothing more until iPolar's own
uncertainty has been measured.

PHD2 Polar Drift Alignment and the balcony drift measurements are not qualified
ground truth either. See `PHD2_POLAR_DRIFT_ABSOLUTE_VALIDATION.md` and
`TPPA_DRIFT_VALIDATION.md`.

## Repeatable, corroborated, certified

These three words are not interchangeable, and most of the confusion in this
project has come from treating them as if they were.

- **Repeatable** - the same method, repeated on unchanged hardware, returns the
  same answer. This measures precision. It says nothing about accuracy: a method
  with a constant systematic bias is perfectly repeatable and perfectly wrong.
  TPPA is repeatable today.
- **Corroborated** - a second, independent method agrees within some stated
  tolerance. This raises confidence and can expose a bias that repetition cannot.
  It is not proof, because two methods can share an error source, and because the
  witness's own uncertainty may be larger than the effect being tested. iPolar
  can deliver corroboration.
- **Certified** - an absolute claim, defensible against a qualified reference
  whose uncertainty is itself characterized. Neither TPPA nor iPolar can deliver
  this. Certification still requires qualified open-sky drift measurement.

A campaign that reaches `QualifiedCorroboratingWitness` has produced
corroboration. It has not certified anything.

## The vendor resolution figure

iOptron states an approximately 30 arcsecond resolution and maximum alignment
precision for iPolar. That is a vendor specification. It is not a characterized
statistical uncertainty, it is not a one-sigma value, and it must never be used
as one.

The evaluator therefore contains no hard-coded TPPA/iPolar agreement threshold
and performs no arithmetic with the vendor figure. Any comparison tolerance must
come from an explicit uncertainty policy file whose values were derived from
measured repeatability data, and whose `Source` field records who set it and on
what evidence.

## Required field sequence

Each comparison block runs in this order, with no hardware change of any kind
inside it:

```text
iPolar -> TPPA A -> TPPA B -> TPPA A -> iPolar
```

Longer blocks are allowed as long as they still open and close with an iPolar leg
and the TPPA legs alternate `A, B, A, B, A`. `RecordBlock` enforces this and
refuses anything else.

The iPolar legs bracket the TPPA legs so that any drift in the iPolar reading
across the block is visible, and so that the TPPA measurements sit inside a
single known iPolar state. The repeated `A` leg cancels first-order linear drift
in the TPPA arc, which is the same reasoning used by the existing verification
diagnostic.

### iPolar must stay mounted and untouched inside a block

The external iPolar attaches to the HAE29C-EC body with thumb screws. Removing
and refitting it changes the mechanical transfer between the iPolar optical axis
and the mount's RA axis, by an amount nobody has measured yet. That change is
indistinguishable from a real polar-alignment change in the data.

Therefore: never unmount, loosen, rotate, or recalibrate iPolar inside an A/B/A
block. The evaluator rejects any campaign whose reseat events fall inside a
recorded block window, and reports the offending event sequence numbers.

## Two separate experiments

Mounting-transfer uncertainty and readout repeatability are different quantities
and must be measured separately. Mixing them produces one meaningless combined
number.

1. **Fixed-mount camera-centre calibration.** iPolar stays bolted in place. Run
   the camera-centre calibration cycle repeatedly, recording the RA positions
   used where the vendor software exposes them. This measures the repeatability
   of the calibration and readout alone. At least five cycles. Recorded with
   `-Command RecordCalibration`, which refuses `-CameraRemovedOrReseated`.
2. **Remove / reseat / recalibrate.** Fully remove iPolar, refit it, and
   recalibrate. This measures the mounting-transfer uncertainty that a thumb-screw
   bracket introduces. At least five cycles, each with recalibration. Recorded
   with `-Command RecordReseat`.

The reseat scatter is expected to dominate. Until both scatters are known, no
agreement threshold between TPPA and iPolar can be justified.

## Feasibility gate at this site

The altitude of the north celestial pole equals the observer's latitude, so at
the Dubai balcony site the NCP sits at roughly 25 degrees. That is near the lower
boundary of the measured balcony opening.

Before collecting any campaign data, confirm and record that:

- the iPolar's line of sight to the pole region actually clears the balcony wall
  and any railing, at the mount's operating position;
- enough pole-region sky is visible for the vendor software to solve, not merely
  enough for the pole to be nominally above the horizon;
- the obstruction situation is stable for the whole session.

If the pole region is clipped, the campaign is not feasible at this site and the
solve-reliability gate will fail honestly rather than producing marginal data.
Do not work around a clipped field by relaxing the gates.

## True pole versus apparent pole

TPPA and iPolar must be compared in the same convention. A refracted apparent
pole and the true pole differ by an amount that is significant at the accuracy
this campaign is trying to establish, and the difference at roughly 25 degrees
altitude is not negligible.

The campaign header records `PoleConvention` as `TruePole`, `ApparentPole`, or
`Unknown`. `Unknown` is a hard rejection: a comparison whose convention is
unstated cannot be interpreted, and guessing it later is not permitted. If the
convention iPolar uses cannot be established from vendor documentation or a
controlled test, the correct action is to record `Unknown`, accept the rejection,
and resolve the question before collecting more data.

## Environmental and dark-frame requirements

Every campaign must record, and the evaluator requires:

- site latitude, longitude, elevation, and the source of those values;
- absolute station pressure, temperature, relative humidity, their source, and
  their age. Station pressure means the pressure at the site, not a sea-level
  reduced value from a weather service. A reading older than the configured limit
  is rejected rather than used;
- a dark frame with its capture time and SHA256. The dark frame must not be stale
  relative to the solve attempts it supports.

Unknown values are recorded as absent. They are never coerced to zero. An absent
elevation stays `null` and fails the gate; it does not silently become sea level.

## No documented numeric API

The iPolar manual documents a visual cross-and-circle interface. It documents no
signed numerical export and no automation API. This tooling therefore assumes
that no machine-readable iPolar result exists.

Consequences, all enforced:

- The tool never automates vendor UI clicking. If a documented and testable
  interface is discovered later, automating it becomes a separate, reviewed
  change; until then, UI automation is not a safe basis for evidence.
- OCR of the vendor window and pixel measurement of the cross position are
  recorded, if supplied, as `Ocr` or `ScreenshotPixelMeasurement`, and are
  explicitly excluded from the numeric evidence with a stated reason. Screen
  pixels are not calibrated angles.
- A number transcribed by hand from the vendor UI is admissible, but only as
  `DocumentedManualReadout`, and only with a non-empty `Source`. It is flagged as
  manual everywhere it appears.

This premise should be re-checked against any future iPolar release. It is a
recorded assumption, not a permanent fact.

## Why screenshots cannot certify sub-arcminute accuracy

A screenshot proves what the vendor software displayed. It does not establish:

- the angular scale of the display, or its stability across sessions;
- where the true pole lies in that image, independent of the vendor's own
  calibration;
- the convention in use;
- the uncertainty of the reading.

Reading a cross position off an image and converting it with an assumed scale
manufactures precision that the evidence does not contain. A campaign whose only
evidence is visual therefore evaluates to `QualitativeWitnessOnly`, which is an
honest verdict rather than a failure.

## Future option: raw capture and independent plate solving

The path to genuinely reviewable iPolar numbers is to capture raw frames from the
iPolar camera and plate-solve them with a solver this project controls, rather
than to read the vendor's answer. That would give an independently checkable
pole position with an uncertainty this project can characterize.

The schema already accommodates it: `-Command RecordArtifact -ArtifactKind
RawFrame` preserves and hashes raw frames alongside screenshots. Implementing the
capture and solve path is separate future work and is not part of this change.

## Open-sky certification remains outstanding

Every report this tooling produces states, unconditionally, that absolute
certification is outstanding and that a qualified open-sky drift reference is
still required. No campaign outcome removes that statement.

## Qualification levels

The evaluator emits exactly one of:

| Level | Meaning |
| --- | --- |
| `Rejected` | A hard gate failed. The campaign supports no iPolar claim at all. |
| `QualitativeWitnessOnly` | Cross/circle evidence only. Qualitative corroboration; certifies nothing numerical. |
| `QuantitativeUnqualified` | Numbers exist, but iPolar readout uncertainty is not characterized. No agreement threshold may be applied. |
| `QualifiedCorroboratingWitness` | Calibration repeatability, reseat uncertainty, pole convention, and readout provenance all pass an explicitly supplied policy. Corroborates TPPA. Still not ground truth. |

`GroundTruth` and `CertifiedAbsoluteAccuracy` are not emittable. Requesting either
in the campaign header is itself a hard rejection, and the evaluator throws if an
internal path ever tries to produce one.

## Default screening gates

- at least 10 solve attempts and at least 9 successes;
- at least 5 fixed-mount camera-centre calibration cycles;
- at least 5 remove/reseat/recalibration cycles, each with recalibration;
- no reseat event inside any recorded no-motion block;
- complete site, atmosphere, pole convention, dark frame, and artifact
  provenance;
- no reused, stale, empty, or missing artifact;
- no non-finite or implausible numeric value.

These are collection-completeness gates. Passing them does not qualify the
witness; only an explicit uncertainty policy does that.

## Evidence integrity

Events are appended to `<campaign>/events.jsonl`, one JSON object per line. Each
event carries its sequence number, UTC timestamp, the hash of its predecessor,
and its own SHA256 content hash. The first event chains to a genesis hash
computed over the whole campaign header, so editing the header, editing an event,
reordering events, or re-hashing a single event to cover an edit all fail
verification.

Artifacts are hashed, size-checked, copied into `<campaign>/artifacts/` under
their event sequence number, and never overwritten. Each artifact must be
provably newer than the preceding one. Content already recorded under another
event is refused, so one capture cannot be presented twice as two independent
observations.

### The preserved bytes are what count

Hashing the operator's source file proves nothing about the copy the campaign
keeps. Both are verified:

- **At record time.** The copy is written to a private staging file, and the
  staged bytes are rehashed and restatted against the source before the event is
  appended. After the event is appended the staging file is promoted to its
  committed `000N-` path and verified once more. A copy that does not match its
  source never becomes campaign evidence.
- **At every filesystem-aware evaluation.** `Evaluate` and `Finalize` rehash and
  restat every recorded evidence file and compare it to what the event log says.
  Missing, modified, size-changed, or unreadable evidence raises the
  `ArtifactIntegrity` gate, forces `IntegrityValid` to false, and rejects the
  campaign.

`Finalize` additionally refuses to run at all while any evidence fails
verification. A campaign may legitimately finalize as `Rejected` - a documented
rejection is a real result - but finalization mints an immutable manifest of
hashes, and stamping that over bytes known to have changed would enshrine a false
record.

### Preserved evidence versus external evidence

Two classes are treated differently, deliberately:

- **Preserved** evidence is a dark frame or an artifact. The campaign copied it,
  owns its lifetime, and requires it to be present and byte-identical forever.
  Absence or alteration fails closed.
- **External** evidence is a linked TPPA run artifact. The campaign records its
  path, hash, and size but never copies it and does not own its lifetime. If it
  later disappears, that is reported as a recorded limitation rather than a
  campaign defect. If it is still present but no longer matches its recorded
  hash, the evidence the comparison rests on has changed, and that does fail
  closed.

The filesystem check lives in `Test-IPolarPreservedEvidence`, which reads files
and reaches no verdict. `Invoke-IPolarCampaignEvaluation` stays pure: it consumes
the resulting facts through `-ArtifactIntegrity` and never touches a disk. An
evaluation performed without those facts reports
`ArtifactIntegrity.Source = NotSupplied` and says plainly that the recorded
hashes were not compared against the bytes on disk.

### Finalization is terminal

`CampaignFinalized` may occur exactly once and must be the last event in the
chain. Every recording command is refused after it, and a refused command does
not change the event log by a single byte. Evaluation independently rejects a
campaign whose `CampaignFinalized` is duplicated or is followed by any other
event, so a hand-edited log cannot smuggle events past finalization.

Re-running `Finalize` on an already finalized campaign is the one permitted
post-finalization operation. It appends nothing and reconstructs any report a
previous interrupted run did not write, byte for byte, refusing to overwrite a
report whose content differs.

### Recording is transactional

All validation happens before any evidence is committed. The preserved copy is
staged under a name unique to that invocation, and nothing appears at the
committed `000N-` path until the corresponding event has been appended. A
rejected timestamp, a duplicate capture, or an interrupted import therefore
leaves no file at the committed path and never blocks a corrected retry. On
failure only that invocation's staging file is removed; committed evidence is
never deleted or overwritten. A genuine collision at a committed path still fails
closed.

### Concurrent writers

Every mutation - artifact reservation, artifact copy, event append, and
finalization - runs inside a single campaign-scoped interprocess lock held on
`<campaign>/.campaign.lock`. Campaign state is re-read and revalidated under that
lock immediately before the append, so the sequence number and predecessor hash
are always live rather than a stale snapshot. Lock acquisition is bounded by
`-LockTimeoutSeconds` (default 30) and fails with a clear message rather than
waiting forever.

Without the lock, parallel writers read the same next sequence number and append
duplicate sequences; a regression test runs six barrier-synchronised writers and
requires a contiguous, unique, hash-valid chain with no orphan staging files.
Event lines are written as UTF-8 bytes without a BOM and flushed to the storage
device, so a crash cannot leave a torn line.

`Evaluate` is read-only and takes no lock. It reads a campaign that a writer is
actively appending to only as a whole-line parse, and a partial line fails closed
with a parse error rather than being silently accepted.

Hashes are computed over a canonical JSON rendering with ordinally sorted keys,
invariant number formatting, and a single UTC timestamp form. This is required
because Windows PowerShell 5.1 and PowerShell 7 disagree about whether an ISO
timestamp reads back as a string or a `DateTime` and whether `1.5` reads back as
`Decimal` or `Double`. A regression fixture pins the canonical text and its hash
so a campaign written on one host stays verifiable on the other.

## Recording a campaign

```bash
pwsh -File tools/ipolar_witness_campaign.ps1 -Command Help
```

Typical order: `Init`, `RecordEnvironment`, `RecordDarkFrame`, repeated
`RecordSolveAttempt`, the two repeatability experiments via `RecordCalibration`
and `RecordReseat`, `RecordArtifact` for each screenshot or raw frame,
`LinkTppaArtifact` for each TPPA run being compared, `RecordBlock` for each
no-motion A/B/A window, then `Evaluate` and finally `Finalize`.

`Finalize` appends a `CampaignFinalized` event and writes four files under
`<campaign>/reports/`: the JSON report, the Markdown report, a compact council
packet that references artifacts by path and SHA256 without redistributing vendor
imagery, and a manifest hashing the header, the event log, and every report. It
validates every destination before appending the final event and refuses to
overwrite an existing report. If output was interrupted after that event,
rerunning `Finalize` reconstructs missing files, accepts byte-identical existing
files, refuses mismatches, and never appends a second finalization event.

`Evaluate` and `Finalize` exit 2 when the campaign is `Rejected`.

## Boundaries

This tooling does not, and must not:

- change TPPA geometry, the estimator, or completion thresholds;
- move the UPAS or command any actuator;
- connect to UPASBridge, NINA, PHD2, the mount, a Switch, a Weather device, or a
  Safety Monitor;
- automate iPolar alignment or vendor UI;
- treat UI pixels or OCR as calibrated angles;
- invent an iPolar API;
- claim absolute sub-arcminute accuracy.

Future UPAS perturbation experiments belong to the movement supervisor and the
P20 workflow, not here.

## Tests

- `tools/tests/ipolar_campaign_evaluator.tests.ps1` covers the pure evaluator,
  canonical serialization, and the report builders.
- `tools/tests/ipolar_witness_campaign_cli.tests.ps1` covers the CLI, evidence
  integrity, tamper detection, and finalization.

Both run under Windows PowerShell 5.1 and PowerShell 7.
