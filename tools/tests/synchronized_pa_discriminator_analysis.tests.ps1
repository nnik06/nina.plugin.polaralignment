Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$toolsRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$tool = Join-Path $toolsRoot 'analyze_synchronized_pa_discriminator.ps1'

function Write-Utf8([string]$Path, [string]$Text) {
    [IO.File]::WriteAllText($Path, $Text, [Text.UTF8Encoding]::new($false))
}

function Get-TestDot([double[]]$Left, [double[]]$Right) {
    $Left[0] * $Right[0] + $Left[1] * $Right[1] + $Left[2] * $Right[2]
}

function Get-TestCross([double[]]$Left, [double[]]$Right) {
    [double[]]@(
        ($Left[1] * $Right[2] - $Left[2] * $Right[1]),
        ($Left[2] * $Right[0] - $Left[0] * $Right[2]),
        ($Left[0] * $Right[1] - $Left[1] * $Right[0]))
}

function Get-TestBasis([double]$RaDegrees, [double]$DecDegrees) {
    $ra = $RaDegrees * [Math]::PI / 180.0
    $dec = $DecDegrees * [Math]::PI / 180.0
    [pscustomobject]@{
        Center = [double[]]@(
            ([Math]::Cos($dec) * [Math]::Cos($ra)),
            ([Math]::Cos($dec) * [Math]::Sin($ra)),
            [Math]::Sin($dec))
        North = [double[]]@(
            (-[Math]::Sin($dec) * [Math]::Cos($ra)),
            (-[Math]::Sin($dec) * [Math]::Sin($ra)),
            [Math]::Cos($dec))
        East = [double[]]@(-[Math]::Sin($ra), [Math]::Cos($ra), 0.0)
    }
}

function Rotate-TestVector(
        [double[]]$Vector,
        [double[]]$Axis,
        [double]$AngleRadians) {
    $cross = Get-TestCross $Axis $Vector
    $dot = Get-TestDot $Axis $Vector
    $cosine = [Math]::Cos($AngleRadians)
    $sine = [Math]::Sin($AngleRadians)
    [double[]]@(
        ($Vector[0] * $cosine + $cross[0] * $sine + $Axis[0] * $dot * (1.0 - $cosine)),
        ($Vector[1] * $cosine + $cross[1] * $sine + $Axis[1] * $dot * (1.0 - $cosine)),
        ($Vector[2] * $cosine + $cross[2] * $sine + $Axis[2] * $dot * (1.0 - $cosine)))
}

function Get-ParallelTransportedTestPositionAngle(
        $ReferenceCenter,
        $CurrentCenter,
        [double]$ReferencePositionAngleDegrees,
        [double]$IntrinsicRollDegrees) {
    $reference = Get-TestBasis `
        ([double]$ReferenceCenter.RaDegrees) ([double]$ReferenceCenter.DecDegrees)
    $current = Get-TestBasis `
        ([double]$CurrentCenter.RaDegrees) ([double]$CurrentCenter.DecDegrees)
    $referenceAngle = $ReferencePositionAngleDegrees * [Math]::PI / 180.0
    $up = [double[]]@(
        ([Math]::Cos($referenceAngle) * $reference.North[0] + [Math]::Sin($referenceAngle) * $reference.East[0]),
        ([Math]::Cos($referenceAngle) * $reference.North[1] + [Math]::Sin($referenceAngle) * $reference.East[1]),
        ([Math]::Cos($referenceAngle) * $reference.North[2] + [Math]::Sin($referenceAngle) * $reference.East[2]))
    $axisRaw = Get-TestCross $reference.Center $current.Center
    $axisNorm = [Math]::Sqrt((Get-TestDot $axisRaw $axisRaw))
    if ($axisNorm -gt 1.0e-12) {
        $axis = [double[]]@(
            ($axisRaw[0] / $axisNorm),
            ($axisRaw[1] / $axisNorm),
            ($axisRaw[2] / $axisNorm))
        $theta = [Math]::Atan2(
            $axisNorm,
            (Get-TestDot $reference.Center $current.Center))
        $up = Rotate-TestVector $up $axis $theta
    }
    if ([Math]::Abs($IntrinsicRollDegrees) -gt 0.0) {
        $up = Rotate-TestVector $up $current.Center (
            -$IntrinsicRollDegrees * [Math]::PI / 180.0)
    }
    $positionAngle = [Math]::Atan2(
        (Get-TestDot $up $current.East),
        (Get-TestDot $up $current.North)) * 180.0 / [Math]::PI
    (($positionAngle % 360.0) + 360.0) % 360.0
}
function New-SyntheticRun(
        [string]$Root,
        [scriptblock]$AngleForElapsedHours,
        [double]$StartAngleDegrees = 42.0,
        [scriptblock]$CenterForElapsedHours = $null,
        [scriptblock]$PositionAngleForState = $null,
        [scriptblock]$FlippedForIndex = $null) {
    [IO.Directory]::CreateDirectory($Root) | Out-Null
    $sampleDirectory = Join-Path $Root 'main-camera\static-solves-synthetic'
    [IO.Directory]::CreateDirectory($sampleDirectory) | Out-Null
    $samplePath = Join-Path $sampleDirectory 'samples.jsonl'
    $start = [DateTimeOffset]::Parse('2026-08-01T20:00:00Z')
    $lines = [System.Collections.Generic.List[string]]::new()
    for ($index = 0; $index -le 40; $index++) {
        $elapsedHours = $index * 0.5 / 60.0
        $intrinsicRoll = & $AngleForElapsedHours $elapsedHours
        $center = if ($null -eq $CenterForElapsedHours) {
            [pscustomobject]@{ RaDegrees = 120.0; DecDegrees = 75.0 }
        } else { & $CenterForElapsedHours $elapsedHours }
        $angle = if ($null -eq $PositionAngleForState) {
            $StartAngleDegrees + $intrinsicRoll
        } else {
            & $PositionAngleForState $index $elapsedHours $center `
                $intrinsicRoll $StartAngleDegrees
        }
        $flipped = if ($null -eq $FlippedForIndex) {
            $false
        } else { [bool](& $FlippedForIndex $index) }
        $started = $start.AddMinutes($index * 0.5)
        $row = [ordered]@{
            Index = $index + 1
            StartedUtc = $started.ToString('o')
            CompletedUtc = $started.AddSeconds(3).ToString('o')
            SolveRaDegreesJ2000 = [double]$center.RaDegrees
            SolveDecDegreesJ2000 = [double]$center.DecDegrees
            PositionAngleDegrees = (($angle % 360.0) + 360.0) % 360.0
            Flipped = $flipped
        }
        $lines.Add(($row | ConvertTo-Json -Compress))
    }
    Write-Utf8 $samplePath ($lines -join [Environment]::NewLine)
    $relative = 'main-camera/static-solves-synthetic/samples.jsonl'
    $artifact = [ordered]@{
        RelativePath = $relative
        LengthBytes = (Get-Item -LiteralPath $samplePath).Length
        Sha256 = (Get-FileHash -LiteralPath $samplePath -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    $probes = @('start', 'midpoint', 'end') | ForEach-Object {
        [ordered]@{
            Label = $_
            OffsetSeconds = 0.005
            Qualified100Milliseconds = $true
        }
    }
    $manifest = [ordered]@{
        SchemaVersion = 2
        EvidenceMode = 'PassiveUnguidedTracking'
        RunId = 'synthetic'
        StartedUtc = $start.ToString('o')
        CompletedUtc = $start.AddMinutes(20).ToString('o')
        RequestedDurationSeconds = 1200
        ClockProbes = $probes
        ClockQualified100Milliseconds = $true
        CoverageQualified = $true
        Artifacts = @($artifact)
        GrantsMountMotionAuthority = $false
        GrantsUpasAuthority = $false
        GrantsAbsoluteAccuracyClaim = $false
    }
    Write-Utf8 (Join-Path $Root 'manifest.json') (
        $manifest | ConvertTo-Json -Depth 8)
    $samplePath
}

function New-GeometryReceipt([string]$Path, [double]$RadiusPixels = 8000.0) {
    $mainSource = $Path + '.main-source'
    $guideSource = $Path + '.guide-source'
    Write-Utf8 $mainSource 'synthetic main WCS source'
    Write-Utf8 $guideSource 'synthetic guide WCS source'
    $receipt = [ordered]@{
        SchemaVersion = 1
        Model = 'orientation-independent-spherical-triangle-upper-bound'
        MainSolutionSource = [IO.Path]::GetFileName($mainSource)
        MainSolutionSha256 = (Get-FileHash $mainSource -Algorithm SHA256).Hash.ToLowerInvariant()
        GuideSolutionSource = [IO.Path]::GetFileName($guideSource)
        GuideSolutionSha256 = (Get-FileHash $guideSource -Algorithm SHA256).Hash.ToLowerInvariant()
        GuideToFarthestMainCornerUpperBoundPixels = $RadiusPixels
        GrantsMotionAuthority = $false
        GrantsAbsolutePolarAccuracyClaim = $false
    }
    Write-Utf8 $Path ($receipt | ConvertTo-Json -Depth 4)
}

Describe 'synchronized PA discriminator operational analyzer' {
    It 'qualifies a stable low-rotation 900-second series' {
        $run = Join-Path $TestDrive 'stable'
        New-SyntheticRun $run { param($hours) 0.001 * $hours } | Out-Null
        $geometry = Join-Path $TestDrive 'stable-geometry.json'
        New-GeometryReceipt $geometry

        $result = & $tool -RunDirectory $run -GeometryReceiptPath $geometry

        $result.PassivePhysicalRotationWitnessQualified | Should Be $true
        $result.PolarAlignmentInferenceQualified | Should Be $false
        $result.RotationSensitivityToPolarErrorComputed | Should Be $false
        $result.PolarAlignmentInferenceQualificationReason | Should Match 'does not bound'
        $result.OperationalGuidedExposureRotationQualified | Should Be $false
        $result.RequiresSynchronizedGuidedEvidence | Should Be $true
        [Math]::Abs($result.FullFit.SlopeDegreesPerHour - 0.001) |
            Should BeLessThan 0.000001
        $result.ConservativeExposureSmearPixels | Should BeLessThan 0.25
        $result.RequiresActualExposureStarShapeValidation | Should Be $true
        $result.GrantsAbsolutePolarAccuracyClaim | Should Be $false
        $result.GeometrySourceHashesVerified | Should Be $true
        $result.PositionAngleNoiseFloorArcseconds | Should Be 1.0
        $result.Model | Should Be 'synchronized-common-tangent-roll-passive-bound'
        $result.NinaPositionAngleConventionSource |
            Should Match 'f360a7bab50bae776af928fe0e5dbde4c996506e'
    }

    It 'rejects a stable series whose measured rotation exceeds the budget' {
        $run = Join-Path $TestDrive 'fast'
        New-SyntheticRun $run { param($hours) 0.03 * $hours } | Out-Null
        $geometry = Join-Path $TestDrive 'fast-geometry.json'
        New-GeometryReceipt $geometry

        $result = & $tool -RunDirectory $run -GeometryReceiptPath $geometry

        $result.PassivePhysicalRotationWitnessQualified | Should Be $false
        $result.ConservativeExposureSmearPixels | Should BeGreaterThan 1.0
        ($result.FailureReasons -join ' ') | Should Match 'conservative exposure smear'
    }

    It 'unwraps a position-angle series across 360 degrees' {
        $run = Join-Path $TestDrive 'wrap'
        New-SyntheticRun $run { param($hours) 0.001 * $hours } 359.9999 |
            Out-Null
        $geometry = Join-Path $TestDrive 'wrap-geometry.json'
        New-GeometryReceipt $geometry

        $result = & $tool -RunDirectory $run -GeometryReceiptPath $geometry

        [Math]::Abs($result.FullFit.SlopeDegreesPerHour - 0.001) |
            Should BeLessThan 0.000001
        $result.MaximumAdjacentPositionAngleDeltaDegrees | Should BeLessThan 0.001
    }

    It 'fails closed when a sealed artifact is modified' {
        $run = Join-Path $TestDrive 'tampered'
        $sample = New-SyntheticRun $run { param($hours) 0.001 * $hours }
        $geometry = Join-Path $TestDrive 'tampered-geometry.json'
        New-GeometryReceipt $geometry
        [IO.File]::AppendAllText($sample, [Environment]::NewLine + '{}')
        $rejected = $false
        try {
            & $tool -RunDirectory $run -GeometryReceiptPath $geometry | Out-Null
        } catch {
            $rejected = $_.Exception.Message -match 'length mismatch|hash mismatch'
        }
        $rejected | Should Be $true
    }

    It 'rejects a nonstationary first-to-last rotation rate' {
        $run = Join-Path $TestDrive 'nonstationary'
        New-SyntheticRun $run {
            param($hours)
            if ($hours -le (10.0 / 60.0)) {
                return 0.001 * $hours
            }
            0.001 * (10.0 / 60.0) +
                0.02 * ($hours - (10.0 / 60.0))
        } | Out-Null
        $geometry = Join-Path $TestDrive 'nonstationary-geometry.json'
        New-GeometryReceipt $geometry

        $result = & $tool -RunDirectory $run -GeometryReceiptPath $geometry

        $result.PassivePhysicalRotationWitnessQualified | Should Be $false
        $result.FirstLastDisagreementSmearPixels | Should BeGreaterThan 0.25
        ($result.FailureReasons -join ' ') | Should Match 'first/last slope disagreement'
    }

    It 'rejects an implausible adjacent position-angle jump' {
        $run = Join-Path $TestDrive 'jump'
        New-SyntheticRun $run {
            param($hours)
            if ($hours -ge 0.2) { return 0.2 + 0.001 * $hours }
            0.001 * $hours
        } | Out-Null
        $geometry = Join-Path $TestDrive 'jump-geometry.json'
        New-GeometryReceipt $geometry

        $result = & $tool -RunDirectory $run -GeometryReceiptPath $geometry

        $result.PassivePhysicalRotationWitnessQualified | Should Be $false
        ($result.FailureReasons -join ' ') |
            Should Match 'adjacent position-angle jump'
    }

    It 'fails closed when a geometry source is modified' {
        $run = Join-Path $TestDrive 'geometry-tamper'
        New-SyntheticRun $run { param($hours) 0.001 * $hours } | Out-Null
        $geometry = Join-Path $TestDrive 'geometry-tamper.json'
        New-GeometryReceipt $geometry
        [IO.File]::AppendAllText($geometry + '.guide-source', 'tampered')
        $rejected = $false
        try {
            & $tool -RunDirectory $run -GeometryReceiptPath $geometry | Out-Null
        } catch {
            $rejected = $_.Exception.Message -match 'guide solution source'
        }
        $rejected | Should Be $true
    }

    It 'fails closed without all three qualified clock probes' {
        $run = Join-Path $TestDrive 'clock'
        New-SyntheticRun $run { param($hours) 0.001 * $hours } | Out-Null
        $geometry = Join-Path $TestDrive 'clock-geometry.json'
        New-GeometryReceipt $geometry
        $manifestPath = Join-Path $run 'manifest.json'
        $manifest = [IO.File]::ReadAllText($manifestPath) | ConvertFrom-Json
        $manifest.ClockProbes = @($manifest.ClockProbes | Select-Object -First 2)
        Write-Utf8 $manifestPath ($manifest | ConvertTo-Json -Depth 8)
        $rejected = $false
        try {
            & $tool -RunDirectory $run -GeometryReceiptPath $geometry | Out-Null
        } catch {
            $rejected = $_.Exception.Message -match 'start, midpoint, and end'
        }
        $rejected | Should Be $true
    }

    It 'removes local-meridian convergence from a high-declination centre drift' {
        $run = Join-Path $TestDrive 'transport-fixed-roll'
        $reference = [pscustomobject]@{ RaDegrees = 359.0; DecDegrees = 80.0 }
        $centerForTime = {
            param($hours)
            [pscustomobject]@{
                RaDegrees = (359.0 + 20.0 * $hours) % 360.0
                DecDegrees = 80.0
            }
        }
        $positionAngle = {
            param($index, $hours, $center, $roll, $startAngle)
            Get-ParallelTransportedTestPositionAngle `
                ([pscustomobject]@{ RaDegrees = 359.0; DecDegrees = 80.0 }) `
                $center $startAngle $roll
        }
        New-SyntheticRun `
            -Root $run `
            -AngleForElapsedHours { param($hours) 0.0 } `
            -CenterForElapsedHours $centerForTime `
            -PositionAngleForState $positionAngle | Out-Null
        $geometry = Join-Path $TestDrive 'transport-fixed-roll-geometry.json'
        New-GeometryReceipt $geometry

        $result = & $tool -RunDirectory $run -GeometryReceiptPath $geometry

        [Math]::Abs($result.FullFit.SlopeDegreesPerHour) |
            Should BeLessThan 0.000001
        [Math]::Abs($result.RawPositionAngleFullFit.SlopeDegreesPerHour) |
            Should BeGreaterThan 1.0
        $result.MaximumAdjacentRawPositionAngleDeltaDegrees |
            Should BeGreaterThan $result.MaximumAdjacentTransportedRollDeltaDegrees
        $result.PassivePhysicalRotationWitnessQualified | Should Be $true
    }

    It 'recovers injected physical roll while the solved centre crosses RA zero' {
        $run = Join-Path $TestDrive 'transport-injected-roll'
        $reference = [pscustomobject]@{ RaDegrees = 359.0; DecDegrees = 82.0 }
        $centerForTime = {
            param($hours)
            [pscustomobject]@{
                RaDegrees = (359.0 + 18.0 * $hours) % 360.0
                DecDegrees = 82.0
            }
        }
        $positionAngle = {
            param($index, $hours, $center, $roll, $startAngle)
            Get-ParallelTransportedTestPositionAngle `
                ([pscustomobject]@{ RaDegrees = 359.0; DecDegrees = 82.0 }) `
                $center $startAngle $roll
        }
        New-SyntheticRun `
            -Root $run `
            -AngleForElapsedHours { param($hours) 0.005 * $hours } `
            -StartAngleDegrees 359.9 `
            -CenterForElapsedHours $centerForTime `
            -PositionAngleForState $positionAngle | Out-Null
        $geometry = Join-Path $TestDrive 'transport-injected-roll-geometry.json'
        New-GeometryReceipt $geometry

        $result = & $tool -RunDirectory $run -GeometryReceiptPath $geometry

        $result.FullFit.SlopeDegreesPerHour | Should BeGreaterThan 0.0
        [Math]::Abs($result.FullFit.SlopeDegreesPerHour - 0.005) |
            Should BeLessThan 0.000001
    }

    It 'fails closed when solve parity changes inside one run' {
        $run = Join-Path $TestDrive 'parity-change'
        New-SyntheticRun `
            -Root $run `
            -AngleForElapsedHours { param($hours) 0.0 } `
            -FlippedForIndex { param($index) $index -ge 20 } | Out-Null
        $geometry = Join-Path $TestDrive 'parity-change-geometry.json'
        New-GeometryReceipt $geometry
        $rejected = $false
        try {
            & $tool -RunDirectory $run -GeometryReceiptPath $geometry | Out-Null
        } catch {
            $rejected = $_.Exception.Message -match 'solve parity changed'
        }
        $rejected | Should Be $true
    }

    It 'fails closed on antipodal tangent-basis transport' {
        $run = Join-Path $TestDrive 'antipodal'
        New-SyntheticRun `
            -Root $run `
            -AngleForElapsedHours { param($hours) 0.0 } `
            -CenterForElapsedHours {
                param($hours)
                [pscustomobject]@{
                    RaDegrees = if ($hours -eq 0.0) { 0.0 } else { 180.0 }
                    DecDegrees = 0.0
                }
            } | Out-Null
        $geometry = Join-Path $TestDrive 'antipodal-geometry.json'
        New-GeometryReceipt $geometry
        $rejected = $false
        try {
            & $tool -RunDirectory $run -GeometryReceiptPath $geometry | Out-Null
        } catch {
            $rejected = $_.Exception.Message -match 'antipodal'
        }
        $rejected | Should Be $true
    }

    It 'cannot collapse the angular measurement margin to zero' {
        $run = Join-Path $TestDrive 'noise-floor'
        New-SyntheticRun $run { param($hours) 0.0 } | Out-Null
        $geometry = Join-Path $TestDrive 'noise-floor-geometry.json'
        New-GeometryReceipt $geometry

        $result = & $tool -RunDirectory $run -GeometryReceiptPath $geometry `
            -PositionAngleNoiseFloorArcseconds 60.0

        $result.ConservativeRotationRateDegreesPerHour | Should BeGreaterThan 0.0
        $result.PassivePhysicalRotationWitnessQualified | Should Be $false
        ($result.FailureReasons -join ' ') | Should Match 'conservative exposure smear'
    }

    It 'rejects a guided label rather than upgrading passive analysis authority' {
        $run = Join-Path $TestDrive 'wrong-evidence-mode'
        New-SyntheticRun $run { param($hours) 0.0 } | Out-Null
        $geometry = Join-Path $TestDrive 'wrong-evidence-mode-geometry.json'
        New-GeometryReceipt $geometry
        $manifestPath = Join-Path $run 'manifest.json'
        $manifest = [IO.File]::ReadAllText($manifestPath) | ConvertFrom-Json
        $manifest.EvidenceMode = 'GuidedTracking'
        Write-Utf8 $manifestPath ($manifest | ConvertTo-Json -Depth 8)
        $rejected = $false
        try {
            & $tool -RunDirectory $run -GeometryReceiptPath $geometry | Out-Null
        } catch {
            $rejected = $_.Exception.Message -match 'Unsupported synchronized evidence mode'
        }
        $rejected | Should Be $true
    }
}
