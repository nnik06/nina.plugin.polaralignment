#requires -Version 7.0
param(
    [Parameter(Mandatory)]
    [ValidateSet(
        'WO-GT81-IV-0.8-OAG-L-ASI2600MM-gain100-bin1',
        'EdgeHD-9.25-0.7-OAG-L-ASI2600MM-gain100-bin1')]
    [string]$OpticalTrainId,
    [Parameter(Mandatory)][string]$PluginDirectory,
    [Parameter(Mandatory)][ValidatePattern('^[0-9A-Fa-f]{64}$')]
    [string]$RuntimeManifestSha256,
    [Parameter(Mandatory)][string]$SequencePath,
    [Parameter(Mandatory)][ValidatePattern('^[0-9A-Fa-f]{64}$')]
    [string]$SequenceSha256,
    [Parameter(Mandatory)][string]$PolicyPath,
    [Parameter(Mandatory)][ValidatePattern('^[0-9A-Fa-f]{64}$')]
    [string]$PolicySha256,
    [Parameter(Mandatory)][string]$RequiredFilterName,
    [Parameter(Mandatory)][string]$ShortScoutFitsPath,
    [Parameter(Mandatory)][ValidatePattern('^[0-9A-Fa-f]{64}$')]
    [string]$ShortScoutFitsSha256,
    [Parameter(Mandatory)][string]$LongScoutFitsPath,
    [Parameter(Mandatory)][ValidatePattern('^[0-9A-Fa-f]{64}$')]
    [string]$LongScoutFitsSha256,
    [Parameter(Mandatory)][string]$SaturationScoutReceiptPath,
    [Parameter(Mandatory)][ValidatePattern('^[0-9A-Fa-f]{64}$')]
    [string]$SaturationScoutReceiptSha256,
    [Parameter(Mandatory)][ValidateRange(0.01, 60.0)]
    [double]$MeasuredPixelScaleArcsecondsPerPixel,
    [Parameter(Mandatory)][string]$OagGeometryReceiptPath,
    [Parameter(Mandatory)][ValidatePattern('^[0-9A-Fa-f]{64}$')]
    [string]$OagGeometryReceiptSha256,
    [Parameter(Mandatory)][string]$OperationalQualificationReportPath,
    [Parameter(Mandatory)][ValidatePattern('^[0-9A-Fa-f]{64}$')]
    [string]$OperationalQualificationReportSha256,
    [Parameter(Mandatory)][string]$OutputPath,
    [ValidateRange(0.1, 10.0)][double]$MaximumPaErrorMinutes = 3.0,
    [ValidateRange(1.0, 180.0)][double]$MaximumEvidenceAgeMinutes = 30.0,
    [ValidateRange(0.0001, 0.02)][double]$MaximumPolicyScaleRelativeError = 0.001,
    [DateTimeOffset]$NowUtc = [DateTimeOffset]::UtcNow
)

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'
$invariant = [Globalization.CultureInfo]::InvariantCulture

function Get-RequiredFile([string]$Path, [string]$Label) {
    $full = [IO.Path]::GetFullPath($Path)
    if (-not [IO.File]::Exists($full)) { throw "$Label is missing: $full" }
    if ([IO.FileInfo]::new($full).Length -le 0) { throw "$Label is empty: $full" }
    return $full
}

function Assert-Hash([string]$Path, [string]$Expected, [string]$Label) {
    $actual = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    if ($actual -ne $Expected.ToUpperInvariant()) {
        throw "$Label SHA-256 mismatch. Expected $Expected; actual $actual."
    }
    return $actual
}

function Get-SerializedTypes($Node) {
    $types = [Collections.Generic.List[string]]::new()
    if ($null -eq $Node) { return $types.ToArray() }
    if ($Node -is [Management.Automation.PSCustomObject]) {
        foreach ($property in $Node.PSObject.Properties) {
            if ($property.Name -eq '$type') {
                $types.Add([string]$property.Value)
            } else {
                foreach ($type in @(Get-SerializedTypes $property.Value)) {
                    $types.Add($type)
                }
            }
        }
    } elseif ($Node -is [Collections.IEnumerable] -and $Node -isnot [string]) {
        foreach ($value in $Node) {
            foreach ($type in @(Get-SerializedTypes $value)) { $types.Add($type) }
        }
    }
    return $types.ToArray()
}

function Assert-PassiveSequence([string]$Path, [string]$Train, [string]$Filter) {
    $raw = [IO.File]::ReadAllText($Path)
    $root = $raw | ConvertFrom-Json
    $allowedTypes = @(
        'NINA.Core.Model.Equipment.BinningMode, NINA.Core',
        'NINA.Sequencer.Conditions.LoopCondition, NINA.Sequencer',
        'NINA.Sequencer.Container.EndAreaContainer, NINA.Sequencer',
        'NINA.Sequencer.Container.ExecutionStrategy.SequentialStrategy, NINA.Sequencer',
        'NINA.Sequencer.Container.SequenceRootContainer, NINA.Sequencer',
        'NINA.Sequencer.Container.SequentialContainer, NINA.Sequencer',
        'NINA.Sequencer.Container.StartAreaContainer, NINA.Sequencer',
        'NINA.Sequencer.Container.TargetAreaContainer, NINA.Sequencer',
        'NINA.Sequencer.SequenceItem.Imaging.TakeExposure, NINA.Sequencer',
        'NINA.Sequencer.SequenceItem.Utility.Annotation, NINA.Sequencer',
        'System.Collections.ObjectModel.ObservableCollection`1[[NINA.Sequencer.Conditions.ISequenceCondition, NINA.Sequencer]], System.ObjectModel',
        'System.Collections.ObjectModel.ObservableCollection`1[[NINA.Sequencer.SequenceItem.ISequenceItem, NINA.Sequencer]], System.ObjectModel',
        'System.Collections.ObjectModel.ObservableCollection`1[[NINA.Sequencer.Trigger.ISequenceTrigger, NINA.Sequencer]], System.ObjectModel')
    $unexpected = @(Get-SerializedTypes $root | Where-Object { $_ -notin $allowedTypes })
    if ($unexpected.Count -gt 0) {
        throw "Sequence contains executable types outside the passive allow-list: $($unexpected -join ', ')"
    }
    if ([string]$root.Name -ne "Actual 900s Witness - $Train") {
        throw 'Sequence optical-train identity is wrong.'
    }
    $ids = @([regex]::Matches($raw, '"\$id"\s*:\s*"([0-9]+)"') |
        ForEach-Object { $_.Groups[1].Value })
    $references = @([regex]::Matches($raw, '"\$ref"\s*:\s*"([0-9]+)"') |
        ForEach-Object { $_.Groups[1].Value })
    if ($ids.Count -eq 0 -or @($ids | Select-Object -Unique).Count -ne $ids.Count) {
        throw 'Sequence object IDs are missing or duplicated.'
    }
    foreach ($reference in $references) {
        if ($reference -notin $ids) { throw "Sequence contains unresolved object reference $reference." }
    }
    $areas = @($root.Items.'$values')
    if ($areas.Count -ne 3 -or
            @($areas[0].Items.'$values').Count -ne 0 -or
            @($areas[2].Items.'$values').Count -ne 0) {
        throw 'Sequence root/start/end structure is not passive and exact.'
    }
    foreach ($container in @($root, $areas[0], $areas[1], $areas[2])) {
        if (@($container.Conditions.'$values').Count -ne 0 -or
                @($container.Triggers.'$values').Count -ne 0) {
            throw 'Root and area containers must have no conditions or triggers.'
        }
    }
    $brackets = @($areas[1].Items.'$values')
    if ($brackets.Count -ne 1) { throw 'Sequence must contain exactly one witness bracket.' }
    $items = @($brackets[0].Items.'$values')
    if ($items.Count -ne 4) { throw 'Witness bracket must contain annotation, pre, long and post nodes.' }
    $requiredAnnotation = "Required filter: '$Filter'."
    if (-not ([string]$items[0].Text).Contains($requiredAnnotation)) {
        throw 'Witness sequence does not bind the exact required filter.'
    }
    if (@($brackets[0].Conditions.'$values').Count -ne 0 -or
            @($brackets[0].Triggers.'$values').Count -ne 0 -or
            [string]$items[0].'$type' -ne
                'NINA.Sequencer.SequenceItem.Utility.Annotation, NINA.Sequencer' -or
            [string]$items[2].'$type' -ne
                'NINA.Sequencer.SequenceItem.Imaging.TakeExposure, NINA.Sequencer') {
        throw 'Witness bracket contains unexpected conditions, triggers, or node ordering.'
    }
    $pre = $items[1]
    $long = $items[2]
    $post = $items[3]
    if ([int]$pre.Conditions.'$values'[0].Iterations -ne 5 -or
            [int]$post.Conditions.'$values'[0].Iterations -ne 5 -or
            [int]$pre.Conditions.'$values'[0].CompletedIterations -ne 0 -or
            [int]$post.Conditions.'$values'[0].CompletedIterations -ne 0 -or
            @($pre.Conditions.'$values').Count -ne 1 -or
            @($post.Conditions.'$values').Count -ne 1 -or
            @($pre.Items.'$values').Count -ne 1 -or
            @($post.Items.'$values').Count -ne 1 -or
            @($pre.Triggers.'$values').Count -ne 0 -or
            @($post.Triggers.'$values').Count -ne 0) {
        throw 'Witness bracket must contain five controls on each side.'
    }
    $exposures = @($pre.Items.'$values'[0], $long, $post.Items.'$values'[0])
    if ([double]$exposures[0].ExposureTime -ne 30.0 -or
            [double]$exposures[1].ExposureTime -ne 900.0 -or
            [double]$exposures[2].ExposureTime -ne 30.0) {
        throw 'Witness exposure durations are not exactly 30/900/30 seconds.'
    }
    foreach ($exposure in $exposures) {
        if ([int]$exposure.Gain -ne 100 -or [int]$exposure.Offset -ne 50 -or
                [int]$exposure.Binning.X -ne 1 -or [int]$exposure.Binning.Y -ne 1 -or
                [string]$exposure.ImageType -ne 'LIGHT') {
            throw 'Witness camera settings are not gain 100, offset 50, bin 1, LIGHT.'
        }
    }
}

function Assert-Fresh([DateTimeOffset]$ObservedUtc, [string]$Label) {
    $age = ($NowUtc.ToUniversalTime() - $ObservedUtc.ToUniversalTime()).TotalMinutes
    if ($age -lt -1.0 -or $age -gt $MaximumEvidenceAgeMinutes) {
        throw "$Label age $([Math]::Round($age, 3)) minutes is outside the permitted window."
    }
}

$pluginRoot = (Resolve-Path -LiteralPath $PluginDirectory).Path
$runtime = & (Join-Path $PSScriptRoot 'validate_tppa_plugin_install.ps1') `
    -PluginDirectory $pluginRoot `
    -ExpectedRuntimeManifestSha256 $RuntimeManifestSha256

$sequence = Get-RequiredFile $SequencePath 'NINA witness sequence'
$sequenceHash = Assert-Hash $sequence $SequenceSha256 'NINA witness sequence'
Assert-PassiveSequence $sequence $OpticalTrainId $RequiredFilterName

$policyFile = Get-RequiredFile $PolicyPath 'actual-exposure policy'
$policyHash = Assert-Hash $policyFile $PolicySha256 'actual-exposure policy'
if ([IO.Path]::GetFileName($policyFile) -match '(?i)\.template\.') {
    throw 'Nominal policy templates are not field-ready; create a same-session measured policy.'
}
$policy = [IO.File]::ReadAllText($policyFile) | ConvertFrom-Json
$requiredScoutPolicyProperties = @(
    'ScoutShortExposureSeconds', 'ScoutLongExposureSeconds',
    'MaximumProjectedBackgroundAdu', 'MinimumProjectedSkyAbovePedestalAdu',
    'MaximumProjectedGradientAdu', 'MaximumImpliedPedestalDeltaAdu',
    'ExpectedPedestalAdu', 'MaximumProjectedResidualSaturatedFraction',
    'MaximumScoutResidualSaturatedFraction', 'MaximumExemptBrightSources',
    'MaximumExemptSourceAreaPixels', 'MaximumExemptBrightSourceFraction',
    'MaximumEvidenceAgeMinutes', 'MaximumScoutSeparationMinutes',
    'RequiredGain', 'RequiredOffset', 'RequiredBinning')
$declaredPolicyProperties = @($policy.PSObject.Properties.Name)
$missingScoutPolicyProperties = @($requiredScoutPolicyProperties |
    Where-Object { $_ -notin $declaredPolicyProperties })
if ($missingScoutPolicyProperties.Count -gt 0) {
    throw "Policy omits explicit saturation-scout fields: $($missingScoutPolicyProperties -join ', ')."
}
if ([int]$policy.SchemaVersion -ne 1 -or [string]$policy.OpticalTrainId -ne $OpticalTrainId) {
    throw 'Policy schema or optical-train identity is wrong.'
}
if ([string]::IsNullOrWhiteSpace($RequiredFilterName) -or
        $RequiredFilterName -match '^(?i:none|unknown|--|REPLACE-)') {
    throw 'Required filter identity is empty, sentinel, or nominal.'
}
if ([string]$policy.RequiredFilterName -cne $RequiredFilterName -or
        $RequiredFilterName -cnotin @($policy.AllowedFilterNames)) {
    throw 'Policy does not bind and allow the exact required filter.'
}
$policyScale = [double]$policy.PixelScaleArcsecondsPerPixel
$scaleError = [Math]::Abs($policyScale - $MeasuredPixelScaleArcsecondsPerPixel) /
    $MeasuredPixelScaleArcsecondsPerPixel
if (-not [double]::IsFinite($policyScale) -or $policyScale -le 0.0 -or
        $scaleError -gt $MaximumPolicyScaleRelativeError) {
    throw "Policy pixel scale does not reproduce the same-session measurement (relative error=$scaleError)."
}
if ([int]$policy.MinimumPreControlFrames -lt 5 -or
        [int]$policy.MinimumPostControlFrames -lt 5 -or
        [double]$policy.ExpectedLongExposureSeconds -ne 900.0) {
    throw 'Policy does not require the complete 5 + 900 s + 5 bracket.'
}

$shortScout = Get-RequiredFile $ShortScoutFitsPath 'short saturation scout FITS'
$shortScoutHash = Assert-Hash $shortScout $ShortScoutFitsSha256 'short saturation scout FITS'
$longScout = Get-RequiredFile $LongScoutFitsPath 'long saturation scout FITS'
$longScoutHash = Assert-Hash $longScout $LongScoutFitsSha256 'long saturation scout FITS'
$scoutReceiptFile = Get-RequiredFile $SaturationScoutReceiptPath 'saturation scout receipt'
$scoutReceiptHash = Assert-Hash $scoutReceiptFile $SaturationScoutReceiptSha256 'saturation scout receipt'
$scoutReceiptJson = [IO.File]::ReadAllText($scoutReceiptFile)
$scoutDocument = [Text.Json.JsonDocument]::Parse($scoutReceiptJson)
try {
    $scoutGeneratedText = $scoutDocument.RootElement.GetProperty('GeneratedUtc').GetString()
} finally { $scoutDocument.Dispose() }
$scoutReceipt = $scoutReceiptJson | ConvertFrom-Json
if ([int]$scoutReceipt.SchemaVersion -ne 1 -or
        [string]$scoutReceipt.Verdict -cne 'PASS' -or
        [string]$scoutReceipt.OpticalTrainId -cne $OpticalTrainId -or
        [string]$scoutReceipt.RequiredFilterName -cne $RequiredFilterName -or
        [string]$scoutReceipt.ShortFitsSha256 -ine $shortScoutHash -or
        [string]$scoutReceipt.LongFitsSha256 -ine $longScoutHash -or
        [double]$scoutReceipt.TargetExposureSeconds -ne 900.0 -or
        [bool]$scoutReceipt.GrantsSequenceStartAuthority -or
        [bool]$scoutReceipt.GrantsMotionAuthority -or
        [bool]$scoutReceipt.PredictsGuidingOrStarShapeSuccess -or
        @($scoutReceipt.Issues).Count -ne 0) {
    throw 'Saturation scout receipt is non-passing, mismatched, or exceeds its authority boundary.'
}
$scoutGeneratedUtc = [DateTimeOffset]::Parse($scoutGeneratedText,
    $invariant, [Globalization.DateTimeStyles]::AssumeUniversal -bor
        [Globalization.DateTimeStyles]::AdjustToUniversal)
Assert-Fresh $scoutGeneratedUtc 'saturation scout receipt'
$scoutReproduction = Join-Path ([IO.Path]::GetDirectoryName($scoutReceiptFile)) `
    ('.readiness-scout-' + [Guid]::NewGuid().ToString('N') + '.json')
try {
    & (Join-Path $PSScriptRoot 'new_actual_exposure_saturation_scout.ps1') `
        -ShortScoutFits $shortScout -LongScoutFits $longScout `
        -PolicyPath $policyFile -OutputPath $scoutReproduction `
        -NowUtc $scoutGeneratedUtc | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Saturation scout reproduction did not pass.' }
    if ([Convert]::ToBase64String([IO.File]::ReadAllBytes($scoutReceiptFile)) -ne
            [Convert]::ToBase64String([IO.File]::ReadAllBytes($scoutReproduction))) {
        throw 'Saturation scout receipt does not reproduce exactly from FITS and policy sources.'
    }
} finally {
    if ([IO.File]::Exists($scoutReproduction)) { [IO.File]::Delete($scoutReproduction) }
}

$geometryFile = Get-RequiredFile $OagGeometryReceiptPath 'OAG geometry receipt'
$geometryHash = Assert-Hash $geometryFile $OagGeometryReceiptSha256 'OAG geometry receipt'
$geometry = [IO.File]::ReadAllText($geometryFile) | ConvertFrom-Json
if ([int]$geometry.SchemaVersion -ne 2 -or
        [string]$geometry.GeometryProvenance -ne 'derived-astap-wcs' -or
        [bool]$geometry.GrantsMotionAuthority -or
        [bool]$geometry.GrantsAbsolutePolarAccuracyClaim) {
    throw 'OAG geometry receipt identity or authority boundary is invalid.'
}
Assert-Fresh ([DateTimeOffset]::Parse([string]$geometry.MainObservationUtc, $invariant)) `
    'Main-camera geometry observation'
Assert-Fresh ([DateTimeOffset]::Parse([string]$geometry.GuideObservationUtc, $invariant)) `
    'Guide-camera geometry observation'
$geometryDirectory = [IO.Path]::GetDirectoryName($geometryFile)
$mainSource = [IO.Path]::GetFullPath((Join-Path $geometryDirectory ([string]$geometry.MainSolutionSource)))
$guideSource = [IO.Path]::GetFullPath((Join-Path $geometryDirectory ([string]$geometry.GuideSolutionSource)))
$reproduction = Join-Path $geometryDirectory ('.readiness-' + [Guid]::NewGuid().ToString('N') + '.json')
try {
    $arguments = @{
        MainWcsPath = $mainSource
        GuideWcsPath = $guideSource
        OutputPath = $reproduction
        MainSolutionSha256 = [string]$geometry.MainSolutionSha256
        GuideSolutionSha256 = [string]$geometry.GuideSolutionSha256
    }
    if ($null -ne $geometry.GuideLockOffsetXFromCenterPixels -or
            $null -ne $geometry.GuideLockOffsetYFromCenterPixels) {
        if ($null -eq $geometry.GuideLockOffsetXFromCenterPixels -or
                $null -eq $geometry.GuideLockOffsetYFromCenterPixels) {
            throw 'OAG geometry receipt has only one guide-lock coordinate.'
        }
        $arguments.GuideLockOffsetXFromCenterPixels =
            [double]$geometry.GuideLockOffsetXFromCenterPixels
        $arguments.GuideLockOffsetYFromCenterPixels =
            [double]$geometry.GuideLockOffsetYFromCenterPixels
    }
    & (Join-Path $PSScriptRoot 'new_derived_oag_geometry_receipt.ps1') @arguments | Out-Null
    $declaredBytes = [Convert]::ToBase64String(
        [IO.File]::ReadAllBytes($geometryFile))
    $reproducedBytes = [Convert]::ToBase64String(
        [IO.File]::ReadAllBytes($reproduction))
    if ($declaredBytes -ne $reproducedBytes) {
        throw 'OAG geometry receipt does not reproduce exactly from its WCS sources.'
    }
} finally {
    if ([IO.File]::Exists($reproduction)) { [IO.File]::Delete($reproduction) }
}

$qualificationFile = Get-RequiredFile $OperationalQualificationReportPath `
    'TPPA operational qualification report'
$qualificationHash = Assert-Hash $qualificationFile `
    $OperationalQualificationReportSha256 'TPPA operational qualification report'
$qualification = [IO.File]::ReadAllText($qualificationFile) | ConvertFrom-Json
if ([int]$qualification.SchemaVersion -ne 1 -or
        -not [bool]$qualification.SpeedAndInternalConsistencyQualified -or
        [int]$qualification.RequiredConsecutiveRuns -lt 3 -or
        [int]$qualification.PassingConsecutivePrefix -lt
            [int]$qualification.RequiredConsecutiveRuns) {
    throw 'TPPA operational qualification did not establish a passing consecutive run set.'
}
if ([double]$qualification.MaximumReportedTotalMinutes -gt $MaximumPaErrorMinutes) {
    throw 'TPPA operational report used a weaker polar-error ceiling than this readiness gate.'
}
Assert-Fresh ([DateTimeOffset]::Parse([string]$qualification.GeneratedUtc, $invariant)) `
    'TPPA operational qualification report'
foreach ($run in @($qualification.Runs)) {
    if (-not [bool]$run.Passed) { throw 'TPPA operational report contains a failed run.' }
    foreach ($total in @($run.ReportedTotalsArcmin)) {
        if ([double]$total -gt $MaximumPaErrorMinutes) {
            throw "TPPA reported total $total arcmin exceeds the readiness ceiling."
        }
    }
}

$outputFull = [IO.Path]::GetFullPath($OutputPath)
if ([IO.File]::Exists($outputFull)) { throw "Refusing to overwrite readiness receipt: $outputFull" }
[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($outputFull))
$receipt = [ordered]@{
    SchemaVersion = 1
    GeneratedUtc = $NowUtc.ToUniversalTime().ToString('O', $invariant)
    OpticalTrainId = $OpticalTrainId
    ReadyForGuided900SecondAcquisition = $true
    PluginVersion = [string]$runtime.PluginVersion
    PluginSourceCommit = [string]$runtime.SourceCommit
    RuntimeManifestSha256 = [string]$runtime.RuntimeManifestSha256
    SequenceSha256 = $sequenceHash
    PolicySha256 = $policyHash
    RequiredFilterName = $RequiredFilterName
    ShortScoutFitsSha256 = $shortScoutHash
    LongScoutFitsSha256 = $longScoutHash
    SaturationScoutReceiptSha256 = $scoutReceiptHash
    ScoutMedianProjectedBackgroundAdu = [double]$scoutReceipt.MedianProjectedBackgroundAdu
    ScoutProjectedResidualSaturatedFraction = [double]$scoutReceipt.ProjectedResidualSaturatedFraction
    MeasuredPixelScaleArcsecondsPerPixel = $MeasuredPixelScaleArcsecondsPerPixel
    PolicyPixelScaleArcsecondsPerPixel = $policyScale
    OagGeometryReceiptSha256 = $geometryHash
    GuideToFarthestMainCornerUpperBoundPixels =
        [double]$geometry.GuideToFarthestMainCornerUpperBoundPixels
    OperationalQualificationReportSha256 = $qualificationHash
    MaximumPaErrorMinutes = $MaximumPaErrorMinutes
    GrantsEquipmentConnectionAuthority = $false
    GrantsSequenceStartAuthority = $false
    GrantsMotionAuthority = $false
    GrantsAbsolutePolarAccuracyClaim = $false
}
$bytes = [Text.UTF8Encoding]::new($false).GetBytes(
    ($receipt | ConvertTo-Json -Depth 4) + "`r`n")
$stream = [IO.File]::Open($outputFull, [IO.FileMode]::CreateNew,
    [IO.FileAccess]::Write, [IO.FileShare]::Read)
try { $stream.Write($bytes, 0, $bytes.Length); $stream.Flush($true) } finally { $stream.Dispose() }
[pscustomobject]@{
    OutputPath = $outputFull
    Sha256 = (Get-FileHash -LiteralPath $outputFull -Algorithm SHA256).Hash
    OpticalTrainId = $OpticalTrainId
    Ready = $true
    PluginVersion = [string]$runtime.PluginVersion
    GuideToCornerPixels = [double]$geometry.GuideToFarthestMainCornerUpperBoundPixels
    MaximumPaErrorMinutes = $MaximumPaErrorMinutes
}
