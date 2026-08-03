# TPPA Fast-Alignment Campaign Protocol

This protocol qualifies an operational alignment result, not traceable absolute
polar-axis accuracy.

1. Before sealing a campaign, commission one cadence-authority artifact from at
   least 20 transitions over at least two nights. It must show zero false-stable
   exits and no more than 0.5 arcminute vector separation from the 30-second
   reference. The authority is valid only for its exact plugin DLL, hardware
   configuration, mechanical epoch, load profile, temperature range, settle,
   and measured worst-case fresh-determination duration.
2. Create one schema-5 manifest with `new_tppa_fast_alignment_campaign.ps1`
   before the first attempt. Bind the exact repository HEAD, plugin DLL SHA-256,
   covariance authority, cadence authority, qualified settle and fresh duration,
   mechanical-state receipt, load profile, optical train, log set, time window,
   attempt denominator, and starting-error strata.
3. Set the returned `TPPA_PREREGISTERED_CAMPAIGN_ID` user environment command
   and restart NINA before the first attempt. Runtime admission fails before
   physical-zero movement when this binding is absent or malformed.
4. Publish the manifest SHA-256 in the append-only field ledger and pass it to
   the analyzer as `-ExpectedCampaignManifestSha256`.
5. Every attempt begins with a fresh supervisor observation of the physical
   UPAS scales. If both conservative signed bounds lie within +/-0.1 degree,
   the supervisor mints a fresh zero witness without movement. Otherwise it
   performs one bounded physical-zero transaction and must re-witness both axes
   inside +/-0.1 degree. Missing, ambiguous, stale, or reused evidence denies
   TPPA start. Controller MPos is never accepted as physical zero.
6. The five-minute clock starts only after physical-zero admission. Runtime
   settle must exactly match the commissioned cadence, and the runtime reserves
   the authority's measured worst-case fresh-determination duration before each
   move and final confirmation. Without a current matching cadence authority,
   the actuator-capable fast path does not start; ordinary non-fast operation
   retains the unconditional 30-second settle floor.
7. The objective envelope is 0--300 arcminutes. Errors up to 24 arcminutes use
   the fine controller; larger errors use the separately guarded coarse planner
   before fine handoff. Every plan remains subject to signed response evidence,
   physical headroom, uncertainty, reversal, regression, budget, and the
   +/-5.4-degree software travel envelope. Admission never guarantees a move.
8. Completion requires two independent fresh stationary true-pole TPPA
   determinations no greater than 3 arcminutes, with no reused continuous
   estimator evidence. Refraction adjustment must be enabled.
9. Every initiated attempt in the sealed window counts. Admission rejection,
   cancellation, crash, missing terminal telemetry, unsafe state, or failure to
   complete within 300 seconds remains a denominator failure. Do not replace an
   incomplete campaign with an unreported new manifest.
10. Run separate campaigns for GT81 and EdgeHD. Each campaign covers contiguous
    0--30, 30--60, 60--120, 120--180, 180--240, and 240--300 arcminute strata,
    with its exact preregistered count and at least one success in every stratum.
11. `PreregisteredCampaignPassRateMet` is a point estimate over the sealed
    denominator, not a confidence-bounded population guarantee. The default
    claim requires at least 16 of 20 attempts over at least three nights, with
    zero false success and zero safety violation.

The software hard limit is +/-5.4 degrees, about 0.6 degree inside the observed
mechanical stops near +/-6 degrees. Hall sensing may later improve actuator
position, rollback, backlash measurement, and limit confidence, but it does not
measure celestial polar error and cannot replace either fresh TPPA result.

Passing the alignment campaign establishes the automated mount-alignment
capability. Imaging acceptance remains a same-session guided 900-second
narrowband bracket whose corner-star behavior shows that alignment is not the
limiting term for each optical train.
