Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$toolsRoot = Split-Path -Parent $PSScriptRoot
$analyzer = Join-Path $toolsRoot 'analyze_tppa_settle_qualification.ps1'
$runner = Join-Path $toolsRoot 'run_guarded_tppa_settle_probe.ps1'

function New-SettleReceipt(
    [string]$Path,
    [int]$RunNumber,
    [string]$Direction,
    [double]$UnstableUntilSeconds = 0.0,
    [string]$RigConfigurationId = 'GT81IV-08-OAGL-ASI2600MM',
    [int]$NightOffset = 0,
    [switch]$UnsafeSample,
    [switch]$MixedRig,
    [switch]$StringTracking,
    [switch]$SparseSamples) {
    $slewCompleted = [DateTimeOffset]::Parse('2026-08-03T18:00:00Z').AddDays($NightOffset).AddMinutes($RunNumber)
    $spacingSeconds = if ($SparseSamples) { 12.0 } else { 5.0 }
    $samples = for ($index = 0; $index -lt 13; $index++) {
        $elapsed = 4.0 + $spacingSeconds * $index
        $offsetArcsec = if ($elapsed -le $UnstableUntilSeconds) { 3.0 } else { 0.2 * [Math]::Sin($index) }
        [ordered]@{
            Index = $index + 1
            ScheduledUtc = $slewCompleted.AddSeconds(5.0 * $index).ToString('O')
            CaptureStartedUtc = $slewCompleted.AddSeconds($elapsed - 2.0).ToString('O')
            CaptureCompletedUtc = $slewCompleted.AddSeconds($elapsed).ToString('O')
            ElapsedFromSlewCompletedSeconds = $elapsed
            SolveRaDegreesJ2000 = 120.0 + $offsetArcsec / (3600.0 * [Math]::Cos(30.0 * [Math]::PI / 180.0))
            SolveDecDegreesJ2000 = 30.0
            PixelScaleArcsec = 1.0
            PositionAngleDegrees = 12.0
            Flipped = $false
            MountAzimuthDegrees = if ($UnsafeSample -and $index -eq 3) { 20.0 } else { 355.0 }
            MountAltitudeDegrees = 45.0
            MountSlewing = $false
            MountTracking = if ($StringTracking -and $index -eq 3) { 'false' } else { $true }
        }
    }
    $rig = if ($MixedRig) { 'EDGEHD925-07-OAGL-ASI2600MM' } else { $RigConfigurationId }
    [ordered]@{
        SchemaVersion = 1
        Event = 'tppa-settle-probe'
        RunId = "run-$RunNumber-$Direction"
        RigConfigurationId = $rig
        StartedUtc = $slewCompleted.AddSeconds(-20).ToString('O')
        SlewCompletedUtc = $slewCompleted.ToString('O')
        CompletedUtc = $slewCompleted.AddSeconds(65).ToString('O')
        TargetAzimuthDegrees = 355.0
        TargetAltitudeDegrees = 45.0
        SlewDistanceDegrees = 8.0
        RaDeltaDegrees = if ($Direction -eq 'IncreasingRA') { 8.0 } else { -8.0 }
        SlewDirection = $Direction
        ExposureSeconds = 1.0
        CadenceSeconds = 5.0
        PreMount = [ordered]@{
            RightAscension = if ($Direction -eq 'IncreasingRA') { 8.0 } else { 8.533333333333333 }
            Declination = 0.0
        }
        PostMount = [ordered]@{
            RightAscension = if ($Direction -eq 'IncreasingRA') { 8.533333333333333 } else { 8.0 }
            Declination = 0.0
        }
        Samples = $samples
    } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $Path -Encoding utf8
}

Describe 'TPPA settle qualification campaign' {
    It 'keeps the runner motion bounded and separate from UPAS' {
        $text = [IO.File]::ReadAllText($runner)
        $text.Contains('guarded_balcony_drift_slew.ps1') | Should Be $true
        $text.Contains('[ValidateRange(25.0, 55.0)]') | Should Be $true
        $text.Contains('($_ -ge 270.0 -and $_ -le 360.0)') | Should Be $true
        $text.Contains('/equipment/mount/slew/stop') | Should Be $true
        $text.Contains('one-degree guard band') | Should Be $true
        $text.ToLowerInvariant().Contains('upas') | Should Be $false
    }

    It 'qualifies twenty bidirectional runs over two Dubai nights' {
        $paths = @()
        for ($index = 1; $index -le 20; $index++) {
            $path = Join-Path $TestDrive "valid-$index.json"
            $direction = if (($index % 2) -eq 0) { 'IncreasingRA' } else { 'DecreasingRA' }
            New-SettleReceipt $path $index $direction -NightOffset ([Math]::Floor(($index - 1) / 10))
            $paths += $path
        }
        $result = & $analyzer -ReceiptPath $paths
        $result.SchemaVersion | Should Be 2
        $result.Event | Should Be 'tppa-settle-cadence-nomination'
        $result.CandidateCadenceQualified | Should Be $true
        $result.ValidRunCount | Should Be 20
        $result.DubaiNightCount | Should Be 2
        $result.RecommendedProbeSettleSeconds | Should Be 12
        $result.ProductionSettleQualified | Should Be $false
        $result.NextRequiredGate | Should Match '20 controlled transitions'
        $result.ScopeNote.Contains('does not qualify the production TPPA settle constant') | Should Be $true
    }

    It 'rejects duplicate run identifiers as non-independent evidence' {
        $paths = @()
        for ($index = 1; $index -le 20; $index++) {
            $path = Join-Path $TestDrive "duplicate-$index.json"
            $direction = if (($index % 2) -eq 0) { 'IncreasingRA' } else { 'DecreasingRA' }
            New-SettleReceipt $path $index $direction -NightOffset ([Math]::Floor(($index - 1) / 10))
            $paths += $path
        }
        $duplicate = Get-Content -LiteralPath $paths[0] -Raw | ConvertFrom-Json
        $second = Get-Content -LiteralPath $paths[1] -Raw | ConvertFrom-Json
        $second.RunId = $duplicate.RunId
        $second | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $paths[1] -Encoding utf8
        $result = & $analyzer -ReceiptPath $paths
        $result.CandidateCadenceQualified | Should Be $false
        ($result.Issues -join ' ') | Should Match 'Duplicate RunId'
    }

    It 'fails when only one slew direction is represented' {
        $paths = @()
        for ($index = 1; $index -le 20; $index++) {
            $path = Join-Path $TestDrive "one-way-$index.json"
            New-SettleReceipt $path $index 'IncreasingRA' -NightOffset ([Math]::Floor(($index - 1) / 10))
            $paths += $path
        }
        $result = & $analyzer -ReceiptPath $paths
        $result.CandidateCadenceQualified | Should Be $false
        ($result.Issues -join ' ') | Should Match 'Only 0 DecreasingRA'
    }

    It 'fails on mixed rig configurations' {
        $paths = @()
        for ($index = 1; $index -le 20; $index++) {
            $path = Join-Path $TestDrive "mixed-$index.json"
            $direction = if (($index % 2) -eq 0) { 'IncreasingRA' } else { 'DecreasingRA' }
            New-SettleReceipt $path $index $direction -NightOffset ([Math]::Floor(($index - 1) / 10)) -MixedRig:($index -eq 10)
            $paths += $path
        }
        $result = & $analyzer -ReceiptPath $paths
        $result.CandidateCadenceQualified | Should Be $false
        ($result.Issues -join ' ') | Should Match 'exactly one rig configuration'
    }

    It 'fails closed on a balcony-envelope violation' {
        $paths = @()
        for ($index = 1; $index -le 20; $index++) {
            $path = Join-Path $TestDrive "unsafe-$index.json"
            $direction = if (($index % 2) -eq 0) { 'IncreasingRA' } else { 'DecreasingRA' }
            New-SettleReceipt $path $index $direction -NightOffset ([Math]::Floor(($index - 1) / 10)) -UnsafeSample:($index -eq 3)
            $paths += $path
        }
        $result = & $analyzer -ReceiptPath $paths
        $result.CandidateCadenceQualified | Should Be $false
        @($result.Runs | Where-Object { ($_.Issues -join ' ') -match 'outside the balcony' }).Count | Should Be 1
    }

    It 'rejects a string masquerading as a tracking boolean' {
        $paths = @()
        for ($index = 1; $index -le 20; $index++) {
            $path = Join-Path $TestDrive "string-bool-$index.json"
            $direction = if (($index % 2) -eq 0) { 'IncreasingRA' } else { 'DecreasingRA' }
            New-SettleReceipt $path $index $direction -NightOffset ([Math]::Floor(($index - 1) / 10)) -StringTracking:($index -eq 3)
            $paths += $path
        }
        $result = & $analyzer -ReceiptPath $paths
        $result.CandidateCadenceQualified | Should Be $false
        @($result.Runs | Where-Object { ($_.Issues -join ' ') -match 'not a JSON boolean' }).Count | Should Be 1
    }

    It 'rejects a sparse solve series that cannot resolve settling cadence' {
        $paths = @()
        for ($index = 1; $index -le 20; $index++) {
            $path = Join-Path $TestDrive "sparse-$index.json"
            $direction = if (($index % 2) -eq 0) { 'IncreasingRA' } else { 'DecreasingRA' }
            New-SettleReceipt $path $index $direction -NightOffset ([Math]::Floor(($index - 1) / 10)) -SparseSamples:($index -eq 3)
            $paths += $path
        }
        $result = & $analyzer -ReceiptPath $paths
        $result.CandidateCadenceQualified | Should Be $false
        @($result.Runs | Where-Object { ($_.Issues -join ' ') -match 'Sample gap' }).Count | Should Be 1
    }

    It 'rejects a campaign whose conservative recommendation exceeds thirty seconds' {
        $paths = @()
        for ($index = 1; $index -le 20; $index++) {
            $path = Join-Path $TestDrive "slow-$index.json"
            $direction = if (($index % 2) -eq 0) { 'IncreasingRA' } else { 'DecreasingRA' }
            New-SettleReceipt $path $index $direction -NightOffset ([Math]::Floor(($index - 1) / 10)) -UnstableUntilSeconds 14.0
            $paths += $path
        }
        $result = & $analyzer -ReceiptPath $paths
        $result.CandidateCadenceQualified | Should Be $false
        $result.CandidateSettleSeconds | Should BeGreaterThan 30
        $result.RecommendedProbeSettleSeconds | Should Be $null
        ($result.Issues -join ' ') | Should Match 'exceeds qualified ceiling'
    }
}
