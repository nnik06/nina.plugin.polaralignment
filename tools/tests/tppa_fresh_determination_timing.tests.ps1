$analyzer = Join-Path $PSScriptRoot '..\analyze_tppa_fresh_determination_timing.ps1'
$producer = Join-Path $PSScriptRoot '..\new_tppa_fresh_determination_timing_receipts.ps1'

function New-TimingCampaign([string]$Root, [double]$FirstDuration = 60.0) {
    New-Item -ItemType Directory -Path $Root | Out-Null
    $runtimeDirectory = Join-Path $Root 'runtime'
    New-Item -ItemType Directory -Path $runtimeDirectory | Out-Null
    $pluginPath = Join-Path $runtimeDirectory 'NINA.Plugins.PolarAlignment.dll'
    [IO.File]::WriteAllBytes($pluginPath, [byte[]](1..64))
    $pluginSha = (Get-FileHash $pluginPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $runtimePath = Join-Path $runtimeDirectory 'TPPA.runtime-manifest.json'
    [ordered]@{
        schemaVersion=1
        packageId='NINA.Plugins.PolarAlignment'
        pluginVersion='2.2.6.103'
        sourceCommit=('a' * 40)
        artifacts=@([ordered]@{name='NINA.Plugins.PolarAlignment.dll';sha256=$pluginSha})
    } | ConvertTo-Json -Depth 5 | Set-Content $runtimePath -Encoding utf8NoBOM

    $lines = [Collections.Generic.List[string]]::new()
    for ($index = 0; $index -lt 59; $index++) {
        $day = if ($index -lt 30) { 4 } else { 5 }
        $started = [DateTimeOffset]::new(
            2026, 8, $day, 20, [int]($index / 30), $index % 30, [TimeSpan]::Zero)
        $duration = if ($index -eq 0) { $FirstDuration } else { 60.0 + ($index % 5) }
        $completed = $started.AddSeconds($duration)
        $direction = if ($index % 2 -eq 0) { 'IncreasingRA' } else { 'DecreasingRA' }
        $receiptId = [Guid]::NewGuid().ToString('D')
        $lines.Add(
            "$($completed.ToString('O'))|INFO| TPPA_COMPLETION_VERIFICATION_TIMING " +
            "schemaVersion=2; phase=return-field; receiptId=$receiptId; " +
            "startedUtc=$($started.ToString('O')); completedUtc=$($completed.ToString('O')); " +
            "slewDirection=$direction; effectiveSettleSeconds=30.000; " +
            "timingPath=fresh-three-point-plus-return-field; measurementOnly=true; " +
            "refractionAdjustmentEnabled=true; cadenceAuthorityConsumed=false; " +
            "upasMovementCount=0; threePointMilliseconds=50000.0; " +
            "returnFieldMilliseconds=$(($duration - 50.0) * 1000.0); " +
            "totalMilliseconds=$($duration * 1000.0)")
    }
    $logPath = Join-Path $Root 'nina.log'
    [IO.File]::WriteAllLines($logPath, $lines, [Text.UTF8Encoding]::new($false))
    $receiptDirectory = Join-Path $Root 'receipts'
    $producerArgs = @{
        NinaLogPath=$logPath
        RuntimeManifestPath=$runtimePath
        HardwareConfigurationId=('c' * 64)
        MechanicalStateId=('d' * 64)
        LoadProfileId='hae29c-ec-full-rig-v1'
        TemperatureC=35.0
        OutputDirectory=$receiptDirectory
        ExpectedReceiptCount=59
    }
    $production = & $producer @producerArgs
    return [pscustomobject]@{
        Paths=@($production.ReceiptPaths)
        LogPath=$logPath
        RuntimePath=$runtimePath
        Root=$Root
    }
}

Describe 'TPPA machine-produced fresh-determination timing evidence' {
    BeforeEach {
        $script:campaign = New-TimingCampaign (
            Join-Path $TestDrive ([Guid]::NewGuid().ToString('N')))
        $script:paths = @($campaign.Paths)
    }

    It 'qualifies 59 identity-consistent no-motion timings over two nights' {
        $result = & $analyzer -TimingReceiptPath $paths
        $result.TimingCampaignQualified | Should Be $true
        $result.ValidDeterminationCount | Should Be 59
        $result.ObservedMaximumSeconds | Should Be 64
        $result.ConservativeUpperBoundSeconds | Should Be 69
        $result.P95CoverageConfidence | Should BeGreaterThan 0.95
        $result.GrantsUpasMotionAuthority | Should Be $false
    }

    It 'fails closed when a timing receipt moved UPAS' {
        $receipt = Get-Content -LiteralPath $paths[0] -Raw | ConvertFrom-Json
        $receipt.UpasMovementCount = 1
        $receipt | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $paths[0] -Encoding utf8NoBOM
        $result = & $analyzer -TimingReceiptPath $paths
        $result.TimingCampaignQualified | Should Be $false
        $result.ExcludedSampleCount | Should Be 1
    }

    It 'rejects an identity mismatch' {
        $receipt = Get-Content -LiteralPath $paths[0] -Raw | ConvertFrom-Json
        $receipt.RuntimeManifestSha256 = ('9' * 64)
        $receipt | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $paths[0] -Encoding utf8NoBOM
        $result = & $analyzer -TimingReceiptPath $paths
        $result.TimingCampaignQualified | Should Be $false
        ($result.Issues -join ';') | Should Match 'mixes RuntimeManifestSha256'
    }

    It 'rejects a source log changed after receipt production' {
        [IO.File]::AppendAllText($campaign.LogPath, [Environment]::NewLine + 'tampered')
        $result = & $analyzer -TimingReceiptPath $paths
        $result.TimingCampaignQualified | Should Be $false
        $result.ExcludedSampleCount | Should Be 59
    }

    It 'rejects a reserve that exceeds 75 seconds' {
        $long = New-TimingCampaign (
            Join-Path $TestDrive ([Guid]::NewGuid().ToString('N'))) 71.0
        $result = & $analyzer -TimingReceiptPath $long.Paths
        $result.TimingCampaignQualified | Should Be $false
        ($result.Issues -join ';') | Should Match 'limit is 75'
    }

    It 'producer rejects a log that records UPAS movement' {
        $lines = [IO.File]::ReadAllLines($campaign.LogPath)
        $lines[0] = $lines[0].Replace('upasMovementCount=0', 'upasMovementCount=1')
        [IO.File]::WriteAllLines($campaign.LogPath, $lines, [Text.UTF8Encoding]::new($false))
        $producerArgs = @{
            NinaLogPath=$campaign.LogPath
            RuntimeManifestPath=$campaign.RuntimePath
            HardwareConfigurationId=('c' * 64)
            MechanicalStateId=('d' * 64)
            LoadProfileId='hae29c-ec-full-rig-v1'
            TemperatureC=35.0
            OutputDirectory=(Join-Path $campaign.Root 'movement-receipts')
            ExpectedReceiptCount=59
        }
        $threw = $false
        try { & $producer @producerArgs | Out-Null } catch { $threw = $true }
        $threw | Should Be $true
    }

    It 'producer rejects a fractional movement count' {
        $lines = [IO.File]::ReadAllLines($campaign.LogPath)
        $lines[0] = $lines[0].Replace('upasMovementCount=0', 'upasMovementCount=0.4')
        [IO.File]::WriteAllLines($campaign.LogPath, $lines, [Text.UTF8Encoding]::new($false))
        $producerArgs = @{
            NinaLogPath=$campaign.LogPath
            RuntimeManifestPath=$campaign.RuntimePath
            HardwareConfigurationId=('c' * 64)
            MechanicalStateId=('d' * 64)
            LoadProfileId='hae29c-ec-full-rig-v1'
            TemperatureC=35.0
            OutputDirectory=(Join-Path $campaign.Root 'fractional-receipts')
            ExpectedReceiptCount=59
        }
        $threw = $false
        try { & $producer @producerArgs | Out-Null } catch { $threw = $true }
        $threw | Should Be $true
    }
}
