# Coarse TPPA Evidence Contract

Checkpoint basis: TPPA `f43f1f8b85da7c39dffde754a96988d8ec0148e1` and
supervisor worktree `6f403566`.

## Purpose

The live coarse stage is intentionally not commissioned yet. The transport and
route boundaries exist, but motion authority requires evidence that the current
ordinary three-point workflow does not produce.

## Frozen Route

- `0..24` arcmin total: existing fine controller only.
- `>24..300` arcmin total: separately commissioned coarse supervisor only.
- `>300` arcmin total: reject without actuator movement.
- Physical zero admission happens before the 300-second timer.
- A successful run still ends on two independent fresh stationary true-pole
  determinations at or below the configured 3 arcmin operational tolerance.

## Determination Receipt

Each coarse input determination must be sealed after its third solve and before
any actuator command. The canonical receipt must include:

- schema, determination, campaign, mechanical-state, and qualified-arc IDs;
- plugin, NINA, solver/catalog, site, atmosphere, and true-pole model identity;
- UTC observation mid-times plus monotonic start/end and budget elapsed time;
- three immutable point records with exposure bounds, image SHA-256, solved
  coordinates, side of pier, achieved geometry, and available solve-quality
  observations;
- a complete motion-log segment proving the determination was stationary;
- physical-zero evidence identity and fresh pre-request physical-axis snapshot;
- signed azimuth/altitude result and repeatability-only covariance;
- covariance method/version and commissioned covariance-artifact SHA-256;
- an explicit `sharedSystematicIncluded=false` assertion.

Canonical bytes are domain-separated and RFC 8785 serialized. The receipt is
append-only. The supervisor must recompute its digest; an opaque caller-supplied
hash is not sufficient.

## Covariance

An exact three-point solution has zero residual degrees of freedom. Covariance
must not be inferred from that fit or estimated from only two determinations.

Production motion authority requires one of these prequalified sources:

1. propagated per-solve coordinate covariance through the numerical Jacobian of
   the exact implemented three-point estimator, with coverage validated by
   Monte Carlo; or
2. a signed commissioned repeatability covariance artifact produced by prior
   no-motion campaigns for the same hardware, load, solver, exposure, arc,
   atmosphere stratum, and software identity, with online pair disagreement
   allowed only to inflate it.

The supervisor's shared TPPA systematic is added once after combining random
covariance. It must never be copied into each determination covariance.

Two fresh determinations are a repeatability falsifier, not an absolute-accuracy
certificate. They may authorize coarse motion only against prequalified
covariance. Missing or stale covariance authority is a denial.

## Current Gaps

The ordinary live three-point path does not currently preserve all required
point evidence. `TppaVerificationPointReceipt` lacks image hashes, exposure
bounds, solver identity/quality, and a complete stationarity ledger. NINA's
`PlateSolveResult` usage in this plugin does not expose a qualified coordinate
covariance. Therefore the HTTPS coarse client must remain unwired from live
motion until the receipt pipeline and commissioned covariance source exist.

The existing overdetermined estimator has residual degrees of freedom but is a
report-only diagnostic and does not fit the current five-minute timing budget as
a replacement for every three-point determination.

## Required Tests

- cross-language RFC 8785 digest vectors and locale independence;
- receipt mutation, replay, overlap, stale evidence, and missing motion-log
  denial tests;
- analytic/numerical Jacobian agreement and Monte Carlo covariance coverage;
- common-mode systematic invariance of the pair disagreement statistic;
- covariance symmetry, positive-semidefiniteness, bounds, and artifact identity;
- no coarse request for `<=24` arcmin and no fine request for `>24` arcmin;
- no motion when receipt, covariance, lease, physical evidence, calibration,
  travel envelope, or remaining runtime is unqualified;
- end-to-end simulated campaigns at p95 solve cadence before field deployment.

## Council Record

Claude Opus 5 High and Gemini 3.1 Pro High agreed that two three-point runs do
not estimate covariance or establish absolute accuracy. Both required
propagation from qualified solve uncertainty. Claude additionally recommended a
commissioned/validated covariance model and optional third determination for
marginal repeatability when budget permits. Gemini's suggestion to move after a
single determination was rejected because it conflicts with the frozen
independent pre-move pair requirement.
