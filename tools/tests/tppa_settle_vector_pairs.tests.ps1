Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$analyzer = Join-Path (Split-Path -Parent $PSScriptRoot) 'analyze_tppa_settle_vector_pairs.ps1'

function New-Nomination([string]$Path) {
    [ordered]@{
        SchemaVersion = 2
        Event = 'tppa-settle-cadence-nomination'
        RigConfigurationId = 'EDGEHD925-07-OAGL-ASI2600MM'
        RecommendedProbeSettleSeconds = 12.0
        CandidateCadenceQualified = $true
        ProductionSettleQualified = $false
        Runs = @(1..20 | ForEach-Object {
            [ordered]@{ RunId = "00000000-0000-0000-0000-$($_.ToString('000000000000'))" }
        })
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $Path -Encoding utf8
}

function Invoke-Analyzer([string[]]$Paths, [string]$Root) {
    $nomination = Join-Path $Root 'nomination.json'
    New-Nomination $nomination
    return & $analyzer -PairManifestPath $Paths -CandidateNominationReceiptPath $nomination
}

function New-Summary(
        [string]$Path,
        [int]$Index,
        [double]$SettleSeconds,
        [double]$AzimuthMinutes,
        [double]$AltitudeMinutes,
        [bool]$DiagnosticPassed = $true,
        [int]$NightOffset = 0) {
    $started = [DateTimeOffset]::Parse('2026-08-04T18:00:00Z').AddDays($NightOffset).AddMinutes($Index * 5)
    [ordered]@{
        SchemaVersion = 2
        RunId = [Guid]::NewGuid().ToString('D')
        StartedUtc = $started.ToString('O')
        CompletedUtc = $started.AddSeconds(120).ToString('O')
        ElapsedSeconds = 120.0
        DiagnosticPassed = $DiagnosticPassed
        RepeatabilityPassed = $DiagnosticPassed
        ReciprocityPassed = $DiagnosticPassed
        ExpectedSampleCount = 9
        ComponentTotalsConsistent = $true
        RefractionAdjustmentEnabled = $true
        OverdeterminedShadowModelCheck = $false
        EffectivePointSettleSeconds = $SettleSeconds
        InitialAzimuthMinutes = $AzimuthMinutes
        InitialAltitudeMinutes = $AltitudeMinutes
        InitialTotalMinutes = [Math]::Sqrt($AzimuthMinutes * $AzimuthMinutes + $AltitudeMinutes * $AltitudeMinutes)
        ReciprocalAzimuthMinutes = $AzimuthMinutes + 0.05
        ReciprocalAltitudeMinutes = $AltitudeMinutes - 0.05
        ReciprocalTotalMinutes = [Math]::Sqrt(($AzimuthMinutes + 0.05) * ($AzimuthMinutes + 0.05) + ($AltitudeMinutes - 0.05) * ($AltitudeMinutes - 0.05))
        RepeatedForwardAzimuthMinutes = $AzimuthMinutes + 0.1
        RepeatedForwardAltitudeMinutes = $AltitudeMinutes - 0.1
        RepeatedForwardTotalMinutes = [Math]::Sqrt(($AzimuthMinutes + 0.1) * ($AzimuthMinutes + 0.1) + ($AltitudeMinutes - 0.1) * ($AltitudeMinutes - 0.1))
        RepeatabilityVectorSeparationMinutes = 0.14
        ReciprocityVectorSeparationMinutes = 0.07
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

function New-PairCampaign(
        [string]$Root,
        [double]$CandidateDeltaAzimuth = 0.1,
        [int]$BadPair = 0,
        [switch]$FailedDiagnostic,
        [switch]$OneDirection,
        [switch]$OneOrder) {
    $paths = @()
    for ($index = 1; $index -le 20; $index++) {
        $candidate = Join-Path $Root "candidate-$index.json"
        $reference = Join-Path $Root "reference-$index.json"
        $manifest = Join-Path $Root "pair-$index.json"
        $delta = if ($index -eq $BadPair) { 0.8 } else { $CandidateDeltaAzimuth }
        $nightOffset = if ($index -le 10) { 0 } else { 1 }
        $candidateIndex = if ($OneOrder -or ($index % 2) -eq 0) { $index * 2 } else { $index * 2 + 1 }
        $referenceIndex = if ($OneOrder -or ($index % 2) -eq 0) { $index * 2 + 1 } else { $index * 2 }
        New-Summary $candidate $candidateIndex 12.0 (5.0 + $delta) -4.0 -DiagnosticPassed:(-not ($FailedDiagnostic -and $index -eq 3)) -NightOffset $nightOffset
        New-Summary $reference $referenceIndex 30.0 5.0 -4.0 -NightOffset $nightOffset
        [ordered]@{
            SchemaVersion = 1
            Event = 'tppa-settle-vector-pair'
            PairId = "pair-$index"
            RigConfigurationId = 'EDGEHD925-07-OAGL-ASI2600MM'
            DubaiNight = if ($index -le 10) { '2026-08-04' } else { '2026-08-05' }
            SlewDirection = if ($OneDirection -or ($index % 2) -eq 0) { 'IncreasingRA' } else { 'DecreasingRA' }
            Order = if ($OneOrder -or ($index % 2) -eq 0) { 'CandidateThenReference' } else { 'ReferenceThenCandidate' }
            SameMechanicalEpoch = $true
            SamePointingArc = $true
            NoPhysicalAdjustmentBetweenRuns = $true
            CandidateSummaryPath = [IO.Path]::GetFileName($candidate)
            ReferenceSummaryPath = [IO.Path]::GetFileName($reference)
        } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifest -Encoding utf8
        $paths += $manifest
    }
    return $paths
}

Describe 'TPPA candidate versus reference settle-vector campaign' {
    It 'qualifies twenty balanced pairs whose vectors all agree within half an arcminute' {
        $paths = New-PairCampaign $TestDrive
        $result = Invoke-Analyzer $paths $TestDrive

        $result.SchemaVersion | Should Be 2
        $result.Event | Should Be 'tppa-settle-vector-pair-campaign'
        $result.CandidateVectorPairQualified | Should Be $true
        $result.RecommendedPairedSettleSeconds | Should Be 12
        $result.ProductionSettleQualified | Should Be $false
        $result.RecommendedProductionSettleSeconds | Should Be $null
        $result.NextRequiredGate | Should Match 'null.*dataset'
        $result.ValidPairCount | Should Be 20
        $result.DubaiNightCount | Should Be 2
        $result.GrantsUpasMotionAuthority | Should Be $false
        $result.ScopeNote | Should Match 'does not qualify the production settle'
    }

    It 'fails the whole campaign when one pair exceeds the vector threshold' {
        $paths = New-PairCampaign $TestDrive -BadPair 7
        $result = Invoke-Analyzer $paths $TestDrive

        $result.CandidateVectorPairQualified | Should Be $false
        $result.RecommendedProductionSettleSeconds | Should Be $null
        @($result.Pairs | Where-Object { ($_.Issues -join ' ') -match 'separation' }).Count | Should Be 1
    }

    It 'rejects a candidate summary whose internal diagnostics failed' {
        $paths = New-PairCampaign $TestDrive -FailedDiagnostic
        $result = Invoke-Analyzer $paths $TestDrive

        $result.CandidateVectorPairQualified | Should Be $false
        @($result.Pairs | Where-Object { ($_.Issues -join ' ') -match 'diagnostics did not all pass' }).Count | Should Be 1
    }

    It 'rejects anti-correlated legs even when their midpoint agrees' {
        $paths = New-PairCampaign $TestDrive
        $candidate = Get-Content -LiteralPath (Join-Path $TestDrive 'candidate-6.json') -Raw | ConvertFrom-Json
        $candidate.InitialAzimuthMinutes = 5.8
        $candidate.RepeatedForwardAzimuthMinutes = 4.3
        $candidate.InitialTotalMinutes = [Math]::Sqrt(5.8 * 5.8 + $candidate.InitialAltitudeMinutes * $candidate.InitialAltitudeMinutes)
        $candidate.RepeatedForwardTotalMinutes = [Math]::Sqrt(4.3 * 4.3 + $candidate.RepeatedForwardAltitudeMinutes * $candidate.RepeatedForwardAltitudeMinutes)
        $candidate | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $TestDrive 'candidate-6.json') -Encoding utf8
        $result = Invoke-Analyzer $paths $TestDrive

        $result.CandidateVectorPairQualified | Should Be $false
        @($result.Pairs | Where-Object { ($_.Issues -join ' ') -match 'Determination-leg separation' }).Count | Should Be 1
    }

    It 'requires both slew directions' {
        $paths = New-PairCampaign $TestDrive -OneDirection
        $result = Invoke-Analyzer $paths $TestDrive

        $result.CandidateVectorPairQualified | Should Be $false
        ($result.Issues -join ' ') | Should Match 'Only 0 DecreasingRA'
    }

    It 'requires both candidate-reference execution orders' {
        $paths = New-PairCampaign $TestDrive -OneOrder
        $result = Invoke-Analyzer $paths $TestDrive

        $result.CandidateVectorPairQualified | Should Be $false
        ($result.Issues -join ' ') | Should Match 'Only 0 ReferenceThenCandidate'
    }

    It 'rejects physical adjustment between paired measurements' {
        $paths = New-PairCampaign $TestDrive
        $manifest = Get-Content -LiteralPath $paths[4] -Raw | ConvertFrom-Json
        $manifest.NoPhysicalAdjustmentBetweenRuns = $false
        $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $paths[4] -Encoding utf8
        $result = Invoke-Analyzer $paths $TestDrive

        $result.CandidateVectorPairQualified | Should Be $false
        @($result.Pairs | Where-Object { ($_.Issues -join ' ') -match 'Physical adjustment' }).Count | Should Be 1
    }

    It 'rejects reuse of a nomination run in the paired campaign' {
        $paths = New-PairCampaign $TestDrive
        $candidatePath = Join-Path $TestDrive 'candidate-1.json'
        $candidate = Get-Content -LiteralPath $candidatePath -Raw | ConvertFrom-Json
        $candidate.RunId = '00000000-0000-0000-0000-000000000001'
        $candidate | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $candidatePath -Encoding utf8
        $result = Invoke-Analyzer $paths $TestDrive

        $result.CandidateVectorPairQualified | Should Be $false
        ($result.Issues -join ' ') | Should Match 'reused'
    }

    It 'hard-fails a schema-v1 summary instead of defaulting missing vectors to zero' {
        $paths = New-PairCampaign $TestDrive
        $candidatePath = Join-Path $TestDrive 'candidate-2.json'
        $candidate = Get-Content -LiteralPath $candidatePath -Raw | ConvertFrom-Json
        $candidate.SchemaVersion = 1
        $candidate | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $candidatePath -Encoding utf8
        $result = Invoke-Analyzer $paths $TestDrive

        $result.CandidateVectorPairQualified | Should Be $false
        @($result.Pairs | Where-Object { ($_.Issues -join ' ') -match 'schema is not 2' }).Count | Should Be 1
    }

    It 'rejects a manifest whose declared order contradicts receipt timestamps' {
        $paths = New-PairCampaign $TestDrive
        $manifest = Get-Content -LiteralPath $paths[1] -Raw | ConvertFrom-Json
        $manifest.Order = 'ReferenceThenCandidate'
        $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $paths[1] -Encoding utf8
        $result = Invoke-Analyzer $paths $TestDrive

        $result.CandidateVectorPairQualified | Should Be $false
        @($result.Pairs | Where-Object { ($_.Issues -join ' ') -match 'contradict' }).Count | Should Be 1
    }

    It 'ingests the camel-case JSON emitted by the C# receipt serializer' {
        $paths = New-PairCampaign $TestDrive
        Convert-SummaryToCamelCase (Join-Path $TestDrive 'candidate-2.json')
        $result = Invoke-Analyzer $paths $TestDrive

        $result.CandidateVectorPairQualified | Should Be $true
    }

    It 'rejects candidate-reference method mismatch' {
        $paths = New-PairCampaign $TestDrive
        $summaryPath = Join-Path $TestDrive 'candidate-4.json'
        $summary = Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json
        $summary.ExpectedSampleCount = 11
        $summary.OverdeterminedShadowModelCheck = $true
        $summary | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $summaryPath -Encoding utf8
        $result = Invoke-Analyzer $paths $TestDrive

        $result.CandidateVectorPairQualified | Should Be $false
        @($result.Pairs | Where-Object { ($_.Issues -join ' ') -match 'measurement methods differ' }).Count | Should Be 1
    }

    It 'rejects a Dubai night label that does not match receipt timestamps' {
        $paths = New-PairCampaign $TestDrive
        $manifest = Get-Content -LiteralPath $paths[2] -Raw | ConvertFrom-Json
        $manifest.DubaiNight = '2026-08-09'
        $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $paths[2] -Encoding utf8
        $result = Invoke-Analyzer $paths $TestDrive

        $result.CandidateVectorPairQualified | Should Be $false
        @($result.Pairs | Where-Object { ($_.Issues -join ' ') -match 'does not match' }).Count | Should Be 1
    }
}
