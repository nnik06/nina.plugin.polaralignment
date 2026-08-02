Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$toolsRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$tool = Join-Path $toolsRoot 'analyze_synchronized_pa_discriminator.ps1'

function Write-Utf8([string]$Path, [string]$Text) {
    [IO.File]::WriteAllText($Path, $Text, [Text.UTF8Encoding]::new($false))
}

function New-SyntheticRun(
        [string]$Root,
        [scriptblock]$AngleForElapsedHours,
        [double]$StartAngleDegrees = 42.0) {
    [IO.Directory]::CreateDirectory($Root) | Out-Null
    $sampleDirectory = Join-Path $Root 'main-camera\static-solves-synthetic'
    [IO.Directory]::CreateDirectory($sampleDirectory) | Out-Null
    $samplePath = Join-Path $sampleDirectory 'samples.jsonl'
    $start = [DateTimeOffset]::Parse('2026-08-01T20:00:00Z')
    $lines = [System.Collections.Generic.List[string]]::new()
    for ($index = 0; $index -le 40; $index++) {
        $elapsedHours = $index * 0.5 / 60.0
        $angle = $StartAngleDegrees +
            (& $AngleForElapsedHours $elapsedHours)
        $started = $start.AddMinutes($index * 0.5)
        $row = [ordered]@{
            Index = $index + 1
            StartedUtc = $started.ToString('o')
            CompletedUtc = $started.AddSeconds(3).ToString('o')
            PositionAngleDegrees = (($angle % 360.0) + 360.0) % 360.0
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
        SchemaVersion = 1
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

        $result.OperationalGuidedExposureRotationQualified | Should Be $true
        [Math]::Abs($result.FullFit.SlopeDegreesPerHour - 0.001) |
            Should BeLessThan 0.000001
        $result.ConservativeExposureSmearPixels | Should BeLessThan 0.1
        $result.RequiresActualExposureStarShapeValidation | Should Be $true
        $result.GrantsAbsolutePolarAccuracyClaim | Should Be $false
        $result.GeometrySourceHashesVerified | Should Be $true
    }

    It 'rejects a stable series whose measured rotation exceeds the budget' {
        $run = Join-Path $TestDrive 'fast'
        New-SyntheticRun $run { param($hours) 0.03 * $hours } | Out-Null
        $geometry = Join-Path $TestDrive 'fast-geometry.json'
        New-GeometryReceipt $geometry

        $result = & $tool -RunDirectory $run -GeometryReceiptPath $geometry

        $result.OperationalGuidedExposureRotationQualified | Should Be $false
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

        $result.OperationalGuidedExposureRotationQualified | Should Be $false
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

        $result.OperationalGuidedExposureRotationQualified | Should Be $false
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
}
