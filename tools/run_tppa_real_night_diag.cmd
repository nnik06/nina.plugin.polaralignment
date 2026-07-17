@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%USERPROFILE%\Documents\run_tppa_real_night_diag.ps1" > "%USERPROFILE%\Documents\run_tppa_real_night_diag.stdout.log" 2>&1
