# TPPA Fast-Alignment Campaign Protocol

This protocol qualifies an operational alignment result, not traceable absolute
polar-axis accuracy.

1. Create one manifest with `new_tppa_fast_alignment_campaign.ps1` before the
   first attempt. Use dedicated campaign logs and bind the exact repository HEAD, plugin DLL SHA-256, commissioned covariance-authority ID and artifact SHA-256, mechanical-state receipt SHA-256, load profile, and declared optical train. Runtime `started` telemetry must match those values exactly; a rebuilt DLL or swapped authority invalidates the attempt.
2. Immediately publish the returned SHA-256 in the append-only field-session
   ledger. Pass it to the analyzer as `-ExpectedCampaignManifestSha256`.
3. Every initiated attempt in the sealed window counts. Admission rejection,
   cancellation, crash, missing terminal telemetry, a safety-gate violation, or
   failure to obtain two fresh stationary determinations at no more than 3
   arcminutes within 300 seconds is a failure.
4. Report every sealed manifest, including failed or incomplete campaigns. Do
   not replace an unreported failed campaign with a new manifest.
5. Run separate campaigns for the GT81 and EdgeHD trains. `OpticalTrainId` is a
   declared setup label, not runtime-verified telemetry.
6. `PreregisteredCampaignPassRateMet` is the observed point estimate over that
   sealed denominator. The report also includes a Wilson 95 percent interval;
   neither the point estimate nor that descriptive interval is a population
   reliability guarantee, and attempts within one night are correlated.
7. The objective envelope is 0--300 arcminutes. Record every attempted start;
   a controller admission rejection inside that objective envelope is a failed
   attempt, not permission to narrow the campaign after sealing.
8. Before sealing a campaign, sweep the coarse planner over every stratum using
   the lower qualified response bound and the upper witness/calibration
   uncertainty bounds. If any required stratum cannot preserve guarded
   headroom, the 0--300 arcminute objective is not qualified; do not silently
   shrink or relabel that campaign.
9. Each attempt begins from an independently established rough alignment. Log
   its initial stratum before any movement or outcome is known; repeated runs
   from one unchanged starting state do not create independent attempts.
10. The signed response calibration must carry a non-empty identity and use
   `azEastPositive_altUpPositive` with
   `tppaErrorAfter=tppaErrorBefore+response*physicalDelta`. Missing,
   non-positive, stale, or incompatible calibration denies motion.
11. The five-minute objective remains unqualified until measured cadence shows
   that the complete initial measurement, every supervisor transaction,
   inter-move fresh feedback, and terminal independent confirmation fit inside
   300 seconds. A manifest records failures; it never proves timing feasibility.
12. The manifest seals contiguous starting-error strata over the whole envelope.
   Each stratum must contain its exact preregistered attempt count and at least
   one success; overall success still requires the sealed 80 percent rate.

The 0--300 arcminute campaign is an evidence denominator, not proof that every
start is presently movable. The current coarse planner remains limited to
0--240 arcminutes and reserves at least 1 degree of nominal travel on either
axis. The software hard limit is +/-5.4 degrees, approximately 0.6 degree inside
the observed mechanical stops near +/-6 degrees. A start in the 240--300
arcminute stratum that cannot preserve the configured reserve is a campaign
failure; it is never silently excluded or converted into motion authority.

The current fixed-gain controller is qualified only over its separately stated
0--24 arcminute admission envelope. The 0--300 arcminute campaign envelope is
an objective/evidence boundary, not an actuator boundary. Starts above 24
arcminutes must remain motion-denied until a separately tested coarse-to-fine
controller and external supervisor headroom contract are commissioned; changing
the manifest alone never authorizes wider UPAS motion.

Imaging acceptance remains a same-session guided 900-second bracket whose
corner star shapes show that alignment is not the limiting term.