Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$toolsRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$runnerPath = Join-Path $toolsRoot 'run_guarded_ipolar_slew_stability.ps1'
$campaignRunnerPath = Join-Path $toolsRoot 'run_guarded_ipolar_pier_side_campaign.ps1'
$capturePath = Join-Path $toolsRoot 'ipolar_slew_capture.ps1'
$gatePath = Join-Path $toolsRoot 'ipolar_star_observability_gate.ps1'
foreach ($path in @($runnerPath, $campaignRunnerPath, $capturePath, $gatePath)) {
    $tokens = $null; $errors = $null
    [Management.Automation.Language.Parser]::ParseFile($path, [ref]$tokens, [ref]$errors) | Out-Null
    if ($errors.Count -gt 0) { throw "$path parse errors: $($errors -join '; ')" }
}

$runner = [IO.File]::ReadAllText($runnerPath)
$campaignRunner = [IO.File]::ReadAllText($campaignRunnerPath)
$capture = [IO.File]::ReadAllText($capturePath)
$gate = [IO.File]::ReadAllText($gatePath)

Describe 'guarded iPolar slew stability runner' {
    It 'requires the star gate before movement' {
        $runner.IndexOf('$gateProcess = Start-Process') | Should BeLessThan $runner.IndexOf('$slewProcess = Start-Process')
    }
    It 'leaves exclusive run-directory creation to the recorder' {
        $runner.Contains('throw "Run directory already exists: $runPath"') | Should Be $true
        $runner.Contains('New-Item -ItemType Directory -Force -Path $Root') | Should Be $true
        $runner.Contains('New-Item -ItemType Directory -Force -Path $runPath') | Should Be $false
        $capture.Contains('if (Test-Path -LiteralPath $run)') | Should Be $true
        $capture.Contains('New-Item -ItemType Directory -Force -Path $frames') | Should Be $true
    }
    It 'fails closed on pier-side or balcony-envelope trajectory changes' {
        $runner.Contains('[string]$mount.SideOfPier -eq $ExpectedPierSide') | Should Be $true
        $runner.Contains('Stop-MountBestEffort') | Should Be $true
        $runner.Contains('Trajectory guard failed') | Should Be $true
    }
    It 'requires an observed slew and two-second idle closure' {
        $runner.Contains('$sawSlewing = $true') | Should Be $true
        $runner.Contains('.TotalSeconds -lt 2.0') | Should Be $true
    }
    It 'preserves child stdout and stderr outside the recorder-owned run directory' {
        $runner.Contains('-RedirectStandardOutput $recorderStdoutPath') | Should Be $true
        $runner.Contains('-RedirectStandardError $recorderStderrPath') | Should Be $true
        $runner.Contains('-RedirectStandardOutput $gateStdoutPath') | Should Be $true
        $runner.Contains('-RedirectStandardError $gateStderrPath') | Should Be $true
        $runner.Contains('RecorderStdoutPath = $recorderStdoutPath') | Should Be $true
    }
    It 'runs the post-slew axis evaluator and records its qualification receipt' {
        $runner.Contains("ipolar_slew_axis_evaluator.ps1") | Should Be $true
        $runner.IndexOf('$axisProcess = Start-Process') | Should BeGreaterThan $runner.IndexOf('$slewProcess = Start-Process')
        $runner.Contains('AxisEvaluationPath = $axisEvaluationPath') | Should Be $true
        $runner.Contains('DifferentialAxisStabilityQualified') | Should Be $true
    }
    It 'uses a configurable high-rate capture cadence for short slew legs' {
        $runner.Contains('[int]$CaptureCadenceMilliseconds = 200') | Should Be $true
        $runner.Contains("'-CadenceMilliseconds', `$CaptureCadenceMilliseconds") | Should Be $true
        $runner.Contains('CaptureCadenceMilliseconds = $CaptureCadenceMilliseconds') | Should Be $true
        $capture.Contains('[ValidateRange(100, 2000)]') | Should Be $true
    }
    It 'pins source-window identity, DPI, display declaration, and native size' {
        $capture.Contains('GetWindowRect') | Should Be $true
        $capture.Contains('GetDpiForWindow') | Should Be $true
        $capture.Contains('DeclaredDisplayMode') | Should Be $true
        $capture.Contains('CaptureProfileDigest') | Should Be $true
        $capture.Contains('source-window profile changed during capture') | Should Be $true
        $capture.Contains('Run directory already exists') | Should Be $true
    }
    It 'preserves a fail-closed recorder failure receipt before exiting' {
        $capture.Contains('$failureReceiptPath') | Should Be $true
        $capture.Contains('[IO.FileMode]::CreateNew') | Should Be $true
        $capture.Contains('GrantsMountMotionAuthority = $false') | Should Be $true
        $capture.Contains('GrantsUpasAuthority = $false') | Should Be $true
    }
    It 'never grants UPAS or absolute-accuracy authority' {
        $runner.Contains('GrantsUpasAuthority = $false') | Should Be $true
        $runner.Contains('GrantsAbsoluteAccuracyClaim = $false') | Should Be $true
        $runner.ToLowerInvariant().Contains('upasbridge') | Should Be $false
    }
}

Describe 'guarded iPolar reciprocal pier-side campaign runner' {
    It 'runs only same-pier reciprocal phases and never automates a pier transition' {
        $campaignRunner.Contains("[ValidateSet('west', 'east', 'finalize')]") | Should Be $true
        $campaignRunner.Contains('Assert-StageStart') | Should Be $true
        $campaignRunner.Contains('RequiresHomeBeforeOtherPier = $true') | Should Be $true
        $campaignRunner.ToLowerInvariant().Contains('findhome') | Should Be $false
        $campaignRunner.ToLowerInvariant().Contains('slewtohome') | Should Be $false
    }

    It 'delegates every moving leg to the star-gated trajectory runner' {
        $campaignRunner.Contains("run_guarded_ipolar_slew_stability.ps1") | Should Be $true
        $campaignRunner.Contains('Invoke-GuardedLeg') | Should Be $true
        $campaignRunner.Contains('DifferentialAxisStabilityQualified') | Should Be $true
    }

    It 'uses immutable phase receipts and the four-leg campaign evaluator' {
        $campaignRunner.Contains('[IO.FileMode]::CreateNew') | Should Be $true
        $campaignRunner.Contains('ipolar_pier_side_campaign_evaluator.ps1') | Should Be $true
        $campaignRunner.Contains("'west-phase.json'") | Should Be $true
        $campaignRunner.Contains("'east-phase.json'") | Should Be $true
    }

    It 'cannot grant UPAS motion or absolute polar-alignment authority' {
        $campaignRunner.Contains('GrantsUpasAuthority = $false') | Should Be $true
        $campaignRunner.Contains('GrantsAbsoluteAccuracyClaim = $false') | Should Be $true
        $campaignRunner.ToLowerInvariant().Contains('upasbridge') | Should Be $false
    }
}

Describe 'iPolar star observability gate' {
    It 'requires fresh hashes, stable stars, and bounded centroid scatter' {
        $gate.Contains('Frames are not provably fresh/unique.') | Should Be $true
        $gate.Contains('MinimumStableStars') | Should Be $true
        $gate.Contains('MaximumRmsScatterPixels') | Should Be $true
    }
    It 'excludes the vendor control panel and color overlays' {
        $gate.Contains("Find(`$framePath, 230") | Should Be $true
        $gate.Contains('Find($framePath, 230, 60') | Should Be $true
        $gate.Contains('> 45.0') | Should Be $true
    }
    It 'rejects stable detections confined to a UI row or border' {
        $gate.Contains('MinimumStableVerticalSpanPixels') | Should Be $true
        $gate.Contains('Stable detections span only') | Should Be $true
        $gate.Contains('StableVerticalSpanPixels = $stableVerticalSpan') | Should Be $true
    }
    It 'cannot grant motion or absolute authority' {
        $gate.Contains('GrantsUpasAuthority = $false') | Should Be $true
        $gate.Contains('GrantsAbsoluteAccuracyClaim = $false') | Should Be $true
    }
}
