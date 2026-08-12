# Usable Sky Window Survey Protocol

## Purpose

Select a repeatable field for TPPA before any alignment or UPAS correction.
This is an imaging/solve survey, not an actuator test. It avoids spending a
TPPA three-point run on an arc whose last waypoint has too few stars or exceeds
the balcony/mount envelope.

## Preconditions

1. NINA has one current process, its current PID-bound log has no unhandled
   dispatcher exception, and its mount API reports connected. The supervisor
   in commit `53eac02` checks this before it starts a sequence.
2. The mount is stationary, clear of the pier/wall, and the intended field and
   every TPPA waypoint fit the configured envelope. A camera frame alone does
   not authorize the later TPPA arc.
3. Keep UPAS automatic correction disabled. Do not use P20 scale readings for
   this survey; there is no UPAS movement.
4. Keep the exposure, gain, binning, focus, filter, and solver settings fixed
   for the complete survey.

## Candidate Order

Survey only the user-approved northern opening: Azimuth 280 through 010
degrees and Altitude 20 through 70 degrees. Start with the previously proven
western reference, then expand outward only when necessary:

| Order | Azimuth | Altitude | Reason |
|---:|---:|---:|---|
| 1 | 300 | 45 | Previously had guide-star detection. |
| 2 | 290 | 45 | Previously had guide-star detection. |
| 3 | 280 | 45 | Previously had guide-star detection. |
| 4 | 310 | 39--45 | Previously produced TPPA solves. |
| 5 | 320 | 45 | Tests the next west-to-north sector. |
| 6 | 340 | 45 | Tests the central northern sector. |
| 7 | 000 | 45 | Tests the eastern edge of the north sector. |
| 8 | best azimuth above | 60--65 | Tests high altitude only after a 45-degree success. |
| 9 | best azimuth above | 30--35 | Tests lower altitude only after a 45-degree success. |
| 10 | best azimuth above | 20--25 | Last resort; use only with explicit mount-envelope clearance. |

The list is deliberately not a slew script. A human-approved or separately
qualified mount route chooses each pointing and rechecks actual telemetry after
the slew. Do not use a failed point to extrapolate a safe next point.

## Per-Candidate Test

1. Slew and settle. Independently confirm `Slewing=false` and actual
   azimuth/altitude are within the approved window.
2. Acquire two frames at the fixed settings, at least 15 seconds apart, with no
   pointing change. Plate solve each frame.
3. Record actual AZ/ALT, exposure settings, solve result, solve residual if
   available, star count if available, and the two solve mid-times.
4. Mark the candidate **usable** only when both captures succeed with the
   configured solver. A timeout, capture error, RPC error, or ambiguous result
   is an equipment/session rejection, not a reason to descend to 20 degrees.
5. After selecting a usable field, separately preflight the whole intended
   TPPA A-B-C arc. The initial field's success does not prove its later
   waypoints are viable.

## Selection And Fallback

Prefer the lowest-order usable candidate with the greatest altitude margin and
two successful solves. If the 70-degree field has insufficient stars, return to
the 45-degree reference band first; do not jump directly to 20 degrees. Use
20--25 degrees only after a clean, repeatable failure of higher candidates and
an explicit full-arc envelope check. It has the least obstruction and
refraction margin.

## Evidence

Save a small JSON or CSV artifact for every candidate, including rejected ones.
The next TPPA field packet should cite the selected candidate and its two
successful preflight solves. This evidence supports field selection only; it
does not claim polar-alignment accuracy.
