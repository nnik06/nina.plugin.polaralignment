#requires -Version 7.5
param(
    [Parameter(Mandatory = $true)]
    [string[]]$PairManifestPath,
    [Parameter(Mandatory = $true)]
    [string]$CandidateNominationReceiptPath,
    [ValidateRange(2, 100)]
    [int]$MinimumPairs = 20,
    [ValidateRange(1, 20)]
    [int]$MinimumDubaiNights = 2,
    [ValidateRange(1, 50)]
    [int]$MinimumPairsPerDirection = 8,
    [ValidateRange(1, 50)]
    [int]$MinimumPairsPerOrder = 8,
    [ValidateRange(1, 50)]
    [int]$MinimumPairsPerDubaiNight = 8,
    [ValidateRange(0.05, 5.0)]
    [double]$MaximumVectorSeparationMinutes = 0.5,
    [ValidateRange(5.0, 29.999)]
    [double]$MaximumCandidateSettleSeconds = 29.0,
    [ValidateRange(29.0, 31.0)]
    [double]$ReferenceSettleSeconds = 30.0,
    [ValidateRange(0.001, 1.0)]
    [double]$SettleToleranceSeconds = 0.01,
    [ValidateRange(1.0, 60.0)]
    [double]$MaximumInterRunGapMinutes = 15.0,
    [ValidateRange(0.5, 1.0)]
    [double]$MaximumNightFraction = 0.70
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

function Get-PropertyValue([object]$Object, [string]$Name) {
    if ($null -eq $Object) { throw "Cannot read '$Name' from null." }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { throw "Missing field '$Name'." }
    return $property.Value
}

function ConvertTo-FiniteDouble([object]$Value, [string]$FieldName) {
    if ($Value -is [string] -or $Value -is [bool] -or $Value -isnot [ValueType]) {
        throw "Field '$FieldName' is not a JSON number."
    }
    $number = [double]$Value
    if (-not [double]::IsFinite($number)) { throw "Field '$FieldName' is not finite." }
    return $number
}

function ConvertTo-StrictInt([object]$Value, [string]$FieldName) {
    $number = ConvertTo-FiniteDouble $Value $FieldName
    if ([Math]::Truncate($number) -ne $number) { throw "Field '$FieldName' is not a JSON integer." }
    return [int]$number
}

function ConvertTo-StrictBoolean([object]$Value, [string]$FieldName) {
    if ($Value -isnot [bool]) { throw "Field '$FieldName' is not a JSON boolean." }
    return [bool]$Value
}

function ConvertTo-ObservedTime([object]$Value, [string]$FieldName) {
    if ($Value -isnot [string] -or $Value -notmatch '(Z|[+-]\d{2}:\d{2})$') {
        throw "Field '$FieldName' is not an offset-bearing timestamp string."
    }
    return [DateTimeOffset]::Parse(
        $Value,
        [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::RoundtripKind)
}

function Read-Json([string]$Path) {
    return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json -DateKind String
}

function Get-DubaiObservingNight([DateTimeOffset]$Timestamp) {
    return $Timestamp.ToOffset([TimeSpan]::FromHours(4)).AddHours(-12).Date.ToString('yyyy-MM-dd')
}

function Resolve-ManifestPath([string]$ManifestPath, [object]$Value, [string]$FieldName) {
    if ($Value -isnot [string] -or [string]::IsNullOrWhiteSpace($Value)) {
        throw "Field '$FieldName' is not a path string."
    }
    if ([IO.Path]::IsPathRooted($Value)) { return [IO.Path]::GetFullPath($Value) }
    return [IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $ManifestPath) $Value))
}

function Read-Summary([string]$Path, [string]$Role) {
    $summary = Read-Json $Path
    if ((ConvertTo-StrictInt (Get-PropertyValue $summary 'SchemaVersion') "$Role.SchemaVersion") -ne 2) {
        throw "$Role summary schema is not 2."
    }
    $runId = [string](Get-PropertyValue $summary 'RunId')
    [Guid]$parsedRunId = [Guid]::Empty
    if (-not [Guid]::TryParse($runId, [ref]$parsedRunId) -or $parsedRunId -eq [Guid]::Empty) {
        throw "$Role RunId is invalid."
    }
    $started = ConvertTo-ObservedTime (Get-PropertyValue $summary 'StartedUtc') "$Role.StartedUtc"
    $completed = ConvertTo-ObservedTime (Get-PropertyValue $summary 'CompletedUtc') "$Role.CompletedUtc"
    if ($completed -le $started) { throw "$Role lifecycle is not increasing." }
    $diagnosticPassed = ConvertTo-StrictBoolean (Get-PropertyValue $summary 'DiagnosticPassed') "$Role.DiagnosticPassed"
    $repeatabilityPassed = ConvertTo-StrictBoolean (Get-PropertyValue $summary 'RepeatabilityPassed') "$Role.RepeatabilityPassed"
    $reciprocityPassed = ConvertTo-StrictBoolean (Get-PropertyValue $summary 'ReciprocityPassed') "$Role.ReciprocityPassed"
    $refraction = ConvertTo-StrictBoolean (Get-PropertyValue $summary 'RefractionAdjustmentEnabled') "$Role.RefractionAdjustmentEnabled"
    if (-not $diagnosticPassed -or -not $repeatabilityPassed -or -not $reciprocityPassed) {
        throw "$Role verification diagnostics did not all pass."
    }
    if (-not $refraction) { throw "$Role verification did not target the true pole." }
    if (-not (ConvertTo-StrictBoolean (Get-PropertyValue $summary 'ComponentTotalsConsistent') "$Role.ComponentTotalsConsistent")) {
        throw "$Role component totals are inconsistent."
    }
    $expectedSampleCount = ConvertTo-StrictInt (Get-PropertyValue $summary 'ExpectedSampleCount') "$Role.ExpectedSampleCount"
    $shadowModel = ConvertTo-StrictBoolean (Get-PropertyValue $summary 'OverdeterminedShadowModelCheck') "$Role.OverdeterminedShadowModelCheck"
    $settle = ConvertTo-FiniteDouble (Get-PropertyValue $summary 'EffectivePointSettleSeconds') "$Role.EffectivePointSettleSeconds"
    $initialAz = ConvertTo-FiniteDouble (Get-PropertyValue $summary 'InitialAzimuthMinutes') "$Role.InitialAzimuthMinutes"
    $initialAlt = ConvertTo-FiniteDouble (Get-PropertyValue $summary 'InitialAltitudeMinutes') "$Role.InitialAltitudeMinutes"
    $initialTotal = ConvertTo-FiniteDouble (Get-PropertyValue $summary 'InitialTotalMinutes') "$Role.InitialTotalMinutes"
    $reciprocalAz = ConvertTo-FiniteDouble (Get-PropertyValue $summary 'ReciprocalAzimuthMinutes') "$Role.ReciprocalAzimuthMinutes"
    $reciprocalAlt = ConvertTo-FiniteDouble (Get-PropertyValue $summary 'ReciprocalAltitudeMinutes') "$Role.ReciprocalAltitudeMinutes"
    $reciprocalTotal = ConvertTo-FiniteDouble (Get-PropertyValue $summary 'ReciprocalTotalMinutes') "$Role.ReciprocalTotalMinutes"
    $repeatAz = ConvertTo-FiniteDouble (Get-PropertyValue $summary 'RepeatedForwardAzimuthMinutes') "$Role.RepeatedForwardAzimuthMinutes"
    $repeatAlt = ConvertTo-FiniteDouble (Get-PropertyValue $summary 'RepeatedForwardAltitudeMinutes') "$Role.RepeatedForwardAltitudeMinutes"
    $repeatTotal = ConvertTo-FiniteDouble (Get-PropertyValue $summary 'RepeatedForwardTotalMinutes') "$Role.RepeatedForwardTotalMinutes"
    foreach ($vector in @(
            @($initialAz, $initialAlt, $initialTotal, 'initial'),
            @($reciprocalAz, $reciprocalAlt, $reciprocalTotal, 'reciprocal'),
            @($repeatAz, $repeatAlt, $repeatTotal, 'repeated-forward'))) {
        $computed = [Math]::Sqrt([double]$vector[0] * [double]$vector[0] + [double]$vector[1] * [double]$vector[1])
        if ([Math]::Abs($computed - [double]$vector[2]) -gt 0.01) { throw "$Role $($vector[3]) components do not reproduce total within receipt tolerance." }
    }
    return [pscustomobject][ordered]@{
        Path = [IO.Path]::GetFullPath($Path)
        RunId = $runId
        StartedUtc = $started
        CompletedUtc = $completed
        ExpectedSampleCount = $expectedSampleCount
        OverdeterminedShadowModelCheck = $shadowModel
        SettleSeconds = $settle
        InitialAzimuthMinutes = $initialAz
        InitialAltitudeMinutes = $initialAlt
        ReciprocalAzimuthMinutes = $reciprocalAz
        ReciprocalAltitudeMinutes = $reciprocalAlt
        RepeatedForwardAzimuthMinutes = $repeatAz
        RepeatedForwardAltitudeMinutes = $repeatAlt
        RepresentativeAzimuthMinutes = ($initialAz + $repeatAz) / 2.0
        RepresentativeAltitudeMinutes = ($initialAlt + $repeatAlt) / 2.0
    }
}

$nominationPath = [IO.Path]::GetFullPath($CandidateNominationReceiptPath)
$nomination = Read-Json $nominationPath
if ((ConvertTo-StrictInt (Get-PropertyValue $nomination 'SchemaVersion') 'nomination.SchemaVersion') -ne 2) {
    throw 'Candidate nomination schema is not 2.'
}
if ([string](Get-PropertyValue $nomination 'Event') -ne 'tppa-settle-cadence-nomination') {
    throw 'Candidate nomination event is invalid.'
}
if (-not (ConvertTo-StrictBoolean (Get-PropertyValue $nomination 'CandidateCadenceQualified') 'nomination.CandidateCadenceQualified')) {
    throw 'Candidate cadence nomination is not qualified.'
}
if (ConvertTo-StrictBoolean (Get-PropertyValue $nomination 'ProductionSettleQualified') 'nomination.ProductionSettleQualified') {
    throw 'Candidate nomination improperly claims production qualification.'
}
$nominatedSettle = ConvertTo-FiniteDouble (Get-PropertyValue $nomination 'RecommendedProbeSettleSeconds') 'nomination.RecommendedProbeSettleSeconds'
$nominatedRig = [string](Get-PropertyValue $nomination 'RigConfigurationId')
if ([string]::IsNullOrWhiteSpace($nominatedRig)) { throw 'Candidate nomination rig is empty.' }
$nominationRunIds = @((Get-PropertyValue $nomination 'Runs') | ForEach-Object {
    $id = [string](Get-PropertyValue $_ 'RunId')
    if ([string]::IsNullOrWhiteSpace($id)) { throw 'Candidate nomination contains an empty RunId.' }
    $id
})
if ($nominationRunIds.Count -lt 20 -or @($nominationRunIds | Select-Object -Unique).Count -ne $nominationRunIds.Count) {
    throw 'Candidate nomination does not contain at least twenty unique run identifiers.'
}

$pairResults = [Collections.Generic.List[object]]::new()
foreach ($manifestInput in $PairManifestPath) {
    $issues = [Collections.Generic.List[string]]::new()
    $manifestPath = [IO.Path]::GetFullPath($manifestInput)
    try {
        $manifest = Read-Json $manifestPath
        if ((ConvertTo-StrictInt (Get-PropertyValue $manifest 'SchemaVersion') 'SchemaVersion') -ne 1) {
            $issues.Add('Manifest schema is not 1')
        }
        if ([string](Get-PropertyValue $manifest 'Event') -ne 'tppa-settle-vector-pair') {
            $issues.Add('Event is not tppa-settle-vector-pair')
        }
        $pairId = [string](Get-PropertyValue $manifest 'PairId')
        $rig = [string](Get-PropertyValue $manifest 'RigConfigurationId')
        $night = [string](Get-PropertyValue $manifest 'DubaiNight')
        $direction = [string](Get-PropertyValue $manifest 'SlewDirection')
        $order = [string](Get-PropertyValue $manifest 'Order')
        if ([string]::IsNullOrWhiteSpace($pairId)) { $issues.Add('PairId is empty') }
        if ([string]::IsNullOrWhiteSpace($rig)) { $issues.Add('RigConfigurationId is empty') }
        if ($night -notmatch '^\d{4}-\d{2}-\d{2}$') { $issues.Add('DubaiNight is invalid') }
        if ($direction -notin @('IncreasingRA', 'DecreasingRA')) { $issues.Add("Invalid slew direction '$direction'") }
        if ($order -notin @('CandidateThenReference', 'ReferenceThenCandidate')) { $issues.Add("Invalid order '$order'") }
        if (-not (ConvertTo-StrictBoolean (Get-PropertyValue $manifest 'SameMechanicalEpoch') 'SameMechanicalEpoch')) {
            $issues.Add('Pair does not share one mechanical epoch')
        }
        if (-not (ConvertTo-StrictBoolean (Get-PropertyValue $manifest 'SamePointingArc') 'SamePointingArc')) {
            $issues.Add('Pair does not use the same pointing arc')
        }
        if (-not (ConvertTo-StrictBoolean (Get-PropertyValue $manifest 'NoPhysicalAdjustmentBetweenRuns') 'NoPhysicalAdjustmentBetweenRuns')) {
            $issues.Add('Physical adjustment occurred between paired runs')
        }
        $candidatePath = Resolve-ManifestPath $manifestPath (Get-PropertyValue $manifest 'CandidateSummaryPath') 'CandidateSummaryPath'
        $referencePath = Resolve-ManifestPath $manifestPath (Get-PropertyValue $manifest 'ReferenceSummaryPath') 'ReferenceSummaryPath'
        if ($candidatePath -eq $referencePath) { $issues.Add('Candidate and reference paths are identical') }
        $candidate = Read-Summary $candidatePath 'candidate'
        $reference = Read-Summary $referencePath 'reference'
        if ($candidate.RunId -eq $reference.RunId) { $issues.Add('Candidate and reference RunId values are identical') }
        if ($candidate.ExpectedSampleCount -ne $reference.ExpectedSampleCount -or
                $candidate.OverdeterminedShadowModelCheck -ne $reference.OverdeterminedShadowModelCheck) {
            $issues.Add('Candidate and reference measurement methods differ')
        }
        $first = if ($order -eq 'CandidateThenReference') { $candidate } else { $reference }
        $second = if ($order -eq 'CandidateThenReference') { $reference } else { $candidate }
        if ($second.StartedUtc -lt $first.CompletedUtc) {
            $issues.Add('Receipt timestamps contradict the declared execution order')
        } elseif (($second.StartedUtc - $first.CompletedUtc).TotalMinutes -gt $MaximumInterRunGapMinutes) {
            $issues.Add("Inter-run gap exceeds $MaximumInterRunGapMinutes minutes")
        }
        $candidateNight = Get-DubaiObservingNight $candidate.StartedUtc
        $referenceNight = Get-DubaiObservingNight $reference.StartedUtc
        if ($candidateNight -ne $night -or $referenceNight -ne $night) {
            $issues.Add('DubaiNight does not match both receipt timestamps')
        }
        if ($candidate.SettleSeconds -gt $MaximumCandidateSettleSeconds) {
            $issues.Add("Candidate settle $($candidate.SettleSeconds) s exceeds $MaximumCandidateSettleSeconds s")
        }
        if ([Math]::Abs($reference.SettleSeconds - $ReferenceSettleSeconds) -gt $SettleToleranceSeconds) {
            $issues.Add("Reference settle $($reference.SettleSeconds) s is not $ReferenceSettleSeconds s")
        }
        if ($candidate.SettleSeconds -ge $reference.SettleSeconds) {
            $issues.Add('Candidate settle is not shorter than reference settle')
        }
        $initialDeltaAz = $candidate.InitialAzimuthMinutes - $reference.InitialAzimuthMinutes
        $initialDeltaAlt = $candidate.InitialAltitudeMinutes - $reference.InitialAltitudeMinutes
        $initialSeparation = [Math]::Sqrt($initialDeltaAz * $initialDeltaAz + $initialDeltaAlt * $initialDeltaAlt)
        $repeatedDeltaAz = $candidate.RepeatedForwardAzimuthMinutes - $reference.RepeatedForwardAzimuthMinutes
        $repeatedDeltaAlt = $candidate.RepeatedForwardAltitudeMinutes - $reference.RepeatedForwardAltitudeMinutes
        $repeatedSeparation = [Math]::Sqrt($repeatedDeltaAz * $repeatedDeltaAz + $repeatedDeltaAlt * $repeatedDeltaAlt)
        $reciprocalDeltaAz = $candidate.ReciprocalAzimuthMinutes - $reference.ReciprocalAzimuthMinutes
        $reciprocalDeltaAlt = $candidate.ReciprocalAltitudeMinutes - $reference.ReciprocalAltitudeMinutes
        $reciprocalSeparation = [Math]::Sqrt($reciprocalDeltaAz * $reciprocalDeltaAz + $reciprocalDeltaAlt * $reciprocalDeltaAlt)
        $deltaAz = $candidate.RepresentativeAzimuthMinutes - $reference.RepresentativeAzimuthMinutes
        $deltaAlt = $candidate.RepresentativeAltitudeMinutes - $reference.RepresentativeAltitudeMinutes
        $separation = [Math]::Sqrt($deltaAz * $deltaAz + $deltaAlt * $deltaAlt)
        $maximumLegSeparation = [Math]::Max($reciprocalSeparation, [Math]::Max($initialSeparation, $repeatedSeparation))
        if ($maximumLegSeparation -gt $MaximumVectorSeparationMinutes) {
            $issues.Add("Determination-leg separation $($maximumLegSeparation.ToString('F3')) arcmin exceeds $MaximumVectorSeparationMinutes arcmin")
        }
        if ($separation -gt $MaximumVectorSeparationMinutes) {
            $issues.Add("Midpoint vector separation $($separation.ToString('F3')) arcmin exceeds $MaximumVectorSeparationMinutes arcmin")
        }
        $pairResults.Add([pscustomobject][ordered]@{
            Path = $manifestPath
            PairId = $pairId
            RigConfigurationId = $rig
            DubaiNight = $night
            SlewDirection = $direction
            Order = $order
            CandidateRunId = $candidate.RunId
            ReferenceRunId = $reference.RunId
            CandidateSettleSeconds = $candidate.SettleSeconds
            ReferenceSettleSeconds = $reference.SettleSeconds
            ExpectedSampleCount = $candidate.ExpectedSampleCount
            OverdeterminedShadowModelCheck = $candidate.OverdeterminedShadowModelCheck
            InitialVectorSeparationMinutes = $initialSeparation
            ReciprocalVectorSeparationMinutes = $reciprocalSeparation
            RepeatedForwardVectorSeparationMinutes = $repeatedSeparation
            MidpointVectorSeparationMinutes = $separation
            MaximumLegVectorSeparationMinutes = $maximumLegSeparation
            Valid = $issues.Count -eq 0
            Issues = $issues.ToArray()
        })
    } catch {
        $issues.Add($_.Exception.Message)
        $pairResults.Add([pscustomobject][ordered]@{
            Path = $manifestPath
            PairId = $null
            RigConfigurationId = $null
            DubaiNight = $null
            SlewDirection = $null
            Order = $null
            CandidateRunId = $null
            ReferenceRunId = $null
            CandidateSettleSeconds = $null
            ReferenceSettleSeconds = $null
            ExpectedSampleCount = $null
            OverdeterminedShadowModelCheck = $null
            InitialVectorSeparationMinutes = $null
            ReciprocalVectorSeparationMinutes = $null
            RepeatedForwardVectorSeparationMinutes = $null
            MidpointVectorSeparationMinutes = $null
            MaximumLegVectorSeparationMinutes = $null
            Valid = $false
            Issues = $issues.ToArray()
        })
    }
}

$validPairs = @($pairResults | Where-Object Valid)
$campaignIssues = [Collections.Generic.List[string]]::new()
if (@($pairResults | Where-Object { -not $_.Valid }).Count -gt 0) { $campaignIssues.Add('One or more pairs are invalid') }
if ($validPairs.Count -lt $MinimumPairs) { $campaignIssues.Add("Only $($validPairs.Count) valid pairs; $MinimumPairs required") }
if (@($validPairs.ExpectedSampleCount | Select-Object -Unique).Count -gt 1 -or
        @($validPairs.OverdeterminedShadowModelCheck | Select-Object -Unique).Count -gt 1) {
    $campaignIssues.Add('Campaign mixes TPPA measurement methods')
}
$pairIds = @($validPairs.PairId)
if (@($pairIds | Select-Object -Unique).Count -ne $pairIds.Count) { $campaignIssues.Add('Duplicate PairId values are not independent evidence') }
$runIds = @($validPairs | ForEach-Object { $_.CandidateRunId; $_.ReferenceRunId })
if (@($runIds | Select-Object -Unique).Count -ne $runIds.Count) { $campaignIssues.Add('A TPPA run was reused across pairs') }
$rigs = @($validPairs.RigConfigurationId | Select-Object -Unique)
if ($rigs.Count -ne 1) { $campaignIssues.Add('Campaign must contain exactly one rig configuration') }
$candidateSettles = @($validPairs.CandidateSettleSeconds | Select-Object -Unique)
if ($candidateSettles.Count -ne 1) { $campaignIssues.Add('Campaign must test exactly one candidate settle value') }
if ($rigs.Count -eq 1 -and $rigs[0] -ne $nominatedRig) {
    $campaignIssues.Add('Pair campaign rig does not match the candidate nomination')
}
if ($candidateSettles.Count -eq 1 -and [Math]::Abs([double]$candidateSettles[0] - $nominatedSettle) -gt $SettleToleranceSeconds) {
    $campaignIssues.Add('Pair campaign settle does not match the candidate nomination')
}
if (@($runIds | Where-Object { $_ -in $nominationRunIds }).Count -gt 0) {
    $campaignIssues.Add('Nomination evidence was reused as paired qualification evidence')
}
$nights = @($validPairs.DubaiNight | Select-Object -Unique)
if ($nights.Count -lt $MinimumDubaiNights) { $campaignIssues.Add("Only $($nights.Count) Dubai nights; $MinimumDubaiNights required") }
foreach ($requiredDirection in @('IncreasingRA', 'DecreasingRA')) {
    $count = @($validPairs | Where-Object SlewDirection -eq $requiredDirection).Count
    if ($count -lt $MinimumPairsPerDirection) { $campaignIssues.Add("Only $count $requiredDirection pairs; $MinimumPairsPerDirection required") }
}
foreach ($requiredOrder in @('CandidateThenReference', 'ReferenceThenCandidate')) {
    $count = @($validPairs | Where-Object Order -eq $requiredOrder).Count
    if ($count -lt $MinimumPairsPerOrder) { $campaignIssues.Add("Only $count $requiredOrder pairs; $MinimumPairsPerOrder required") }
}
foreach ($night in $nights) {
    $count = @($validPairs | Where-Object DubaiNight -eq $night).Count
    if ($count -lt $MinimumPairsPerDubaiNight) { $campaignIssues.Add("Only $count pairs on Dubai night $night; $MinimumPairsPerDubaiNight required") }
}

function Get-ObservedDistribution([object[]]$Values) {
    $sorted = @($Values | Sort-Object)
    if ($sorted.Count -eq 0) { return [pscustomobject]@{ Maximum=$null; Median=$null; P95=$null } }
    $middle = [int][Math]::Floor($sorted.Count / 2)
    $median = if ($sorted.Count % 2 -eq 0) { ([double]$sorted[$middle - 1] + [double]$sorted[$middle]) / 2.0 } else { [double]$sorted[$middle] }
    $p95Index = [Math]::Min($sorted.Count - 1, [Math]::Ceiling(0.95 * $sorted.Count) - 1)
    return [pscustomobject]@{ Maximum=[double]$sorted[-1]; Median=$median; P95=[double]$sorted[$p95Index] }
}
$worstSeparations = @($validPairs | ForEach-Object {
    [Math]::Max([double]$_.MaximumLegVectorSeparationMinutes, [double]$_.MidpointVectorSeparationMinutes)
})
$separationDistribution = Get-ObservedDistribution $worstSeparations
$maximumObservedNightFraction = if ($validPairs.Count -gt 0) {
    [double](($nights | ForEach-Object { @($validPairs | Where-Object DubaiNight -eq $_).Count } | Measure-Object -Maximum).Maximum) / $validPairs.Count
} else { $null }
if ($null -ne $maximumObservedNightFraction -and $maximumObservedNightFraction -gt $MaximumNightFraction) {
    $campaignIssues.Add("One Dubai night supplies $($maximumObservedNightFraction.ToString('P1')) of vector pairs; maximum is $($MaximumNightFraction.ToString('P1'))")
}

$pairedCandidateQualified = $campaignIssues.Count -eq 0
[pscustomobject][ordered]@{
    SchemaVersion = 2
    Event = 'tppa-settle-vector-pair-campaign'
    GeneratedUtc = [DateTimeOffset]::UtcNow.ToString('O')
    CandidateNominationReceiptPath = $nominationPath
    CandidateNominationReceiptSha256 = (Get-FileHash -LiteralPath $nominationPath -Algorithm SHA256).Hash
    RigConfigurationId = if ($rigs.Count -eq 1) { $rigs[0] } else { $null }
    CandidateSettleSeconds = if ($candidateSettles.Count -eq 1) { $candidateSettles[0] } else { $null }
    ReferenceSettleSeconds = $ReferenceSettleSeconds
    MaximumInterRunGapMinutes = $MaximumInterRunGapMinutes
    MaximumNightFraction = $MaximumNightFraction
    MaximumObservedNightFraction = $maximumObservedNightFraction
    MaximumVectorSeparationMinutes = $MaximumVectorSeparationMinutes
    ObservedMaximumPairSeparationMinutes = $separationDistribution.Maximum
    ObservedMedianPairSeparationMinutes = $separationDistribution.Median
    ObservedP95PairSeparationMinutes = $separationDistribution.P95
    MinimumPairs = $MinimumPairs
    MinimumDubaiNights = $MinimumDubaiNights
    MinimumPairsPerDirection = $MinimumPairsPerDirection
    MinimumPairsPerOrder = $MinimumPairsPerOrder
    MinimumPairsPerDubaiNight = $MinimumPairsPerDubaiNight
    MaximumCandidateSettleSeconds = $MaximumCandidateSettleSeconds
    ValidPairCount = $validPairs.Count
    DubaiNightCount = $nights.Count
    CandidateVectorPairQualified = $pairedCandidateQualified
    RecommendedPairedSettleSeconds = if ($pairedCandidateQualified) { $candidateSettles[0] } else { $null }
    ProductionSettleQualified = $false
    RecommendedProductionSettleSeconds = $null
    GrantsUpasMotionAuthority = $false
    PhysicalAdjustmentStatus = 'operator-attested; not independently observed by this analyzer'
    ScopeNote = 'This qualifies the candidate-versus-reference vector-pair stage for one named rig configuration. It does not qualify the production settle, prove absolute polar-alignment accuracy, or authorize UPAS motion.'
    NextRequiredGate = 'Acquire an independent or interleaved 30-second-versus-30-second null dataset to characterize same-method dispersion. Production promotion remains deliberately unimplemented.'
    Issues = $campaignIssues.ToArray()
    Pairs = $pairResults.ToArray()
}
