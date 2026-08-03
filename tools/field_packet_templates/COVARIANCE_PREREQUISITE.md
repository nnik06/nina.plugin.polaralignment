# Covariance Authority Prerequisite

The fast-alignment campaign requires a current covariance authority in
addition to the cadence authority described in `FIELD_READINESS.md`.

Before NINA starts, create a mechanical-epoch receipt and set the returned
`TPPA_MECHANICAL_STATE_ID` user environment value. Restart NINA. Keep UPAS
stationary and collect at least 20 complete schema-6 VerificationOnly evidence
files under one fixed optical train, load profile, hardware configuration,
solver/catalog, true-pole mode, safe arc and temperature envelope. Each file
must contain exactly three fresh qualified determinations and correction
sequence zero.

Seal every exact evidence path and SHA-256 in the schema-1 manifest specified
by `docs/TPPA_COVARIANCE_COMMISSIONING.md`. Failed preregistered attempts must
not be replaced with later runs.

Compile the create-once authority only after sealing the manifest:

```powershell
& $Repo\tools\new_tppa_covariance_authority.ps1 `
  -ManifestPath '<SEALED-COVARIANCE-CAMPAIGN-MANIFEST>' `
  -RepositoryRoot 'C:\Dev\upas-nina-tppa-plugin' `
  -OutputPath '<CREATE-NEW-COVARIANCE-AUTHORITY-PATH>'
```

Preserve the authority ID and file SHA-256. This authority describes
repeatability uncertainty only; it does not establish absolute accuracy. Any
DLL rebuild, optical/load or cable change, solver/catalog change,
mechanical-epoch change, or temperature outside the commissioned interval
invalidates it.

Field order is therefore:

1. supervisor travel/response commissioning and ledgered fit;
2. covariance commissioning and authority;
3. cadence commissioning arms A/B/C/D and authority;
4. separate sealed GT81 and EdgeHD 20-attempt fast-alignment campaigns;
5. same-session guided 900-second bracket for each unchanged optical train.
