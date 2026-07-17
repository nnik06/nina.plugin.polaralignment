@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%USERPROFILE%\Documents\run_tppa_soak_2h.ps1" > "%USERPROFILE%\Documents\run_tppa_soak_2h.stdout.log" 2>&1
