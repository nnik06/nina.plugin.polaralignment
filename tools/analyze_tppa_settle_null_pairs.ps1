#requires -Version 7.5
param(
    [Parameter(Mandatory = $true)]
    [string[]]$NullPairManifestPath,
    [ValidateRange(2, 100)]
    [int]$MinimumNullPairs = 10,
    [ValidateRange(1, 20)]
    [int]$MinimumDubaiNights = 2,
    [ValidateRange(1, 50)]
    [int]$MinimumPairsPerDirection = 4,
    [ValidateRange(1, 50)]
    [int]$MinimumPairsPerDubaiNight = 4,
    [ValidateRange(0.05, 5.0)]
    [double]$MaximumVectorSeparationMinutes = 0.5,
    [ValidateRange(29.0, 31.0)]
    [double]$ReferenceSettleSeconds = 30.0,
    [ValidateRange(0.001, 1.0)]
    [double]$SettleToleranceSeconds = 0.01,
    [ValidateRange(1.0, 60.0)]
    [double]$MaximumInterRunGapMinutes = 15.0
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
    foreach ($field in @('DiagnosticPassed', 'RepeatabilityPassed', 'ReciprocityPassed')) {
        if (-not (ConvertTo-StrictBoolean (Get-PropertyValue $summary $field) "$Role.$field")) {
            throw "$Role verification diagnostics did not all pass."
        }
    }
    if (-not (ConvertTo-StrictBoolean (Get-PropertyValue $summary 'RefractionAdjustmentEnabled') "$Role.RefractionAdjustmentEnabled")) {
        throw "$Role verification did not target the true pole."
    }
    if (-not (ConvertTo-StrictBoolean (Get-PropertyValue $summary 'ComponentTotalsConsistent') "$Role.ComponentTotalsConsistent")) {
        throw "$Role component totals are inconsistent."
    }
    $expectedSampleCount = ConvertTo-StrictInt (Get-PropertyValue $summary 'ExpectedSampleCount') "$Role.ExpectedSampleCount"
    $shadowModel = ConvertTo-StrictBoolean (Get-PropertyValue $summary 'OverdeterminedShadowModelCheck') "$Role.OverdeterminedShadowModelCheck"
    $settle = ConvertTo-FiniteDouble (Get-PropertyValue $summary 'EffectivePointSettleSeconds') "$Role.EffectivePointSettleSeconds"
    if ([Math]::Abs($settle - $ReferenceSettleSeconds) -gt $SettleToleranceSeconds) {
        throw "$Role settle $settle s is not $ReferenceSettleSeconds s."
    }
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
        RunId = $runId
        StartedUtc = $started
        CompletedUtc = $completed
        ExpectedSampleCount = $expectedSampleCount
        OverdeterminedShadowModelCheck = $shadowModel
        InitialAzimuthMinutes = $initialAz
        InitialAltitudeMinutes = $initialAlt
        ReciprocalAzimuthMinutes = $reciprocalAz
        ReciprocalAltitudeMinutes = $reciprocalAlt
        RepeatedForwardAzimuthMinutes = $repeatAz
        RepeatedForwardAltitudeMinutes = $repeatAlt
        MidpointAzimuthMinutes = ($initialAz + $repeatAz) / 2.0
        MidpointAltitudeMinutes = ($initialAlt + $repeatAlt) / 2.0
    }
}

$pairResults = [Collections.Generic.List[object]]::new()
foreach ($manifestInput in $NullPairManifestPath) {
    $issues = [Collections.Generic.List[string]]::new()
    $manifestPath = [IO.Path]::GetFullPath($manifestInput)
    try {
        $manifest = Read-Json $manifestPath
        if ((ConvertTo-StrictInt (Get-PropertyValue $manifest 'SchemaVersion') 'SchemaVersion') -ne 1) { $issues.Add('Manifest schema is not 1') }
        if ([string](Get-PropertyValue $manifest 'Event') -ne 'tppa-settle-null-pair') { $issues.Add('Event is not tppa-settle-null-pair') }
        $pairId = [string](Get-PropertyValue $manifest 'PairId')
        $rig = [string](Get-PropertyValue $manifest 'RigConfigurationId')
        $night = [string](Get-PropertyValue $manifest 'DubaiNight')
        $direction = [string](Get-PropertyValue $manifest 'SlewDirection')
        if ([string]::IsNullOrWhiteSpace($pairId)) { $issues.Add('PairId is empty') }
        if ([string]::IsNullOrWhiteSpace($rig)) { $issues.Add('RigConfigurationId is empty') }
        if ($night -notmatch '^\d{4}-\d{2}-\d{2}$') { $issues.Add('DubaiNight is invalid') }
        if ($direction -notin @('IncreasingRA', 'DecreasingRA')) { $issues.Add("Invalid slew direction '$direction'") }
        foreach ($field in @('SameMechanicalEpoch', 'SamePointingArc', 'NoPhysicalAdjustmentBetweenRuns')) {
            if (-not (ConvertTo-StrictBoolean (Get-PropertyValue $manifest $field) $field)) { $issues.Add("$field is false") }
        }
        $firstPath = Resolve-ManifestPath $manifestPath (Get-PropertyValue $manifest 'FirstSummaryPath') 'FirstSummaryPath'
        $secondPath = Resolve-ManifestPath $manifestPath (Get-PropertyValue $manifest 'SecondSummaryPath') 'SecondSummaryPath'
        if ($firstPath -eq $secondPath) { $issues.Add('First and second paths are identical') }
        $first = Read-Summary $firstPath 'first'
        $second = Read-Summary $secondPath 'second'
        if ($first.RunId -eq $second.RunId) { $issues.Add('First and second RunId values are identical') }
        if ($first.ExpectedSampleCount -ne $second.ExpectedSampleCount -or
                $first.OverdeterminedShadowModelCheck -ne $second.OverdeterminedShadowModelCheck) {
            $issues.Add('First and second measurement methods differ')
        }
        if ($second.StartedUtc -lt $first.CompletedUtc) {
            $issues.Add('Receipt timestamps contradict first-then-second order')
        } elseif (($second.StartedUtc - $first.CompletedUtc).TotalMinutes -gt $MaximumInterRunGapMinutes) {
            $issues.Add("Inter-run gap exceeds $MaximumInterRunGapMinutes minutes")
        }
        if ((Get-DubaiObservingNight $first.StartedUtc) -ne $night -or (Get-DubaiObservingNight $second.StartedUtc) -ne $night) {
            $issues.Add('DubaiNight does not match both receipt timestamps')
        }
        $initialAz = $first.InitialAzimuthMinutes - $second.InitialAzimuthMinutes
        $initialAlt = $first.InitialAltitudeMinutes - $second.InitialAltitudeMinutes
        $initialSeparation = [Math]::Sqrt($initialAz * $initialAz + $initialAlt * $initialAlt)
        $repeatAz = $first.RepeatedForwardAzimuthMinutes - $second.RepeatedForwardAzimuthMinutes
        $repeatAlt = $first.RepeatedForwardAltitudeMinutes - $second.RepeatedForwardAltitudeMinutes
        $repeatedSeparation = [Math]::Sqrt($repeatAz * $repeatAz + $repeatAlt * $repeatAlt)
        $reciprocalAz = $first.ReciprocalAzimuthMinutes - $second.ReciprocalAzimuthMinutes
        $reciprocalAlt = $first.ReciprocalAltitudeMinutes - $second.ReciprocalAltitudeMinutes
        $reciprocalSeparation = [Math]::Sqrt($reciprocalAz * $reciprocalAz + $reciprocalAlt * $reciprocalAlt)
        $midAz = $first.MidpointAzimuthMinutes - $second.MidpointAzimuthMinutes
        $midAlt = $first.MidpointAltitudeMinutes - $second.MidpointAltitudeMinutes
        $midpointSeparation = [Math]::Sqrt($midAz * $midAz + $midAlt * $midAlt)
        $maximumLegSeparation = [Math]::Max($reciprocalSeparation, [Math]::Max($initialSeparation, $repeatedSeparation))
        $exceedsConfiguredThreshold = $maximumLegSeparation -gt $MaximumVectorSeparationMinutes -or
            $midpointSeparation -gt $MaximumVectorSeparationMinutes
        $pairResults.Add([pscustomobject][ordered]@{
            Path = $manifestPath
            PairId = $pairId
            RigConfigurationId = $rig
            DubaiNight = $night
            SlewDirection = $direction
            FirstRunId = $first.RunId
            SecondRunId = $second.RunId
            ExpectedSampleCount = $first.ExpectedSampleCount
            OverdeterminedShadowModelCheck = $first.OverdeterminedShadowModelCheck
            MaximumLegVectorSeparationMinutes = $maximumLegSeparation
            ReciprocalVectorSeparationMinutes = $reciprocalSeparation
            MidpointVectorSeparationMinutes = $midpointSeparation
            ExceedsConfiguredThreshold = $exceedsConfiguredThreshold
            Valid = $issues.Count -eq 0
            Issues = $issues.ToArray()
        })
    } catch {
        $issues.Add($_.Exception.Message)
        $pairResults.Add([pscustomobject][ordered]@{
            Path = $manifestPath; PairId = $null; RigConfigurationId = $null; DubaiNight = $null
            SlewDirection = $null; FirstRunId = $null; SecondRunId = $null
            ExpectedSampleCount = $null; OverdeterminedShadowModelCheck = $null
            MaximumLegVectorSeparationMinutes = $null; ReciprocalVectorSeparationMinutes = $null
            MidpointVectorSeparationMinutes = $null; ExceedsConfiguredThreshold = $null
            Valid = $false; Issues = $issues.ToArray()
        })
    }
}

$validPairs = @($pairResults | Where-Object Valid)
$campaignIssues = [Collections.Generic.List[string]]::new()
if (@($pairResults | Where-Object { -not $_.Valid }).Count -gt 0) { $campaignIssues.Add('One or more null pairs are invalid') }
if ($validPairs.Count -lt $MinimumNullPairs) { $campaignIssues.Add("Only $($validPairs.Count) valid null pairs; $MinimumNullPairs required") }
if (@($validPairs.ExpectedSampleCount | Select-Object -Unique).Count -gt 1 -or
        @($validPairs.OverdeterminedShadowModelCheck | Select-Object -Unique).Count -gt 1) {
    $campaignIssues.Add('Null campaign mixes TPPA measurement methods')
}
$pairIds = @($validPairs.PairId)
if (@($pairIds | Select-Object -Unique).Count -ne $pairIds.Count) { $campaignIssues.Add('Duplicate PairId values are not independent evidence') }
$runIds = @($validPairs | ForEach-Object { $_.FirstRunId; $_.SecondRunId })
if (@($runIds | Select-Object -Unique).Count -ne $runIds.Count) { $campaignIssues.Add('A null-arm TPPA run was reused') }
$rigs = @($validPairs.RigConfigurationId | Select-Object -Unique)
if ($rigs.Count -ne 1) { $campaignIssues.Add('Null arm does not contain exactly one rig configuration') }
$nights = @($validPairs.DubaiNight | Select-Object -Unique)
if ($nights.Count -lt $MinimumDubaiNights) { $campaignIssues.Add("Only $($nights.Count) Dubai nights; $MinimumDubaiNights required") }
foreach ($direction in @('IncreasingRA', 'DecreasingRA')) {
    $count = @($validPairs | Where-Object SlewDirection -eq $direction).Count
    if ($count -lt $MinimumPairsPerDirection) { $campaignIssues.Add("Only $count $direction null pairs; $MinimumPairsPerDirection required") }
}
foreach ($night in $nights) {
    $count = @($validPairs | Where-Object DubaiNight -eq $night).Count
    if ($count -lt $MinimumPairsPerDubaiNight) { $campaignIssues.Add("Only $count null pairs on Dubai night $night; $MinimumPairsPerDubaiNight required") }
}

$datasetQualified = $campaignIssues.Count -eq 0
function Get-ObservedDistribution([object[]]$Values) {
    $sorted = @($Values | Sort-Object)
    if ($sorted.Count -eq 0) { return [pscustomobject]@{ Maximum = $null; P95 = $null } }
    $p95Index = [Math]::Min($sorted.Count - 1, [Math]::Ceiling(0.95 * $sorted.Count) - 1)
    return [pscustomobject]@{ Maximum = [double]$sorted[-1]; P95 = [double]$sorted[$p95Index] }
}
$legDistribution = Get-ObservedDistribution @($validPairs.MaximumLegVectorSeparationMinutes)
$midpointDistribution = Get-ObservedDistribution @($validPairs.MidpointVectorSeparationMinutes)
[pscustomobject][ordered]@{
    SchemaVersion = 2
    Event = 'tppa-settle-null-dataset'
    GeneratedUtc = [DateTimeOffset]::UtcNow.ToString('O')
    RigConfigurationId = if ($rigs.Count -eq 1) { $rigs[0] } else { $null }
    ReferenceSettleSeconds = $ReferenceSettleSeconds
    ConfiguredComparisonThresholdMinutes = $MaximumVectorSeparationMinutes
    MinimumNullPairs = $MinimumNullPairs
    MinimumDubaiNights = $MinimumDubaiNights
    MinimumPairsPerDirection = $MinimumPairsPerDirection
    MinimumPairsPerDubaiNight = $MinimumPairsPerDubaiNight
    MaximumInterRunGapMinutes = $MaximumInterRunGapMinutes
    ValidNullPairCount = $validPairs.Count
    DubaiNightCount = $nights.Count
    DubaiNights = $nights
    NullArmDatasetQualified = $datasetQualified
    ObservedMaximumNullLegSeparationMinutes = $legDistribution.Maximum
    ObservedP95NullLegSeparationMinutes = $legDistribution.P95
    ObservedMaximumNullMidpointSeparationMinutes = $midpointDistribution.Maximum
    ObservedP95NullMidpointSeparationMinutes = $midpointDistribution.P95
    ProductionSettleQualified = $false
    RecommendedProductionSettleSeconds = $null
    GrantsUpasMotionAuthority = $false
    PhysicalAdjustmentStatus = 'operator-attested; not independently observed by this analyzer'
    ScopeNote = 'This structurally validates and describes a 30-second-versus-30-second null dataset for one named rig configuration. It does not establish a production threshold, qualify a shorter settle, prove absolute polar-alignment accuracy, or authorize UPAS motion.'
    Issues = $campaignIssues.ToArray()
    NullPairs = $pairResults.ToArray()
}
