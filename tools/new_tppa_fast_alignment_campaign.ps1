#requires -Version 7.5
[CmdletBinding()]
param(
    [Guid]$CampaignId = [Guid]::NewGuid(),
    [Parameter(Mandatory)][ValidateSet(
        'WO-GT81-IV-0.8-OAG-L-ASI2600MM-gain100-bin1',
        'EdgeHD-9.25-0.7-OAG-L-ASI2600MM-gain100-bin1')]
    [string]$OpticalTrainId,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string]$RepositoryHead,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{64}$')][string]$PluginAssemblySha256,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$')][string]$CovarianceAuthorityId,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{64}$')][string]$CovarianceAuthoritySha256,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$')][string]$CadenceAuthorityId,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{64}$')][string]$CadenceAuthoritySha256,
    [Parameter(Mandatory)][ValidateRange(5.0, 29.999)][double]$QualifiedSettleSeconds,
    [Parameter(Mandatory)][ValidateRange(5.0, 75.0)][double]$QualifiedFreshDeterminationSeconds,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{64}$')][string]$MechanicalStateId,
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$LoadProfileId,
    [Parameter(Mandatory)][string[]]$LogPath,
    [Parameter(Mandatory)][string]$OutputPath,
    [ValidateRange(1, 100)][int]$ExpectedAttemptCount = 20,
    [ValidateRange(0.01, 1.0)][double]$RequiredPassRate = 0.8,
    [ValidateRange(1, 100)][int]$MinimumSuccessfulAttempts = 16,
    [ValidateRange(1, 100)][int]$MinimumEligibleRuns = 20,
    [ValidateRange(1, 30)][int]$MinimumNights = 3,
    [ValidateRange(1.0, 1800.0)][double]$MaximumRuntimeSeconds = 300.0,
    [ValidateRange(0.1, 60.0)][double]$MaximumToleranceMinutes = 3.0,
    [ValidateRange(0, 2)][int]$MinimumMoveCount = 1,
    [ValidateRange(1, 2)][int]$MaximumMoveCount = 2,
    [DateTimeOffset]$CampaignEndUtc = [DateTimeOffset]::UtcNow.AddDays(4)
)

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'
$createdUtc = [DateTimeOffset]::UtcNow
if ($MinimumSuccessfulAttempts -gt $ExpectedAttemptCount -or
        $MinimumEligibleRuns -gt $ExpectedAttemptCount) {
    throw 'MinimumSuccessfulAttempts and MinimumEligibleRuns cannot exceed ExpectedAttemptCount.'
}
if ($MinimumMoveCount -gt $MaximumMoveCount) {
    throw 'MinimumMoveCount cannot exceed MaximumMoveCount.'
}
if ($CampaignEndUtc -le $createdUtc -or ($CampaignEndUtc - $createdUtc).TotalDays -gt 7.0) {
    throw 'CampaignEndUtc must be after manifest creation and no more than seven days later.'
}
$logs = @($LogPath | ForEach-Object {
    if ([string]::IsNullOrWhiteSpace($_)) { throw 'LogPath contains a blank value.' }
    [IO.Path]::GetFullPath($_)
})
if ($logs.Count -eq 0 -or @($logs | Sort-Object -Unique).Count -ne $logs.Count) {
    throw 'LogPath must contain at least one unique path.'
}
$output = [IO.Path]::GetFullPath($OutputPath)
if ($output -in $logs) { throw 'Campaign manifest output cannot be one of its log paths.' }
if ([IO.File]::Exists($output)) { throw "Refusing to overwrite campaign manifest: $output" }
$directory = [IO.Path]::GetDirectoryName($output)
if (-not [string]::IsNullOrWhiteSpace($directory)) { [void][IO.Directory]::CreateDirectory($directory) }
$minimumInitialTotalMinutes = 0.0
$maximumInitialTotalMinutes = 300.0
$stratumBounds = @(0.0, 30.0, 60.0, 120.0, 180.0, 240.0, 300.0)
$stratumCounts = [int[]]::new($stratumBounds.Count - 1)
$minimumPerStratum = [Math]::Floor($ExpectedAttemptCount / $stratumCounts.Count)
if ($minimumPerStratum -lt 1) {
    throw "ExpectedAttemptCount must be at least $($stratumCounts.Count) to cover every preregistered starting-error stratum."
}
for ($index = 0; $index -lt $stratumCounts.Count; $index++) {
    $stratumCounts[$index] = $minimumPerStratum
}
$remainderOrder = @(0, 5, 1, 4, 2, 3)
$remainder = $ExpectedAttemptCount - ($minimumPerStratum * $stratumCounts.Count)
for ($index = 0; $index -lt $remainder; $index++) {
    $stratumCounts[$remainderOrder[$index]]++
}
$initialTotalStrata = for ($index = 0; $index -lt $stratumCounts.Count; $index++) {
    [ordered]@{
        MinimumMinutesInclusive = $stratumBounds[$index]
        MaximumMinutesExclusive = $stratumBounds[$index + 1]
        RequiredAttempts = $stratumCounts[$index]
        MinimumSuccessfulAttempts = [Math]::Max(1, $stratumCounts[$index] - 2)
    }
}

$manifest = [ordered]@{
    SchemaVersion = 5
    CampaignId = $CampaignId.ToString('D')
    CreatedUtc = $createdUtc.ToUniversalTime().ToString('O')
    CampaignStartUtc = $createdUtc.ToUniversalTime().ToString('O')
    CampaignEndUtc = $CampaignEndUtc.ToUniversalTime().ToString('O')
    OpticalTrainId = $OpticalTrainId
    RepositoryHead = $RepositoryHead
    PluginAssemblySha256 = $PluginAssemblySha256
    CovarianceAuthorityId = $CovarianceAuthorityId
    CovarianceAuthoritySha256 = $CovarianceAuthoritySha256
    CadenceAuthorityId = $CadenceAuthorityId
    CadenceAuthoritySha256 = $CadenceAuthoritySha256
    QualifiedSettleSeconds = $QualifiedSettleSeconds
    QualifiedFreshDeterminationSeconds = $QualifiedFreshDeterminationSeconds
    MechanicalStateId = $MechanicalStateId
    LoadProfileId = $LoadProfileId
    ExpectedAttemptCount = $ExpectedAttemptCount
    LogPaths = $logs
    RequiredPassRate = $RequiredPassRate
    MinimumSuccessfulAttempts = $MinimumSuccessfulAttempts
    MinimumEligibleRuns = $MinimumEligibleRuns
    MinimumNights = $MinimumNights
    MaximumRuntimeSeconds = $MaximumRuntimeSeconds
    MinimumSettleSeconds = $QualifiedSettleSeconds
    MaximumToleranceMinutes = $MaximumToleranceMinutes
    MinimumMoveCount = $MinimumMoveCount
    MaximumMoveCount = $MaximumMoveCount
    MinimumInitialTotalMinutes = $minimumInitialTotalMinutes
    MaximumInitialTotalMinutes = $maximumInitialTotalMinutes
    InitialTotalStrata = $initialTotalStrata
}
$bytes = [Text.UTF8Encoding]::new($false).GetBytes(($manifest | ConvertTo-Json -Depth 4) + "`r`n")
$stream = [IO.File]::Open($output, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
try { $stream.Write($bytes, 0, $bytes.Length); $stream.Flush($true) } finally { $stream.Dispose() }
[pscustomobject]@{
    Path = $output
    Sha256 = (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash.ToLowerInvariant()
    CampaignId = $manifest.CampaignId
    ExpectedAttemptCount = $ExpectedAttemptCount
    OpticalTrainId = $OpticalTrainId
    SetForNextNinaLaunch = "[Environment]::SetEnvironmentVariable('TPPA_PREREGISTERED_CAMPAIGN_ID','$($manifest.CampaignId)','User')"
    Instruction = 'Publish this SHA-256 in the append-only session ledger before the first attempt; every sealed campaign must later be reported.'
}
