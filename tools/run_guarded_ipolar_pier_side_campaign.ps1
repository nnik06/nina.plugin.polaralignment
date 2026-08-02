param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('west', 'east', 'finalize')]
    [string]$Stage,
    [Parameter(Mandatory = $true)]
    [string]$CampaignId,
    [ValidateRange(0.1, 2.0)]
    [double]$StartToleranceDegrees = 0.5,
    [ValidateRange(100, 1000)]
    [int]$CaptureCadenceMilliseconds = 200,
    [string]$Root = 'C:\Users\nnik0\Documents\UPAS\ipolar-slew-stability'
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$api = 'http://127.0.0.1:1888/v2/api'
$legRunner = Join-Path $PSScriptRoot 'run_guarded_ipolar_slew_stability.ps1'
$campaignEvaluator = Join-Path $PSScriptRoot 'ipolar_pier_side_campaign_evaluator.ps1'
foreach ($required in @($legRunner, $campaignEvaluator)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Required tool missing: $required" }
}

$campaignPath = Join-Path $Root $CampaignId
New-Item -ItemType Directory -Force -Path $campaignPath | Out-Null

function Write-NewUtf8File([string]$Path, [string]$Content) {
    $fullPath = [IO.Path]::GetFullPath($Path)
    $parent = Split-Path -Parent $fullPath
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) {
        New-Item -ItemType Directory -Force -Path $parent | Out-Null
    }
    $stream = [IO.File]::Open($fullPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try {
        $bytes = [Text.UTF8Encoding]::new($false).GetBytes($Content)
        $stream.Write($bytes, 0, $bytes.Length)
    } finally {
        $stream.Dispose()
    }
}

function Get-MountInfo {
    return (Invoke-RestMethod -Uri "$api/equipment/mount/info" -TimeoutSec 5).Response
}

function Get-AngularDistance([double]$Left, [double]$Right) {
    $distance = [Math]::Abs($Left - $Right) % 360.0
    if ($distance -gt 180.0) { $distance = 360.0 - $distance }
    return $distance
}

function Assert-StageStart(
    [string]$ExpectedPierSide,
    [double]$ExpectedAzimuth,
    [double]$ExpectedAltitude
) {
    $mount = Get-MountInfo
    if (-not [bool]$mount.Connected -or [bool]$mount.Slewing -or -not [bool]$mount.TrackingEnabled -or [bool]$mount.AtPark) {
        throw 'Stage start requires a connected, tracking, unparked, idle mount.'
    }
    if ([string]$mount.SideOfPier -ne $ExpectedPierSide) {
        throw "Stage start pier '$($mount.SideOfPier)' does not match '$ExpectedPierSide'."
    }
    $azimuthDelta = Get-AngularDistance ([double]$mount.Azimuth) $ExpectedAzimuth
    $altitudeDelta = [Math]::Abs([double]$mount.Altitude - $ExpectedAltitude)
    if ($azimuthDelta -gt $StartToleranceDegrees -or $altitudeDelta -gt $StartToleranceDegrees) {
        throw ("Stage start is not at the qualified start: actual Az={0:F3}, Alt={1:F3}; expected Az={2:F3}, Alt={3:F3}." -f
            [double]$mount.Azimuth, [double]$mount.Altitude, $ExpectedAzimuth, $ExpectedAltitude)
    }
}

function Invoke-GuardedLeg(
    [string]$Role,
    [string]$ExpectedPierSide,
    [double]$TargetAzimuth,
    [double]$TargetAltitude
) {
    $runId = "$CampaignId-$Role"
    $args = @(
        '-NoProfile', '-File', $legRunner,
        '-TargetAzimuthDegrees', $TargetAzimuth.ToString([Globalization.CultureInfo]::InvariantCulture),
        '-TargetAltitudeDegrees', $TargetAltitude.ToString([Globalization.CultureInfo]::InvariantCulture),
        '-ExpectedPierSide', $ExpectedPierSide,
        '-RunId', $runId,
        '-CaptureCadenceMilliseconds', $CaptureCadenceMilliseconds,
        '-Root', $campaignPath
    )
    $pwsh = (Get-Process -Id $PID).Path
    $process = Start-Process -FilePath $pwsh -ArgumentList $args -Wait -PassThru -WindowStyle Hidden
    $resultPath = Join-Path (Join-Path $campaignPath $runId) 'axis-evaluation.json'
    if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $resultPath -PathType Leaf)) {
        throw "Guarded leg '$Role' failed closed; exit=$($process.ExitCode), result=$resultPath."
    }
    $result = [IO.File]::ReadAllText($resultPath) | ConvertFrom-Json
    if (-not [bool]$result.DifferentialAxisStabilityQualified) {
        throw "Guarded leg '$Role' did not qualify."
    }
    return [ordered]@{
        Role = $Role
        RunId = $runId
        ResultPath = [IO.Path]::GetFullPath($resultPath)
        ResultSha256 = (Get-FileHash -LiteralPath $resultPath -Algorithm SHA256).Hash
    }
}

if ($Stage -eq 'finalize') {
    $westPath = Join-Path $campaignPath 'west-phase.json'
    $eastPath = Join-Path $campaignPath 'east-phase.json'
    foreach ($phasePath in @($westPath, $eastPath)) {
        if (-not (Test-Path -LiteralPath $phasePath -PathType Leaf)) { throw "Missing phase receipt: $phasePath" }
    }
    $west = [IO.File]::ReadAllText($westPath) | ConvertFrom-Json
    $east = [IO.File]::ReadAllText($eastPath) | ConvertFrom-Json
    if ([string]$west.CampaignId -ne $CampaignId -or [string]$east.CampaignId -ne $CampaignId) {
        throw 'Phase receipts do not belong to this campaign.'
    }
    $manifestPath = Join-Path $campaignPath 'campaign-manifest.json'
    $manifest = [ordered]@{
        SchemaVersion = 1
        CampaignId = $CampaignId
        CreatedUtc = [DateTime]::UtcNow.ToString('o')
        Legs = @($west.Legs) + @($east.Legs)
        GrantsUpasAuthority = $false
        GrantsAbsoluteAccuracyClaim = $false
    }
    Write-NewUtf8File $manifestPath ($manifest | ConvertTo-Json -Depth 6)
    $evaluationPath = Join-Path $campaignPath 'campaign-evaluation.json'
    $pwsh = (Get-Process -Id $PID).Path
    $args = @('-NoProfile', '-File', $campaignEvaluator, '-ManifestPath', $manifestPath, '-OutputPath', $evaluationPath)
    $process = Start-Process -FilePath $pwsh -ArgumentList $args -Wait -PassThru -WindowStyle Hidden
    if ($process.ExitCode -ne 0) { throw "Pier-side campaign failed qualification; see $evaluationPath." }
    Get-Content -LiteralPath $evaluationPath -Raw
    exit 0
}

$stageConfiguration = if ($Stage -eq 'west') {
    [ordered]@{ Pier='pierWest'; StartAz=0.6; StartAlt=35.0; TargetAz=1.2; TargetAlt=45.0 }
} else {
    [ordered]@{ Pier='pierEast'; StartAz=329.7; StartAlt=49.6; TargetAz=349.0; TargetAlt=36.7 }
}

$phasePath = Join-Path $campaignPath "$Stage-phase.json"
if (Test-Path -LiteralPath $phasePath) { throw "Phase already exists and will not be overwritten: $phasePath" }
Assert-StageStart $stageConfiguration.Pier $stageConfiguration.StartAz $stageConfiguration.StartAlt

$outbound = Invoke-GuardedLeg "$Stage-outbound" $stageConfiguration.Pier $stageConfiguration.TargetAz $stageConfiguration.TargetAlt
$return = Invoke-GuardedLeg "$Stage-return" $stageConfiguration.Pier $stageConfiguration.StartAz $stageConfiguration.StartAlt
$phase = [ordered]@{
    SchemaVersion = 1
    CampaignId = $CampaignId
    Stage = $Stage
    CompletedUtc = [DateTime]::UtcNow.ToString('o')
    Legs = @($outbound, $return)
    RequiresHomeBeforeOtherPier = $true
    GrantsUpasAuthority = $false
    GrantsAbsoluteAccuracyClaim = $false
}
Write-NewUtf8File $phasePath ($phase | ConvertTo-Json -Depth 6)
$phase | ConvertTo-Json -Depth 6
