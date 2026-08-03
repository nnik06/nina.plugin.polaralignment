#requires -Version 7.5
param(
    [Parameter(Mandatory = $true)]
    [string[]]$ReceiptPath,
    [ValidateRange(3, 20)]
    [int]$ReferenceSamples = 3,
    [ValidateRange(3, 20)]
    [int]$MinimumStableSamples = 4,
    [ValidateRange(0.1, 10.0)]
    [double]$MaximumRadialResidualArcsec = 1.5,
    [ValidateRange(0.001, 1.0)]
    [double]$MaximumPositionAngleResidualDegrees = 0.05,
    [ValidateRange(0.0001, 0.1)]
    [double]$MaximumPixelScaleFraction = 0.005,
    [ValidateRange(1.0, 10.0)]
    [double]$SafetyMultiplier = 3.0,
    [ValidateRange(5.0, 30.0)]
    [double]$MinimumRecommendedSettleSeconds = 10.0,
    [ValidateRange(10.0, 60.0)]
    [double]$MaximumQualifiedSettleSeconds = 30.0,
    [ValidateRange(2, 100)]
    [int]$MinimumRuns = 10,
    [ValidateRange(1, 20)]
    [int]$MinimumDubaiNights = 2,
    [ValidateRange(1, 20)]
    [int]$MinimumRunsPerDirection = 3,
    [ValidateRange(1, 20)]
    [int]$MinimumRunsPerDubaiNight = 4,
    [ValidateRange(20.0, 180.0)]
    [double]$MinimumObservationSpanSeconds = 50.0,
    [ValidateRange(3.0, 30.0)]
    [double]$MaximumSamplingGapSeconds = 10.0
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

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
    if ([Math]::Truncate($number) -ne $number -or $number -lt [int]::MinValue -or $number -gt [int]::MaxValue) {
        throw "Field '$FieldName' is not a JSON integer."
    }
    return [int]$number
}

function ConvertTo-StrictBoolean([object]$Value, [string]$FieldName) {
    if ($Value -isnot [bool]) { throw "Field '$FieldName' is not a JSON boolean." }
    return [bool]$Value
}

function ConvertTo-ObservedTime([object]$Value, [string]$FieldName) {
    if ($Value -isnot [string] -or [string]::IsNullOrWhiteSpace($Value)) {
        throw "Field '$FieldName' is not a timestamp string."
    }
    if ($Value -notmatch '(Z|[+-]\d{2}:\d{2})$') {
        throw "Field '$FieldName' lacks an explicit UTC offset."
    }
    return [DateTimeOffset]::Parse(
        $Value,
        [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::RoundtripKind)
}

function Get-PropertyValue([object]$Object, [string]$Name) {
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { throw "Missing field '$Name'." }
    return $property.Value
}

function Get-SignedCircularDegreesDelta([double]$Value, [double]$Reference) {
    $delta = (($Value - $Reference + 540.0) % 360.0) - 180.0
    if ($delta -le -180.0) { return 180.0 }
    return $delta
}

function Get-EquatorialSeparationDegrees(
        [double]$FirstRaHours,
        [double]$FirstDecDegrees,
        [double]$SecondRaHours,
        [double]$SecondDecDegrees) {
    $ra1 = ($FirstRaHours * 15.0) * [Math]::PI / 180.0
    $ra2 = ($SecondRaHours * 15.0) * [Math]::PI / 180.0
    $dec1 = $FirstDecDegrees * [Math]::PI / 180.0
    $dec2 = $SecondDecDegrees * [Math]::PI / 180.0
    $cosine = [Math]::Sin($dec1) * [Math]::Sin($dec2) +
        [Math]::Cos($dec1) * [Math]::Cos($dec2) * [Math]::Cos($ra2 - $ra1)
    return [Math]::Acos([Math]::Max(-1.0, [Math]::Min(1.0, $cosine))) * 180.0 / [Math]::PI
}

function Get-Median([double[]]$Values) {
    if ($Values.Count -eq 0) { throw 'Cannot calculate a median from no values.' }
    $sorted = @($Values | Sort-Object)
    $middle = [int][Math]::Floor($sorted.Count / 2)
    if (($sorted.Count % 2) -eq 1) { return [double]$sorted[$middle] }
    return ([double]$sorted[$middle - 1] + [double]$sorted[$middle]) / 2.0
}

function Get-DubaiObservingNight([DateTimeOffset]$Timestamp) {
    return $Timestamp.ToOffset([TimeSpan]::FromHours(4)).AddHours(-12).Date.ToString('yyyy-MM-dd')
}

$runResults = [Collections.Generic.List[object]]::new()
foreach ($path in $ReceiptPath) {
    $issues = [Collections.Generic.List[string]]::new()
    try {
        $receipt = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -DateKind String
        if ((ConvertTo-StrictInt (Get-PropertyValue $receipt 'SchemaVersion') 'SchemaVersion') -ne 1) {
            $issues.Add('SchemaVersion is not 1')
        }
        if ([string](Get-PropertyValue $receipt 'Event') -ne 'tppa-settle-probe') {
            $issues.Add('Event is not tppa-settle-probe')
        }
        $runId = [string](Get-PropertyValue $receipt 'RunId')
        $rigConfigurationId = [string](Get-PropertyValue $receipt 'RigConfigurationId')
        if ([string]::IsNullOrWhiteSpace($runId)) { $issues.Add('RunId is empty') }
        if ([string]::IsNullOrWhiteSpace($rigConfigurationId)) { $issues.Add('RigConfigurationId is empty') }
        $startedUtc = ConvertTo-ObservedTime (Get-PropertyValue $receipt 'StartedUtc') 'StartedUtc'
        $slewCompletedUtc = ConvertTo-ObservedTime (Get-PropertyValue $receipt 'SlewCompletedUtc') 'SlewCompletedUtc'
        $completedUtc = ConvertTo-ObservedTime (Get-PropertyValue $receipt 'CompletedUtc') 'CompletedUtc'
        if ($slewCompletedUtc -lt $startedUtc -or $completedUtc -lt $slewCompletedUtc) {
            $issues.Add('Lifecycle timestamps are out of order')
        }
        $slewDistance = ConvertTo-FiniteDouble (Get-PropertyValue $receipt 'SlewDistanceDegrees') 'SlewDistanceDegrees'
        if ($slewDistance -lt 5.0) { $issues.Add("Slew distance $slewDistance deg is below 5 deg") }
        $raDeltaDegrees = ConvertTo-FiniteDouble (Get-PropertyValue $receipt 'RaDeltaDegrees') 'RaDeltaDegrees'
        if ([Math]::Abs($raDeltaDegrees) -lt 2.0) { $issues.Add("RA component $raDeltaDegrees deg is below 2 deg") }
        $direction = [string](Get-PropertyValue $receipt 'SlewDirection')
        if ($direction -notin @('IncreasingRA', 'DecreasingRA')) { $issues.Add("Invalid slew direction '$direction'") }
        $expectedDirection = if ($raDeltaDegrees -gt 0.0) { 'IncreasingRA' } else { 'DecreasingRA' }
        if ($direction -ne $expectedDirection) { $issues.Add("Slew direction '$direction' contradicts RA delta $raDeltaDegrees deg") }
        $preMount = Get-PropertyValue $receipt 'PreMount'
        $postMount = Get-PropertyValue $receipt 'PostMount'
        $preRaHours = ConvertTo-FiniteDouble (Get-PropertyValue $preMount 'RightAscension') 'PreMount.RightAscension'
        $preDec = ConvertTo-FiniteDouble (Get-PropertyValue $preMount 'Declination') 'PreMount.Declination'
        $postRaHours = ConvertTo-FiniteDouble (Get-PropertyValue $postMount 'RightAscension') 'PostMount.RightAscension'
        $postDec = ConvertTo-FiniteDouble (Get-PropertyValue $postMount 'Declination') 'PostMount.Declination'
        $recomputedRaDelta = Get-SignedCircularDegreesDelta ($postRaHours * 15.0) ($preRaHours * 15.0)
        $recomputedDistance = Get-EquatorialSeparationDegrees $preRaHours $preDec $postRaHours $postDec
        if ([Math]::Abs($recomputedRaDelta - $raDeltaDegrees) -gt 0.05) {
            $issues.Add('Reported RA delta disagrees with pre/post mount states')
        }
        if ([Math]::Abs($recomputedDistance - $slewDistance) -gt 0.05) {
            $issues.Add('Reported slew distance disagrees with pre/post mount states')
        }
        $exposure = ConvertTo-FiniteDouble (Get-PropertyValue $receipt 'ExposureSeconds') 'ExposureSeconds'
        $cadence = ConvertTo-FiniteDouble (Get-PropertyValue $receipt 'CadenceSeconds') 'CadenceSeconds'
        if ($exposure -le 0.0 -or $exposure -gt 3.0) { $issues.Add("Exposure $exposure s is outside (0,3]") }
        if ($cadence -lt 3.0 -or $cadence -gt 10.0) { $issues.Add("Cadence $cadence s is outside [3,10]") }

        $samples = @((Get-PropertyValue $receipt 'Samples'))
        if ($samples.Count -lt [Math]::Max($ReferenceSamples, $MinimumStableSamples)) {
            $issues.Add("Only $($samples.Count) samples were supplied")
        }
        $validated = [Collections.Generic.List[object]]::new()
        $previousElapsed = -1.0
        $firstElapsed = $null
        $lastElapsed = $null
        $flippedStates = [Collections.Generic.HashSet[bool]]::new()
        foreach ($sample in $samples) {
            $elapsed = ConvertTo-FiniteDouble (Get-PropertyValue $sample 'ElapsedFromSlewCompletedSeconds') 'sample.elapsed'
            $ra = ConvertTo-FiniteDouble (Get-PropertyValue $sample 'SolveRaDegreesJ2000') 'sample.ra'
            $dec = ConvertTo-FiniteDouble (Get-PropertyValue $sample 'SolveDecDegreesJ2000') 'sample.dec'
            $scale = ConvertTo-FiniteDouble (Get-PropertyValue $sample 'PixelScaleArcsec') 'sample.scale'
            $angle = ConvertTo-FiniteDouble (Get-PropertyValue $sample 'PositionAngleDegrees') 'sample.angle'
            $captureStarted = ConvertTo-ObservedTime (Get-PropertyValue $sample 'CaptureStartedUtc') 'sample.startedUtc'
            $captureCompleted = ConvertTo-ObservedTime (Get-PropertyValue $sample 'CaptureCompletedUtc') 'sample.completedUtc'
            if ($captureStarted -lt $slewCompletedUtc -or $captureCompleted -le $captureStarted -or $captureCompleted -gt $completedUtc) {
                $issues.Add('Sample timestamps fall outside the probe lifecycle')
            }
            if ($elapsed -lt 0.0) { $issues.Add('A sample elapsed time is negative') }
            if ($elapsed -le $previousElapsed) {
                $issues.Add('Sample elapsed times are not strictly increasing')
            } elseif ($previousElapsed -ge 0.0 -and ($elapsed - $previousElapsed) -gt $MaximumSamplingGapSeconds) {
                $issues.Add("Sample gap $($elapsed - $previousElapsed) s exceeds $MaximumSamplingGapSeconds s")
            }
            if ([Math]::Abs(($captureCompleted - $slewCompletedUtc).TotalSeconds - $elapsed) -gt 1.0) {
                $issues.Add('Sample elapsed time disagrees with wall clock by more than 1 second')
            }
            $mountSlewing = ConvertTo-StrictBoolean (Get-PropertyValue $sample 'MountSlewing') 'sample.mountSlewing'
            $mountTracking = ConvertTo-StrictBoolean (Get-PropertyValue $sample 'MountTracking') 'sample.mountTracking'
            $flipped = ConvertTo-StrictBoolean (Get-PropertyValue $sample 'Flipped') 'sample.flipped'
            [void]$flippedStates.Add($flipped)
            if ($mountSlewing -or -not $mountTracking) {
                $issues.Add('A sample was captured while the mount was slewing or not tracking')
            }
            $azimuth = ConvertTo-FiniteDouble (Get-PropertyValue $sample 'MountAzimuthDegrees') 'sample.mountAzimuth'
            $altitude = ConvertTo-FiniteDouble (Get-PropertyValue $sample 'MountAltitudeDegrees') 'sample.mountAltitude'
            $azimuthSafe = ($azimuth -ge 270.0 -and $azimuth -le 360.0) -or ($azimuth -ge 0.0 -and $azimuth -le 10.0)
            if (-not $azimuthSafe -or $altitude -lt 25.0 -or $altitude -gt 55.0) {
                $issues.Add('A sample was outside the balcony pointing envelope')
            }
            $validated.Add([pscustomobject]@{ Elapsed=$elapsed; RA=$ra; Dec=$dec; Scale=$scale; Angle=$angle; Flipped=$flipped })
            if ($null -eq $firstElapsed) { $firstElapsed = $elapsed }
            $lastElapsed = $elapsed
            $previousElapsed = $elapsed
        }
        if ($null -ne $firstElapsed -and $firstElapsed -gt $MaximumSamplingGapSeconds) {
            $issues.Add("First completed solve at $firstElapsed s exceeds $MaximumSamplingGapSeconds s")
        }
        if ($null -ne $firstElapsed -and $null -ne $lastElapsed -and
                ($lastElapsed - $firstElapsed) -lt $MinimumObservationSpanSeconds) {
            $issues.Add("Observation span $($lastElapsed - $firstElapsed) s is below $MinimumObservationSpanSeconds s")
        }
        if ($flippedStates.Count -ne 1) { $issues.Add('Plate-solve flipped state changed within the probe') }

        $stableOnset = $null
        $recommended = $null
        $sampleDiagnostics = @()
        if ($validated.Count -ge [Math]::Max($ReferenceSamples, $MinimumStableSamples)) {
            $reference = @($validated | Select-Object -Last $ReferenceSamples)
            $referenceRaAnchor = [double]$reference[-1].RA
            $referenceRa = $referenceRaAnchor + (Get-Median ([double[]]@($reference | ForEach-Object {
                Get-SignedCircularDegreesDelta ([double]$_.RA) $referenceRaAnchor
            })))
            $referenceDec = Get-Median ([double[]]@($reference.Dec))
            $referenceScale = Get-Median ([double[]]@($reference.Scale))
            $referenceAngleAnchor = [double]$reference[-1].Angle
            $referenceAngle = $referenceAngleAnchor + (Get-Median ([double[]]@($reference | ForEach-Object {
                Get-SignedCircularDegreesDelta ([double]$_.Angle) $referenceAngleAnchor
            })))
            if ($referenceScale -le 0.0) { $issues.Add('Reference pixel scale is not positive') }

            $cosDec = [Math]::Cos($referenceDec * [Math]::PI / 180.0)
            $sampleDiagnostics = @($validated | ForEach-Object {
                $deltaRa = (Get-SignedCircularDegreesDelta ([double]$_.RA) $referenceRa) * $cosDec * 3600.0
                $deltaDec = ([double]$_.Dec - $referenceDec) * 3600.0
                [pscustomobject]@{
                    ElapsedSeconds = [double]$_.Elapsed
                    RadialResidualArcsec = [Math]::Sqrt($deltaRa * $deltaRa + $deltaDec * $deltaDec)
                    PositionAngleResidualDegrees = [Math]::Abs((Get-SignedCircularDegreesDelta ([double]$_.Angle) $referenceAngle))
                    PixelScaleFraction = if ($referenceScale -gt 0.0) {
                        [Math]::Abs(([double]$_.Scale - $referenceScale) / $referenceScale)
                    } else { [double]::PositiveInfinity }
                }
            })
            foreach ($candidate in $sampleDiagnostics) {
                $tail = @($sampleDiagnostics | Where-Object { $_.ElapsedSeconds -ge $candidate.ElapsedSeconds })
                if ($tail.Count -lt $MinimumStableSamples) { continue }
                $tailPasses = @($tail | Where-Object {
                    $_.RadialResidualArcsec -le $MaximumRadialResidualArcsec -and
                    $_.PositionAngleResidualDegrees -le $MaximumPositionAngleResidualDegrees -and
                    $_.PixelScaleFraction -le $MaximumPixelScaleFraction
                }).Count -eq $tail.Count
                if ($tailPasses) {
                    $stableOnset = [double]$candidate.ElapsedSeconds
                    $recommended = [Math]::Max(
                        $MinimumRecommendedSettleSeconds,
                        [Math]::Ceiling($stableOnset * $SafetyMultiplier))
                    break
                }
            }
            if ($null -eq $stableOnset) { $issues.Add('No sustained stable tail was found') }
        }

        $runResults.Add([pscustomobject][ordered]@{
            Path = [IO.Path]::GetFullPath($path)
            RunId = $runId
            RigConfigurationId = $rigConfigurationId
            DubaiNight = Get-DubaiObservingNight $startedUtc
            SlewDirection = $direction
            SlewDistanceDegrees = $slewDistance
            StableOnsetSeconds = $stableOnset
            RecommendedSettleSeconds = $recommended
            Valid = $issues.Count -eq 0
            Issues = $issues.ToArray()
            Samples = $sampleDiagnostics
        })
    } catch {
        $issues.Add($_.Exception.Message)
        $runResults.Add([pscustomobject][ordered]@{
            Path = [IO.Path]::GetFullPath($path)
            RunId = $null
            RigConfigurationId = $null
            DubaiNight = $null
            SlewDirection = $null
            SlewDistanceDegrees = $null
            StableOnsetSeconds = $null
            RecommendedSettleSeconds = $null
            Valid = $false
            Issues = $issues.ToArray()
            Samples = @()
        })
    }
}

$validRuns = @($runResults | Where-Object Valid)
$rigConfigurations = @($validRuns.RigConfigurationId | Select-Object -Unique)
$runIds = @($validRuns.RunId)
$distinctRunIds = @($runIds | Select-Object -Unique)
$nights = @($validRuns.DubaiNight | Select-Object -Unique)
$directions = @($validRuns.SlewDirection | Select-Object -Unique)
$campaignIssues = [Collections.Generic.List[string]]::new()
if (@($runResults | Where-Object { -not $_.Valid }).Count -gt 0) { $campaignIssues.Add('One or more receipts are invalid') }
if ($validRuns.Count -lt $MinimumRuns) { $campaignIssues.Add("Only $($validRuns.Count) valid runs; $MinimumRuns required") }
if ($distinctRunIds.Count -ne $runIds.Count) { $campaignIssues.Add('Duplicate RunId values are not independent evidence') }
if ($rigConfigurations.Count -ne 1) { $campaignIssues.Add('Campaign must contain exactly one rig configuration') }
if ($nights.Count -lt $MinimumDubaiNights) { $campaignIssues.Add("Only $($nights.Count) Dubai nights; $MinimumDubaiNights required") }
foreach ($requiredDirection in @('IncreasingRA', 'DecreasingRA')) {
    $directionCount = @($validRuns | Where-Object SlewDirection -eq $requiredDirection).Count
    if ($directionCount -lt $MinimumRunsPerDirection) {
        $campaignIssues.Add("Only $directionCount $requiredDirection slew probes; $MinimumRunsPerDirection required")
    }
}
foreach ($night in $nights) {
    $nightCount = @($validRuns | Where-Object DubaiNight -eq $night).Count
    if ($nightCount -lt $MinimumRunsPerDubaiNight) {
        $campaignIssues.Add("Only $nightCount runs on Dubai night $night; $MinimumRunsPerDubaiNight required")
    }
}

$candidateSettle = if ($validRuns.Count -gt 0) {
    [double](($validRuns.RecommendedSettleSeconds | Measure-Object -Maximum).Maximum)
} else { $null }
if ($null -ne $candidateSettle -and $candidateSettle -gt $MaximumQualifiedSettleSeconds) {
    $campaignIssues.Add("Campaign recommendation $candidateSettle s exceeds qualified ceiling $MaximumQualifiedSettleSeconds s")
}
$qualified = $campaignIssues.Count -eq 0

[pscustomobject][ordered]@{
    SchemaVersion = 1
    GeneratedUtc = [DateTimeOffset]::UtcNow.ToString('O')
    MinimumRuns = $MinimumRuns
    MinimumDubaiNights = $MinimumDubaiNights
    MinimumRunsPerDirection = $MinimumRunsPerDirection
    MinimumRunsPerDubaiNight = $MinimumRunsPerDubaiNight
    MinimumObservationSpanSeconds = $MinimumObservationSpanSeconds
    MaximumSamplingGapSeconds = $MaximumSamplingGapSeconds
    ReferenceSamples = $ReferenceSamples
    MinimumStableSamples = $MinimumStableSamples
    MaximumRadialResidualArcsec = $MaximumRadialResidualArcsec
    MaximumPositionAngleResidualDegrees = $MaximumPositionAngleResidualDegrees
    MaximumPixelScaleFraction = $MaximumPixelScaleFraction
    SafetyMultiplier = $SafetyMultiplier
    MinimumRecommendedSettleSeconds = $MinimumRecommendedSettleSeconds
    MaximumQualifiedSettleSeconds = $MaximumQualifiedSettleSeconds
    RigConfigurationId = if ($rigConfigurations.Count -eq 1) { $rigConfigurations[0] } else { $null }
    ValidRunCount = $validRuns.Count
    DubaiNightCount = $nights.Count
    SlewDirections = $directions
    CandidateSettleSeconds = $candidateSettle
    RecommendedSettleSeconds = if ($qualified) { $candidateSettle } else { $null }
    Qualified = $qualified
    ScopeNote = 'This campaign qualifies post-slew plate-solve settling for one named rig configuration. It does not prove polar-alignment accuracy or authorize UPAS motion.'
    Issues = $campaignIssues.ToArray()
    Runs = $runResults.ToArray()
}
