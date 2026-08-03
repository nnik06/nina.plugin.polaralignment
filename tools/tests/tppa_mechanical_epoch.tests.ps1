$scriptPath = Join-Path $PSScriptRoot '..\new_tppa_mechanical_epoch.ps1'

Describe 'TPPA mechanical epoch receipt generator' {
    It 'creates a sealed receipt whose hash is the mechanical state ID' {
        $output = Join-Path $TestDrive 'epoch.json'
        $result = & $scriptPath -OpticalTrainId 'WO-GT81-IV-0.8-OAG-L-ASI2600MM-gain100-bin1' `
            -PhysicalConfigurationDescription 'GT81 fixed on HAE29C-EC; UPAS scales zero; cables dressed.' `
            -OutputPath $output -EstablishedUtc '2026-08-03T12:00:00Z'
        $receipt = [IO.File]::ReadAllText($output) | ConvertFrom-Json
        $result.MechanicalStateId | Should Match '^[0-9a-f]{64}$'
        $result.MechanicalStateId | Should Be ((Get-FileHash $output -Algorithm SHA256).Hash.ToLowerInvariant())
        $receipt.schemaVersion | Should Be 1
        $receipt.invalidatedBy.Count | Should BeGreaterThan 3
        $result.SetForNextNinaLaunch | Should Match 'TPPA_MECHANICAL_STATE_ID'
    }

    It 'refuses to overwrite an existing epoch receipt' {
        $output = Join-Path $TestDrive 'existing.json'
        [IO.File]::WriteAllText($output, 'preserve')
        $message = try {
            & $scriptPath -OpticalTrainId 'WO-GT81-IV-0.8-OAG-L-ASI2600MM-gain100-bin1' `
                -PhysicalConfigurationDescription 'fixed rig' -OutputPath $output
            ''
        } catch { $_.Exception.Message }
        $message | Should Match 'Refusing to overwrite'
        [IO.File]::ReadAllText($output) | Should Be 'preserve'
    }

    It 'mints distinct identities for separately established epochs' {
        $first = & $scriptPath -OpticalTrainId 'EdgeHD-9.25-0.7-OAG-L-ASI2600MM-gain100-bin1' `
            -PhysicalConfigurationDescription 'epoch one' -OutputPath (Join-Path $TestDrive 'one.json')
        $second = & $scriptPath -OpticalTrainId 'EdgeHD-9.25-0.7-OAG-L-ASI2600MM-gain100-bin1' `
            -PhysicalConfigurationDescription 'epoch two' -OutputPath (Join-Path $TestDrive 'two.json')
        $first.MechanicalStateId | Should Not Be $second.MechanicalStateId
    }
}
