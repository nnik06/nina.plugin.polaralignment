param(
    [Parameter(Mandatory = $true)]
    [string]$RunDirectory,
    [Parameter(Mandatory = $true)]
    [string]$GeometryReceiptPath,
    [ValidateRange(300.0, 3600.0)]
    [double]$ExposureSeconds = 900.0,
    [ValidateRange(0.01, 10.0)]
    [double]$AllowedSmearPixels = 0.5,
    [ValidateRange(10, 200)]
    [int]$MinimumSamples = 20,
    [ValidateRange(10.0, 120.0)]
    [double]$MinimumSpanMinutes = 20.0,
    [ValidateRange(5.0, 30.0)]
    [double]$WindowMinutes = 10.0,
    [ValidateRange(0.5, 10.0)]
    [double]$MinimumPairSeparationMinutes = 2.0,
    [ValidateRange(0.001, 2.0)]
    [double]$MaximumAdjacentPositionAngleDeltaDegrees = 0.1,
    [ValidateRange(0.1, 60.0)]
    [double]$PositionAngleNoiseFloorArcseconds = 1.0,
    [ValidateRange(0.01, 5.0)]
    [double]$MaximumWindowDisagreementSmearPixels = 0.25,
    [ValidateSet('Object', 'Json')]
    [string]$OutputFormat = 'Object'
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

function Get-Sha256([string]$Path) {
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-NormalizedRelativePath([string]$Path) {
    ([string]$Path).Replace('\', '/').TrimStart('/')
}

function Get-Median([double[]]$Values) {
    if ($null -eq $Values -or $Values.Count -eq 0) {
        throw 'Cannot calculate a median from an empty collection.'
    }
    $ordered = @($Values | Sort-Object)
    $middle = [int][Math]::Floor($ordered.Count / 2.0)
    if (($ordered.Count % 2) -eq 1) {
        return [double]$ordered[$middle]
    }
    ([double]$ordered[$middle - 1] + [double]$ordered[$middle]) / 2.0
}

function Get-SignedCircularDeltaDegrees([double]$Value, [double]$Reference) {
    $delta = (($Value - $Reference + 540.0) % 360.0) - 180.0
    if ($delta -le -180.0) { return 180.0 }
    $delta
}

function Get-DotProduct([double[]]$Left, [double[]]$Right) {
    $Left[0] * $Right[0] + $Left[1] * $Right[1] + $Left[2] * $Right[2]
}

function Get-CrossProduct([double[]]$Left, [double[]]$Right) {
    [double[]]@(
        ($Left[1] * $Right[2] - $Left[2] * $Right[1]),
        ($Left[2] * $Right[0] - $Left[0] * $Right[2]),
        ($Left[0] * $Right[1] - $Left[1] * $Right[0]))
}

function Get-NormalizedVector([double[]]$Vector, [string]$Label) {
    $norm = [Math]::Sqrt((Get-DotProduct $Vector $Vector))
    if ([double]::IsNaN($norm) -or [double]::IsInfinity($norm) -or
            $norm -le 1.0e-15) {
        throw "$Label is zero, non-finite, or ill-conditioned."
    }
    [double[]]@(($Vector[0] / $norm), ($Vector[1] / $norm), ($Vector[2] / $norm))
}

function Get-CelestialBasis([double]$RaDegrees, [double]$DecDegrees) {
    $ra = $RaDegrees * [Math]::PI / 180.0
    $dec = $DecDegrees * [Math]::PI / 180.0
    $cosRa = [Math]::Cos($ra)
    $sinRa = [Math]::Sin($ra)
    $cosDec = [Math]::Cos($dec)
    $sinDec = [Math]::Sin($dec)
    [pscustomobject]@{
        Center = [double[]]@(($cosDec * $cosRa), ($cosDec * $sinRa), $sinDec)
        North = [double[]]@((-$sinDec * $cosRa), (-$sinDec * $sinRa), $cosDec)
        East = [double[]]@(-$sinRa, $cosRa, 0.0)
    }
}

function Get-ImageUpVector(
        [double]$RaDegrees,
        [double]$DecDegrees,
        [double]$PositionAngleDegrees) {
    $basis = Get-CelestialBasis $RaDegrees $DecDegrees
    $angle = $PositionAngleDegrees * [Math]::PI / 180.0
    [double[]]@(
        ([Math]::Cos($angle) * $basis.North[0] + [Math]::Sin($angle) * $basis.East[0]),
        ([Math]::Cos($angle) * $basis.North[1] + [Math]::Sin($angle) * $basis.East[1]),
        ([Math]::Cos($angle) * $basis.North[2] + [Math]::Sin($angle) * $basis.East[2]))
}

function Move-TangentVectorToReference(
        [double[]]$Vector,
        [double[]]$FromCenter,
        [double[]]$ReferenceCenter) {
    $axisRaw = Get-CrossProduct $FromCenter $ReferenceCenter
    $axisNorm = [Math]::Sqrt((Get-DotProduct $axisRaw $axisRaw))
    $centerDot = [Math]::Max(-1.0, [Math]::Min(1.0,
        (Get-DotProduct $FromCenter $ReferenceCenter)))
    if ($axisNorm -le 1.0e-12) {
        if ($centerDot -lt 0.0) {
            throw 'Solve centres are antipodal; tangent-basis transport is ambiguous.'
        }
        return [double[]]@($Vector[0], $Vector[1], $Vector[2])
    }
    $axis = [double[]]@(
        ($axisRaw[0] / $axisNorm),
        ($axisRaw[1] / $axisNorm),
        ($axisRaw[2] / $axisNorm))
    $theta = [Math]::Atan2($axisNorm, $centerDot)
    if ([Math]::Abs([Math]::PI - $theta) -lt 1.0e-8) {
        throw 'Solve centres are too close to antipodal for unique tangent-basis transport.'
    }
    $axisCrossVector = Get-CrossProduct $axis $Vector
    $axisDotVector = Get-DotProduct $axis $Vector
    $cosTheta = [Math]::Cos($theta)
    $sinTheta = [Math]::Sin($theta)
    $rotated = [double[]]@(
        ($Vector[0] * $cosTheta + $axisCrossVector[0] * $sinTheta +
            $axis[0] * $axisDotVector * (1.0 - $cosTheta)),
        ($Vector[1] * $cosTheta + $axisCrossVector[1] * $sinTheta +
            $axis[1] * $axisDotVector * (1.0 - $cosTheta)),
        ($Vector[2] * $cosTheta + $axisCrossVector[2] * $sinTheta +
            $axis[2] * $axisDotVector * (1.0 - $cosTheta)))
    $radial = Get-DotProduct $rotated $ReferenceCenter
    Get-NormalizedVector ([double[]]@(
        ($rotated[0] - $radial * $ReferenceCenter[0]),
        ($rotated[1] - $radial * $ReferenceCenter[1]),
        ($rotated[2] - $radial * $ReferenceCenter[2]))) 'transported image axis'
}

function Get-TheilSenFit($Points, [double]$MinimumPairHours) {
    if ($Points.Count -lt 3) {
        throw 'A robust rotation fit requires at least three points.'
    }
    $slopes = [System.Collections.Generic.List[double]]::new()
    for ($left = 0; $left -lt $Points.Count - 1; $left++) {
        for ($right = $left + 1; $right -lt $Points.Count; $right++) {
            $deltaHours = [double]$Points[$right].ElapsedHours -
                [double]$Points[$left].ElapsedHours
            if ($deltaHours -ge $MinimumPairHours) {
                $slopes.Add(
                    ([double]$Points[$right].UnwrappedRotationDegrees -
                        [double]$Points[$left].UnwrappedRotationDegrees) /
                    $deltaHours)
            }
        }
    }
    if ($slopes.Count -eq 0) {
        throw 'No rotation-fit point pair satisfies the minimum separation.'
    }
    $slope = Get-Median @($slopes)
    $intercepts = @($Points | ForEach-Object {
        [double]$_.UnwrappedRotationDegrees -
            $slope * [double]$_.ElapsedHours
    })
    $intercept = Get-Median $intercepts
    $residuals = @($Points | ForEach-Object {
        [double]$_.UnwrappedRotationDegrees -
            ($intercept + $slope * [double]$_.ElapsedHours)
    })
    $residualMedian = Get-Median $residuals
    $absoluteDeviations = @($residuals | ForEach-Object {
        [Math]::Abs([double]$_ - $residualMedian)
    })
    $spanHours = [double]$Points[-1].ElapsedHours -
        [double]$Points[0].ElapsedHours
    if ($spanHours -le 0.0) {
        throw 'Rotation-fit points do not have a positive time span.'
    }
    [pscustomobject][ordered]@{
        SampleCount = $Points.Count
        SpanMinutes = $spanHours * 60.0
        SlopeDegreesPerHour = $slope
        InterceptDegrees = $intercept
        ResidualMadDegrees = Get-Median $absoluteDeviations
    }
}

function Get-SmearPixels(
        [double]$RateDegreesPerHour,
        [double]$Seconds,
        [double]$RadiusPixels) {
    $rotationRadians = [Math]::Abs($RateDegreesPerHour) *
        ($Seconds / 3600.0) * [Math]::PI / 180.0
    2.0 * $RadiusPixels * [Math]::Sin($rotationRadians / 2.0)
}

if (-not (Test-Path -LiteralPath $RunDirectory -PathType Container)) {
    throw "Synchronized run directory is missing: $RunDirectory"
}
if (-not (Test-Path -LiteralPath $GeometryReceiptPath -PathType Leaf)) {
    throw "OAG geometry receipt is missing: $GeometryReceiptPath"
}
$root = [IO.Path]::GetFullPath($RunDirectory).TrimEnd('\', '/')
$manifestPath = Join-Path $root 'manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "Synchronized manifest is missing: $manifestPath"
}
$manifest = [IO.File]::ReadAllText($manifestPath) | ConvertFrom-Json
if ([int]$manifest.SchemaVersion -notin @(1, 2)) {
    throw "Unsupported synchronized manifest schema: $($manifest.SchemaVersion)"
}
$evidenceMode = if ($manifest.PSObject.Properties.Name -contains 'EvidenceMode') {
    [string]$manifest.EvidenceMode
} else { 'LegacyPassiveUnspecified' }
if ([int]$manifest.SchemaVersion -eq 2 -and
        $evidenceMode -ne 'PassiveUnguidedTracking') {
    throw "Unsupported synchronized evidence mode for passive analysis: $evidenceMode"
}
if (-not [bool]$manifest.CoverageQualified -or
        -not [bool]$manifest.ClockQualified100Milliseconds) {
    throw 'Synchronized manifest did not pass coverage and clock qualification.'
}
if ([bool]$manifest.GrantsMountMotionAuthority -or
        [bool]$manifest.GrantsUpasAuthority -or
        [bool]$manifest.GrantsAbsoluteAccuracyClaim) {
    throw 'Synchronized manifest contains an invalid authority grant.'
}
$clockProbes = @($manifest.ClockProbes)
if ($clockProbes.Count -ne 3 -or
        @($clockProbes.Label) -join ',' -ne 'start,midpoint,end') {
    throw 'Synchronized manifest must contain start, midpoint, and end clock probes.'
}
foreach ($probe in $clockProbes) {
    if (-not [bool]$probe.Qualified100Milliseconds -or
            $null -eq $probe.OffsetSeconds -or
            [Math]::Abs([double]$probe.OffsetSeconds) -gt 0.1) {
        throw "Clock probe '$($probe.Label)' is not qualified to 100 ms."
    }
}

$artifactEntries = @($manifest.Artifacts)
if ($artifactEntries.Count -eq 0) {
    throw 'Synchronized manifest contains no artifacts.'
}
$manifestPaths = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::OrdinalIgnoreCase)
foreach ($artifact in $artifactEntries) {
    $relative = Get-NormalizedRelativePath ([string]$artifact.RelativePath)
    if ([string]::IsNullOrWhiteSpace($relative) -or
            [IO.Path]::IsPathRooted($relative) -or
            $relative -match '(^|/)[.][.](/|$)') {
        throw "Unsafe artifact path in synchronized manifest: $relative"
    }
    if (-not $manifestPaths.Add($relative)) {
        throw "Duplicate artifact path in synchronized manifest: $relative"
    }
    $fullPath = [IO.Path]::GetFullPath(
        (Join-Path $root ($relative.Replace('/', [IO.Path]::DirectorySeparatorChar))))
    if (-not $fullPath.StartsWith(
            $root + [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Artifact path escapes synchronized run directory: $relative"
    }
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        throw "Manifest artifact is missing: $relative"
    }
    $file = Get-Item -LiteralPath $fullPath
    if ([long]$artifact.LengthBytes -ne [long]$file.Length) {
        throw "Manifest artifact length mismatch: $relative"
    }
    $expectedHash = ([string]$artifact.Sha256).ToLowerInvariant()
    if ($expectedHash -notmatch '^[0-9a-f]{64}$' -or
            (Get-Sha256 $fullPath) -ne $expectedHash) {
        throw "Manifest artifact hash mismatch: $relative"
    }
}
$actualPaths = @(Get-ChildItem -LiteralPath $root -Recurse -File |
    Where-Object { $_.FullName -ne $manifestPath } |
    ForEach-Object {
        Get-NormalizedRelativePath (
            $_.FullName.Substring($root.Length).TrimStart('\', '/'))
    })
if ($actualPaths.Count -ne $manifestPaths.Count) {
    throw 'Synchronized run artifact set differs from the sealed manifest.'
}
foreach ($relative in $actualPaths) {
    if (-not $manifestPaths.Contains($relative)) {
        throw "Unsealed artifact exists in synchronized run: $relative"
    }
}

$geometry = [IO.File]::ReadAllText($GeometryReceiptPath) | ConvertFrom-Json
if ([int]$geometry.SchemaVersion -ne 1 -or
        [string]$geometry.Model -ne
            'orientation-independent-spherical-triangle-upper-bound') {
    throw 'OAG geometry receipt has an unsupported schema or model.'
}
if ([bool]$geometry.GrantsMotionAuthority -or
        [bool]$geometry.GrantsAbsolutePolarAccuracyClaim) {
    throw 'OAG geometry receipt contains an invalid authority grant.'
}
$geometryDirectory = Split-Path -Parent (
    [IO.Path]::GetFullPath($GeometryReceiptPath))
$geometrySources = @(
    [pscustomobject]@{
        Label = 'main'
        Source = [string]$geometry.MainSolutionSource
        Sha256 = [string]$geometry.MainSolutionSha256
    },
    [pscustomobject]@{
        Label = 'guide'
        Source = [string]$geometry.GuideSolutionSource
        Sha256 = [string]$geometry.GuideSolutionSha256
    })
foreach ($source in $geometrySources) {
    if ([string]::IsNullOrWhiteSpace($source.Source) -or
            $source.Sha256 -notmatch '^[0-9a-fA-F]{64}$') {
        throw 'Operational OAG geometry requires both source paths and SHA-256 values.'
    }
    $sourcePath = $source.Source
    if (-not [IO.Path]::IsPathRooted($sourcePath)) {
        $sourcePath = Join-Path $geometryDirectory $sourcePath
    }
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf) -or
            (Get-Sha256 $sourcePath) -ne $source.Sha256.ToLowerInvariant()) {
        throw "OAG $($source.Label) solution source is missing or has a hash mismatch."
    }
}
$radiusPixels = [double]$geometry.GuideToFarthestMainCornerUpperBoundPixels
if ([double]::IsNaN($radiusPixels) -or
        [double]::IsInfinity($radiusPixels) -or
        $radiusPixels -le 0.0) {
    throw 'OAG geometry receipt contains an invalid guide-to-corner radius.'
}

$samplePaths = @($manifestPaths | Where-Object {
    $_ -match '^main-camera/static-solves-[^/]+/samples[.]jsonl$'
})
if ($samplePaths.Count -ne 1) {
    throw "Expected one sealed main-camera samples.jsonl artifact; found $($samplePaths.Count)."
}
$samplePath = Join-Path $root (
    $samplePaths[0].Replace('/', [IO.Path]::DirectorySeparatorChar))
$rawRows = @([IO.File]::ReadAllLines($samplePath) |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
    ForEach-Object { $_ | ConvertFrom-Json })
if ($rawRows.Count -lt 3) {
    throw 'Main-camera position-angle evidence has fewer than three rows.'
}

$solveParity = $null
$orderedRows = @($rawRows | ForEach-Object {
    $started = [DateTimeOffset]::Parse(
        [string]$_.StartedUtc,
        [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::RoundtripKind)
    $completed = [DateTimeOffset]::Parse(
        [string]$_.CompletedUtc,
        [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::RoundtripKind)
    foreach ($required in @(
            'SolveRaDegreesJ2000', 'SolveDecDegreesJ2000',
            'PositionAngleDegrees', 'Flipped')) {
        if ($_.PSObject.Properties.Name -notcontains $required) {
            throw "Main-camera sample is missing required WCS property '$required'."
        }
    }
    $ra = [double]$_.SolveRaDegreesJ2000
    $dec = [double]$_.SolveDecDegreesJ2000
    $angle = [double]$_.PositionAngleDegrees
    $flipped = [bool]$_.Flipped
    if ($completed -lt $started -or
            [double]::IsNaN($ra) -or [double]::IsInfinity($ra) -or
            [double]::IsNaN($dec) -or [double]::IsInfinity($dec) -or
            [double]::IsNaN($angle) -or [double]::IsInfinity($angle) -or
            $ra -lt 0.0 -or $ra -ge 360.0 -or
            $dec -lt -90.0 -or $dec -gt 90.0) {
        throw 'Main-camera sample contains invalid WCS coordinates, timestamp, or position angle.'
    }
    if ($null -eq $solveParity) {
        $solveParity = $flipped
    } elseif ($flipped -ne $solveParity) {
        throw 'Main-camera solve parity changed during the synchronized run.'
    }
    [pscustomobject]@{
        MidpointUtc = $started.AddTicks(
            [long](($completed - $started).Ticks / 2))
        SolveRaDegreesJ2000 = $ra
        SolveDecDegreesJ2000 = $dec
        PositionAngleDegrees = (($angle % 360.0) + 360.0) % 360.0
        Flipped = $flipped
    }
} | Sort-Object MidpointUtc)
for ($index = 1; $index -lt $orderedRows.Count; $index++) {
    if ($orderedRows[$index].MidpointUtc -le
            $orderedRows[$index - 1].MidpointUtc) {
        throw 'Main-camera sample midpoints are not strictly increasing.'
    }
}

$referenceRow = $orderedRows[0]
$referenceBasis = Get-CelestialBasis `
    ([double]$referenceRow.SolveRaDegreesJ2000) `
    ([double]$referenceRow.SolveDecDegreesJ2000)
$referenceUp = Get-NormalizedVector (Get-ImageUpVector `
    ([double]$referenceRow.SolveRaDegreesJ2000) `
    ([double]$referenceRow.SolveDecDegreesJ2000) `
    ([double]$referenceRow.PositionAngleDegrees)) 'reference image axis'
foreach ($row in $orderedRows) {
    $basis = Get-CelestialBasis `
        ([double]$row.SolveRaDegreesJ2000) `
        ([double]$row.SolveDecDegreesJ2000)
    $up = Get-NormalizedVector (Get-ImageUpVector `
        ([double]$row.SolveRaDegreesJ2000) `
        ([double]$row.SolveDecDegreesJ2000) `
        ([double]$row.PositionAngleDegrees)) 'image axis'
    $transported = Move-TangentVectorToReference `
        $up $basis.Center $referenceBasis.Center
    $signedSine = Get-DotProduct $referenceBasis.Center (
        Get-CrossProduct $transported $referenceUp)
    $signedCosine = [Math]::Max(-1.0, [Math]::Min(1.0,
        (Get-DotProduct $referenceUp $transported)))
    $row | Add-Member -NotePropertyName TransportedRollDegrees -NotePropertyValue (
        [Math]::Atan2($signedSine, $signedCosine) * 180.0 / [Math]::PI)
}

$firstTime = $orderedRows[0].MidpointUtc
$points = [System.Collections.Generic.List[object]]::new()
$rawPoints = [System.Collections.Generic.List[object]]::new()
$unwrapped = [double]$orderedRows[0].TransportedRollDegrees
$rawUnwrapped = [double]$orderedRows[0].PositionAngleDegrees
$maximumAdjacentDelta = 0.0
$maximumAdjacentRawDelta = 0.0
$points.Add([pscustomobject]@{
    ElapsedHours = 0.0
    UnwrappedRotationDegrees = $unwrapped
})
$rawPoints.Add([pscustomobject]@{
    ElapsedHours = 0.0
    UnwrappedRotationDegrees = $rawUnwrapped
})
for ($index = 1; $index -lt $orderedRows.Count; $index++) {
    $delta = Get-SignedCircularDeltaDegrees `
        ([double]$orderedRows[$index].TransportedRollDegrees) `
        ([double]$orderedRows[$index - 1].TransportedRollDegrees)
    $rawDelta = Get-SignedCircularDeltaDegrees `
        ([double]$orderedRows[$index].PositionAngleDegrees) `
        ([double]$orderedRows[$index - 1].PositionAngleDegrees)
    $maximumAdjacentDelta = [Math]::Max(
        $maximumAdjacentDelta,
        [Math]::Abs($delta))
    $maximumAdjacentRawDelta = [Math]::Max(
        $maximumAdjacentRawDelta,
        [Math]::Abs($rawDelta))
    $unwrapped += $delta
    $rawUnwrapped += $rawDelta
    $elapsedHours =
        ($orderedRows[$index].MidpointUtc - $firstTime).TotalHours
    $points.Add([pscustomobject]@{
        ElapsedHours = $elapsedHours
        UnwrappedRotationDegrees = $unwrapped
    })
    $rawPoints.Add([pscustomobject]@{
        ElapsedHours = $elapsedHours
        UnwrappedRotationDegrees = $rawUnwrapped
    })
}
$spanMinutes = ([double]$points[-1].ElapsedHours) * 60.0
$firstWindow = @($points | Where-Object {
    [double]$_.ElapsedHours * 60.0 -le $WindowMinutes
})
$lastWindowStart = $spanMinutes - $WindowMinutes
$lastWindow = @($points | Where-Object {
    [double]$_.ElapsedHours * 60.0 -ge $lastWindowStart
})
$minimumPairHours = $MinimumPairSeparationMinutes / 60.0
$rawFullFit = Get-TheilSenFit @($rawPoints) $minimumPairHours
$fullFit = Get-TheilSenFit @($points) $minimumPairHours
$firstFit = Get-TheilSenFit $firstWindow $minimumPairHours
$lastFit = Get-TheilSenFit $lastWindow $minimumPairHours

$fits = @($fullFit, $firstFit, $lastFit)
$conservativeRate = 0.0
foreach ($fit in $fits) {
    $fitSpanHours = [double]$fit.SpanMinutes / 60.0
    $madRateMargin = 3.0 * 1.4826 *
        [double]$fit.ResidualMadDegrees / $fitSpanHours
    $noiseFloorRateMargin = 2.0 *
        ($PositionAngleNoiseFloorArcseconds / 3600.0) / $fitSpanHours
    $conservativeRate = [Math]::Max(
        $conservativeRate,
        [Math]::Abs([double]$fit.SlopeDegreesPerHour) +
            $madRateMargin + $noiseFloorRateMargin)
}
$windowRateDifference = [Math]::Abs(
    [double]$firstFit.SlopeDegreesPerHour -
    [double]$lastFit.SlopeDegreesPerHour)
$conservativeSmear = Get-SmearPixels `
    $conservativeRate $ExposureSeconds $radiusPixels
$windowDisagreementSmear = Get-SmearPixels `
    $windowRateDifference $ExposureSeconds $radiusPixels

$reasons = [System.Collections.Generic.List[string]]::new()
if ($points.Count -lt $MinimumSamples) {
    $reasons.Add("sample-count $($points.Count) < $MinimumSamples")
}
if ($spanMinutes -lt $MinimumSpanMinutes) {
    $reasons.Add((
        'time-span {0:F3} min < {1:F3} min' -f
        $spanMinutes, $MinimumSpanMinutes))
}
if ($firstWindow.Count -lt 5 -or $lastWindow.Count -lt 5) {
    $reasons.Add('first/last analysis windows require at least five samples each')
}
if ($maximumAdjacentDelta -gt $MaximumAdjacentPositionAngleDeltaDegrees) {
    $reasons.Add((
        'adjacent position-angle jump {0:F6} deg > {1:F6} deg' -f
        $maximumAdjacentDelta,
        $MaximumAdjacentPositionAngleDeltaDegrees))
}
if ($windowDisagreementSmear -gt $MaximumWindowDisagreementSmearPixels) {
    $reasons.Add((
        'first/last slope disagreement implies {0:F4} px > {1:F4} px' -f
        $windowDisagreementSmear,
        $MaximumWindowDisagreementSmearPixels))
}
if ($conservativeSmear -gt $AllowedSmearPixels) {
    $reasons.Add((
        'conservative exposure smear {0:F4} px > {1:F4} px' -f
        $conservativeSmear,
        $AllowedSmearPixels))
}
$qualified = $reasons.Count -eq 0
$result = [pscustomobject][ordered]@{
    SchemaVersion = 2
    Model = 'synchronized-common-tangent-roll-passive-bound'
    NinaPositionAngleConventionSource =
        'isbeorn/nina@f360a7bab50bae776af928fe0e5dbde4c996506e: PlateSolveResult.cs, WorldCoordinateSystem.cs, ASTAPSolver.cs'
    RunDirectory = $root
    SynchronizedManifestSha256 = Get-Sha256 $manifestPath
    GeometryReceiptPath = [IO.Path]::GetFullPath($GeometryReceiptPath)
    GeometryReceiptSha256 = Get-Sha256 $GeometryReceiptPath
    GuideToFarthestCornerPixels = $radiusPixels
    GeometrySourceHashesVerified = $true
    ExposureSeconds = $ExposureSeconds
    AllowedSmearPixels = $AllowedSmearPixels
    SampleCount = $points.Count
    SpanMinutes = $spanMinutes
    EvidenceMode = $evidenceMode
    NinaPositionAngleConvention =
        'image-up axis east of celestial north; constant 180-degree ASTAP offset is slope-invariant'
    SolveParityFlipped = $solveParity
    ReferenceRightAscensionDegreesJ2000 = [double]$referenceRow.SolveRaDegreesJ2000
    ReferenceDeclinationDegreesJ2000 = [double]$referenceRow.SolveDecDegreesJ2000
    PositionAngleNoiseFloorArcseconds = $PositionAngleNoiseFloorArcseconds
    MaximumAdjacentRawPositionAngleDeltaDegrees = $maximumAdjacentRawDelta
    MaximumAdjacentTransportedRollDeltaDegrees = $maximumAdjacentDelta
    MaximumAdjacentPositionAngleDeltaDegrees = $maximumAdjacentDelta
    RawPositionAngleFullFit = $rawFullFit
    FullFit = $fullFit
    FirstWindowFit = $firstFit
    LastWindowFit = $lastFit
    ConservativeRotationRateDegreesPerHour = $conservativeRate
    ConservativeExposureRotationArcseconds =
        $conservativeRate * ($ExposureSeconds / 3600.0) * 3600.0
    ConservativeExposureSmearPixels = $conservativeSmear
    FirstLastRateDifferenceDegreesPerHour = $windowRateDifference
    FirstLastDisagreementSmearPixels = $windowDisagreementSmear
    FailureReasons = @($reasons)
    PassivePhysicalRotationWitnessQualified = $qualified
    PolarAlignmentInferenceQualified = $false
    PolarAlignmentInferenceQualificationReason =
        'Physical roll alone does not bound the polar-error vector without a qualified geometry-sensitivity model.'
    RotationSensitivityToPolarErrorComputed = $false
    OperationalGuidedExposureRotationQualified = $false
    OperationalGuidedExposureRotationQualificationReason =
        'This analyzer consumes a passive unguided tracking run; guided operational qualification requires a synchronized guided run.'
    RequiresSynchronizedGuidedEvidence = $true
    RequiresActualExposureStarShapeValidation = $true
    GrantsMountMotionAuthority = $false
    GrantsUpasAuthority = $false
    GrantsAbsolutePolarAccuracyClaim = $false
}

if ($OutputFormat -eq 'Json') {
    $result | ConvertTo-Json -Depth 8
} else {
    $result
}
