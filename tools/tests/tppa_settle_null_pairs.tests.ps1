Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$analyzer = Join-Path (Split-Path -Parent $PSScriptRoot) 'analyze_tppa_settle_null_pairs.ps1'

function New-Summary([string]$Path, [int]$Index, [double]$Azimuth, [double]$Altitude, [int]$NightOffset) {
    $started = [DateTimeOffset]::Parse('2026-08-06T18:00:00Z').AddDays($NightOffset).AddMinutes($Index * 5)
    [ordered]@{
        SchemaVersion = 2; RunId = [Guid]::NewGuid().ToString('D')
        StartedUtc = $started.ToString('O'); CompletedUtc = $started.AddSeconds(120).ToString('O')
        ElapsedSeconds = 120.0; DiagnosticPassed = $true; RepeatabilityPassed = $true; ReciprocityPassed = $true
        ExpectedSampleCount = 9; ComponentTotalsConsistent = $true; RefractionAdjustmentEnabled = $true; OverdeterminedShadowModelCheck = $false
        EffectivePointSettleSeconds = 30.0
        InitialAzimuthMinutes = $Azimuth; InitialAltitudeMinutes = $Altitude
        InitialTotalMinutes = [Math]::Sqrt($Azimuth * $Azimuth + $Altitude * $Altitude)
        ReciprocalAzimuthMinutes = $Azimuth; ReciprocalAltitudeMinutes = $Altitude
        ReciprocalTotalMinutes = [Math]::Sqrt($Azimuth * $Azimuth + $Altitude * $Altitude)
        RepeatedForwardAzimuthMinutes = $Azimuth + 0.1; RepeatedForwardAltitudeMinutes = $Altitude - 0.1
        RepeatedForwardTotalMinutes = [Math]::Sqrt(($Azimuth + 0.1) * ($Azimuth + 0.1) + ($Altitude - 0.1) * ($Altitude - 0.1))
        RepeatabilityVectorSeparationMinutes = 0.14; ReciprocityVectorSeparationMinutes = 0.1
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $Path -Encoding utf8
}


function Convert-SummaryToCamelCase([string]$Path) {
    $summary = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    $camel = [ordered]@{}
    foreach ($property in $summary.PSObject.Properties) {
        $name = $property.Name.Substring(0, 1).ToLowerInvariant() + $property.Name.Substring(1)
        $camel[$name] = $property.Value
    }
    $camel | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $Path -Encoding utf8
}

function New-NullCampaign([string]$Root, [int]$BadPair = 0, [switch]$OneDirection) {
    $paths = @()
    for ($index = 1; $index -le 10; $index++) {
        $nightOffset = if ($index -le 5) { 0 } else { 1 }
        $first = Join-Path $Root "null-first-$index.json"
        $second = Join-Path $Root "null-second-$index.json"
        $manifest = Join-Path $Root "null-pair-$index.json"
        $delta = if ($index -eq $BadPair) { 0.8 } else { 0.1 }
        New-Summary $first ($index * 2) 5.0 -3.0 $nightOffset
        New-Summary $second ($index * 2 + 1) (5.0 + $delta) -3.0 $nightOffset
        [ordered]@{
            SchemaVersion = 1; Event = 'tppa-settle-null-pair'; PairId = "null-$index"
            RigConfigurationId = 'GT81IV-08-OAGL-ASI2600MM'
            DubaiNight = if ($nightOffset -eq 0) { '2026-08-06' } else { '2026-08-07' }
            SlewDirection = if ($OneDirection -or ($index % 2) -eq 0) { 'IncreasingRA' } else { 'DecreasingRA' }
            SameMechanicalEpoch = $true; SamePointingArc = $true; NoPhysicalAdjustmentBetweenRuns = $true
            FirstSummaryPath = [IO.Path]::GetFileName($first); SecondSummaryPath = [IO.Path]::GetFileName($second)
        } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifest -Encoding utf8
        $paths += $manifest
    }
    return $paths
}

Describe 'TPPA independent 30-second null-arm qualification' {
    It 'qualifies ten independent null pairs over two nights without promoting production settle' {
        $paths = New-NullCampaign $TestDrive
        $result = & $analyzer -NullPairManifestPath $paths

        $result.SchemaVersion | Should Be 2
        $result.Event | Should Be 'tppa-settle-null-dataset'
        $result.NullArmDatasetQualified | Should Be $true
        $result.ProductionSettleQualified | Should Be $false
        $result.RecommendedProductionSettleSeconds | Should Be $null
        $result.ValidNullPairCount | Should Be 10
        $result.ObservedMaximumNullLegSeparationMinutes | Should BeGreaterThan 0
        $result.ObservedP95NullMidpointSeparationMinutes | Should BeGreaterThan 0
        $result.GrantsUpasMotionAuthority | Should Be $false
    }

    It 'retains structurally valid threshold exceedances in uncensored observations' {
        $paths = New-NullCampaign $TestDrive -BadPair 4
        $result = & $analyzer -NullPairManifestPath $paths

        $result.NullArmDatasetQualified | Should Be $true
        $result.ObservedMaximumNullLegSeparationMinutes | Should BeGreaterThan 0.5
        @($result.NullPairs | Where-Object ExceedsConfiguredThreshold).Count | Should Be 1
    }

    It 'computes leg and midpoint distributions from separate populations' {
        $paths = New-NullCampaign $TestDrive
        $summaryPath = Join-Path $TestDrive 'null-second-4.json'
        $summary = Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json
        $summary.ReciprocalAzimuthMinutes = 5.8
        $summary.ReciprocalTotalMinutes = [Math]::Sqrt(5.8 * 5.8 + 3.0 * 3.0)
        $summary | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $summaryPath -Encoding utf8
        $result = & $analyzer -NullPairManifestPath $paths

        $result.NullArmDatasetQualified | Should Be $true
        $result.ObservedP95NullLegSeparationMinutes | Should BeGreaterThan 0.7
        $result.ObservedP95NullMidpointSeparationMinutes | Should BeLessThan 0.2
    }

    It 'emits a uniform null-pair shape and immutable no-authority fields on failure' {
        $goodPaths = New-NullCampaign $TestDrive
        $good = & $analyzer -NullPairManifestPath $goodPaths
        $badPaths = New-NullCampaign $TestDrive
        $summaryPath = Join-Path $TestDrive 'null-second-1.json'
        $summary = Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json
        $summary.ComponentTotalsConsistent = $false
        $summary | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $summaryPath -Encoding utf8
        $bad = & $analyzer -NullPairManifestPath $badPaths
        $invalidPair = @($bad.NullPairs | Where-Object { -not $_.Valid })[0]

        (@($invalidPair.PSObject.Properties.Name) -join ',') | Should Be (@($good.NullPairs[0].PSObject.Properties.Name) -join ',')
        $bad.ProductionSettleQualified | Should Be $false
        $bad.RecommendedProductionSettleSeconds | Should Be $null
        $bad.GrantsUpasMotionAuthority | Should Be $false
    }

    It 'requires both slew directions in the null arm' {
        $paths = New-NullCampaign $TestDrive -OneDirection
        $result = & $analyzer -NullPairManifestPath $paths

        $result.NullArmDatasetQualified | Should Be $false
        ($result.Issues -join ' ') | Should Match 'Only 0 DecreasingRA'
    }

    It 'ingests the camel-case JSON emitted by the C# receipt serializer' {
        $paths = New-NullCampaign $TestDrive
        Convert-SummaryToCamelCase (Join-Path $TestDrive 'null-first-1.json')
        $result = & $analyzer -NullPairManifestPath $paths

        $result.NullArmDatasetQualified | Should Be $true
    }

    It 'rejects a non-reference settle in either null arm' {
        foreach ($name in @('null-first-2.json', 'null-second-2.json')) {
            $paths = New-NullCampaign $TestDrive
            $summaryPath = Join-Path $TestDrive $name
            $summary = Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json
            $summary.EffectivePointSettleSeconds = 12.0
            $summary | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $summaryPath -Encoding utf8
            $result = & $analyzer -NullPairManifestPath $paths

            $result.NullArmDatasetQualified | Should Be $false
            (@($result.NullPairs | ForEach-Object Issues) -join ' ') | Should Match 'is not 30'
        }
    }

    It 'rejects cross-pair method mixing within the null campaign' {
        $paths = New-NullCampaign $TestDrive
        foreach ($name in @('null-first-2.json', 'null-second-2.json')) {
            $summaryPath = Join-Path $TestDrive $name
            $summary = Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json
            $summary.ExpectedSampleCount = 11
            $summary.OverdeterminedShadowModelCheck = $true
            $summary | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $summaryPath -Encoding utf8
        }
        $result = & $analyzer -NullPairManifestPath $paths

        $result.NullArmDatasetQualified | Should Be $false
        ($result.Issues -join ' ') | Should Match 'mixes TPPA measurement methods'
    }

    It 'rejects first-second method mismatch' {
        $paths = New-NullCampaign $TestDrive
        $summaryPath = Join-Path $TestDrive 'null-second-2.json'
        $summary = Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json
        $summary.ExpectedSampleCount = 11
        $summary.OverdeterminedShadowModelCheck = $true
        $summary | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $summaryPath -Encoding utf8
        $result = & $analyzer -NullPairManifestPath $paths

        $result.NullArmDatasetQualified | Should Be $false
        (@($result.NullPairs | ForEach-Object Issues) -join ' ') | Should Match 'measurement methods differ'
    }

    It 'rejects reuse of a TPPA run within the null arm' {
        $paths = New-NullCampaign $TestDrive
        $summaryPath = Join-Path $TestDrive 'null-first-1.json'
        $summary = Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json
        $other = Get-Content -LiteralPath (Join-Path $TestDrive 'null-second-1.json') -Raw | ConvertFrom-Json
        $summary.RunId = $other.RunId
        $summary | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $summaryPath -Encoding utf8
        $result = & $analyzer -NullPairManifestPath $paths

        $result.NullArmDatasetQualified | Should Be $false
        (@($result.NullPairs | ForEach-Object Issues) -join ' ') | Should Match 'identical'
    }
}
