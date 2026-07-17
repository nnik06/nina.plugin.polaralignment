$stopAt = (Get-Date).Date.AddHours(4)
if ((Get-Date) -ge $stopAt) { $stopAt = $stopAt.AddDays(1) }

& "$env:USERPROFILE\Documents\tppa_phd2_supervisor.ps1" `
    -Mode BurstThenDrift `
    -RepeatBursts 30 `
    -StopAt $stopAt `
    -Cycles 3 `
    -DriftMinutes 12 `
    -SettleSeconds 30 `
    -TppaInterRunSettleSeconds 20 `
    -TppaTimeoutMinutes 20 `
    -TppaAutoStopSeconds 0 `
    -SequencePath "$env:USERPROFILE\OneDrive\Documents\N.I.N.A\1_TPPA_PHD2_DIAG.json"
