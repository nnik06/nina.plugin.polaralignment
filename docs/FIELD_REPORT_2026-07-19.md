# Field Report - 2026-07-19

## Build and deployment

- Reviewed commits `29fbe19` and `db2f71d`.
- `db2f71d` passed 129/129 tests.
- Deployed DLL SHA-256: `EFE6C9548FEB1FD91F287C59D5A9F834F6AD08420BA5B5D12AE7EEF1270AAD6F`.
- Rollback directory: `backup-before-db2f71d-20260719`.

## VerificationOnly

`VerificationOnly` is automated-mount-only. It performs two identical A-B-C determinations (six solves total), causes no actuator side effects, restores position A with a pre-run-position fallback, and reports the delta between determinations.

## Council review

- Antigravity identified manual-mode and restore-target concerns; both were adopted.
- Claude identified duplicate validation and the normal step reset; both were fixed.

## PHD2 reanalysis

- The 7.81-minute run was ineligible because it was too short.
- The 19.83-minute run was ineligible because it had mixed sign; two-minute DEC windows ranged from -1.191 to +0.390 arcsec/min.
- Therefore, the data supports no valid conclusion about disagreement with TPPA.

## Shutdown

The scheduled task falsely returned 0 because its CMD wrapper contained literal backtick-r/backtick-n text instead of real newlines, so PowerShell never launched. This was detected at 04:25, and the PowerShell script was run directly.

Final state: mount parked with tracking false; panel closed with light false; camera, filter wheel, focuser, rotator, guider, flat device, mount, and switch disconnected; N.I.N.A. closed.

Prevention: schedule `powershell.exe -File` directly, or byte-verify the wrapper and require the shutdown log to exist.

## Next session

1. Run passive `VerificationOnly` first.
2. Make one conservative correction only after the determinations agree.
3. After settling, collect at least 12 minutes of PHD2 drift.
4. Compare results only when stationarity is eligible.
