# TPPA Covariance Commissioning

The fast actuator path requires a covariance authority tied to the exact plugin
DLL. This authority describes repeatability uncertainty only. It excludes
shared systematic error and grants no absolute-accuracy claim.

## Source campaign

1. Build the final DLL and record its lowercase SHA-256 and repository HEAD.
2. Install that exact DLL without rebuilding it afterward.
3. Keep UPAS stationary. Use one fixed load profile, hardware configuration,
   solver/catalog, true-pole refraction mode, safe sky arc, and mechanical
   epoch.
4. Create a mechanical-epoch receipt before NINA starts with
   tools/new_tppa_mechanical_epoch.ps1. Set the returned
   TPPA_MECHANICAL_STATE_ID in the user environment and restart NINA. The
   receipt hash must remain unchanged for the whole commissioning campaign.
   The epoch represents the unchanged rigid assembly, optical train, load,
   balance, cable routing, and mechanical seating. Rigid-body relocation,
   intended UPAS travel, and manual polar-axis orientation changes do not by
   themselves invalidate it. A tripod/pier component reseat, optical or load
   change, cable-routing change, impact, fastener adjustment, or unexplained
   discontinuity does invalidate it and requires a new receipt and NINA restart.
   Commissioning samples must span the deployment and orientation variability
   intended for the campaign; every individual source run remains stationary.
5. Collect at least 20 complete VerificationOnly run-evidence JSON files. Each
   file must use evidence schema 6, contain exactly three fresh qualified
   determinations, have correction sequence zero, and carry the same mechanical
   state ID as the receipt.
6. Seal every exact evidence path and SHA-256 in a commissioning manifest. Do
   not omit failed preregistered attempts and replace them with later runs.

The manifest uses this frozen schema:

```json
{
  "schemaVersion": 1,
  "repositoryHead": "40 lowercase hex characters",
  "pluginAssemblySha256": "64 lowercase hex characters",
  "hardwareConfigurationId": "exact evidence hardwareConfigurationId",
  "mechanicalStateId": "64 lowercase hex campaign physical-epoch digest",
  "loadProfileId": "hae29c-ec-full-rig-v1",
  "catalogIdentity": "exact commissioned catalog",
  "targetSkyArcId": "safe-arc-a",
  "temperatureC": { "minimum": 25.0, "maximum": 45.0 },
  "evidenceFiles": [
    { "path": "relative/or/absolute/run.json", "sha256": "64 lowercase hex characters" }
  ]
}
```

## Compile the authority

```powershell
pwsh -NoProfile -File tools/new_tppa_covariance_authority.ps1 `
  -ManifestPath C:\evidence\covariance-campaign.json `
  -RepositoryRoot C:\Dev\upas-nina-tppa-plugin `
  -OutputPath C:\evidence\tppa-covariance-authority.json
```

The compiler verifies repository HEAD, every evidence hash, exact DLL identity,
hardware and solver identity, source uniqueness, true-pole refraction, geometry,
closure, arc span, no-motion correction sequence, and temperature range. It
reduces each run to one mean mount-axis vector, computes tangent-plane sample
covariance, and publishes twice the largest eigenvalue as an isotropic floor.
The isotropic form avoids pretending that an arbitrary celestial tangent basis
is the physical AZ/ALT actuator basis.

The artifact deliberately records `confidenceLevel=0.5`: the two-times
empirical covariance floor is conservative engineering input, not a calibrated
probabilistic or absolute-accuracy claim. A large floor may cause the supervisor
to deny movement, which is the intended fail-closed result.

## Installation boundary

Place the generated artifact at the configured covariance-authority path only
after its exact bytes and SHA-256 are preserved. Keep
TPPA_MECHANICAL_STATE_ID set to the receipt hash used for commissioning;
authority loading fails closed when active hardware or the mechanical epoch
does not match. Any DLL rebuild, load-profile
change, solver/catalog change, hardware/mechanical epoch change, temperature
outside the commissioned interval, or source-campaign correction movement
invalidates it. Repeat commissioning rather than editing the JSON manually.
