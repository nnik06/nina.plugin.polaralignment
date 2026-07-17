& "$env:USERPROFILE\Documents\tppa_phd2_supervisor.ps1" `
    -Mode BurstThenDrift `
    -RepeatBursts 60 `
    -Cycles 3 `
    -DriftMinutes 1 `
    -SettleSeconds 5 `
    -TppaInterRunSettleSeconds 10 `
    -TppaTimeoutMinutes 12 `
    -TppaAutoStopSeconds 15 `
    -SequencePath "$env:USERPROFILE\OneDrive\Documents\N.I.N.A\1_TPPA_PHD2_DIAG.json"
