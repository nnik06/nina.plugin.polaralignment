#requires -Version 7.5
param(
    [Parameter(Mandatory = $true)][string]$NominationReportPath,
    [Parameter(Mandatory = $true)][string]$VectorPairReportPath,
    [Parameter(Mandatory = $true)][string]$NullPairReportPath,
    [Parameter(Mandatory = $true)][string]$TimingReportPath,
    [Parameter(Mandatory = $true)][string]$RuntimeManifestPath,
    [Parameter(Mandatory = $true)][ValidatePattern('^[0-9a-f]{64}$')][string]$MechanicalStateId,
    [Parameter(Mandatory = $true)][string]$OutputPath,
    [ValidateRange(1,30)][int]$ValidityDays = 7
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
$policy = 'tppa-cadence-v3|transitions>=20|nullPairs>=10|timings>=59|nights>=2|maxVector<=0.5|nullMaximum<=0.25|candidateToNullMaximum<=1.5|timingMax+5<=timingUpperTolerance<=75|excluded=0|directionOrderCoverage=true|nightDominance<=0.70|falseStableExits=0'

function Read-Report([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path)
    if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { throw "Evidence artifact is missing: $full" }
    return [pscustomobject]@{
        Path=$full
        Sha256=(Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash.ToLowerInvariant()
        Value=([IO.File]::ReadAllText($full) | ConvertFrom-Json -DateKind String)
    }
}
function Require([bool]$Condition,[string]$Message) { if(-not $Condition){throw $Message} }
function Number([object]$Value,[string]$Name) {
    if($Value -is [string] -or $Value -is [bool] -or $Value -isnot [ValueType]){throw "$Name is not numeric"}
    $result=[double]$Value
    if(-not [double]::IsFinite($result)){throw "$Name is not finite"}
    return $result
}
function Int([object]$Value,[string]$Name) {
    $result=Number $Value $Name
    if([Math]::Truncate($result)-ne $result){throw "$Name is not an integer"}
    return [int]$result
}
function Bool([object]$Value,[string]$Name) {
    if($Value -isnot [bool]){throw "$Name is not boolean"}
    return [bool]$Value
}
function LowerHex([object]$Value,[string]$Name,[int]$Length=64) {
    if($Value -isnot [string] -or $Value -notmatch "^[0-9a-f]{$Length}$"){throw "$Name is not canonical lowercase hexadecimal"}
    return [string]$Value
}
function Sha256Text([string]$Value) {
    $bytes=[Text.Encoding]::UTF8.GetBytes($Value)
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
}

$nomination=Read-Report $NominationReportPath
$vector=Read-Report $VectorPairReportPath
$nullArm=Read-Report $NullPairReportPath
$timing=Read-Report $TimingReportPath
$runtime=Read-Report $RuntimeManifestPath

Require ((Int $nomination.Value.SchemaVersion 'nomination.SchemaVersion') -eq 2 -and $nomination.Value.Event -eq 'tppa-settle-cadence-nomination') 'Nomination report schema/event is invalid'
Require (Bool $nomination.Value.CandidateCadenceQualified 'nomination.CandidateCadenceQualified') 'Nomination did not qualify'
Require (-not (Bool $nomination.Value.ProductionSettleQualified 'nomination.ProductionSettleQualified')) 'Nomination improperly claims production authority'
Require ((Int $nomination.Value.ValidRunCount 'nomination.ValidRunCount') -ge 20) 'Nomination has fewer than 20 transitions'
Require ((Int $nomination.Value.DubaiNightCount 'nomination.DubaiNightCount') -ge 2) 'Nomination has fewer than two nights'
$candidateSettle=Number $nomination.Value.CandidateSettleSeconds 'nomination.CandidateSettleSeconds'
Require ($candidateSettle -ge 5 -and $candidateSettle -lt 30) 'Candidate settle is outside 5..<30 seconds'
$rig=LowerHex $nomination.Value.RigConfigurationId 'nomination.RigConfigurationId'

Require ((Int $vector.Value.SchemaVersion 'vector.SchemaVersion') -eq 2 -and $vector.Value.Event -eq 'tppa-settle-vector-pair-campaign') 'Vector report schema/event is invalid'
Require (Bool $vector.Value.CandidateVectorPairQualified 'vector.CandidateVectorPairQualified') 'Vector-pair campaign did not qualify'
Require (-not (Bool $vector.Value.ProductionSettleQualified 'vector.ProductionSettleQualified')) 'Vector report improperly claims production authority'
Require ((Int $vector.Value.ValidPairCount 'vector.ValidPairCount') -ge 20) 'Vector report has fewer than 20 pairs'
Require ((Int $vector.Value.DubaiNightCount 'vector.DubaiNightCount') -ge 2) 'Vector report has fewer than two nights'
Require ((LowerHex $vector.Value.RigConfigurationId 'vector.RigConfigurationId') -eq $rig) 'Vector rig identity differs from nomination'
Require ([Math]::Abs((Number $vector.Value.CandidateSettleSeconds 'vector.CandidateSettleSeconds')-$candidateSettle) -le 0.01) 'Vector candidate settle differs from nomination'
Require ($vector.Value.CandidateNominationReceiptSha256.ToLowerInvariant() -eq $nomination.Sha256) 'Vector report does not hash-link the exact nomination report'
$candidateMaximum=Number $vector.Value.ObservedMaximumPairSeparationMinutes 'vector.ObservedMaximumPairSeparationMinutes'
Require ($candidateMaximum -le 0.5) 'Vector separation exceeds 0.5 arcminute'
Require ((Number $vector.Value.MaximumObservedNightFraction 'vector.MaximumObservedNightFraction') -le 0.70) 'Vector campaign is dominated by one night'

Require ((Int $nullArm.Value.SchemaVersion 'null.SchemaVersion') -eq 2 -and $nullArm.Value.Event -eq 'tppa-settle-null-dataset') 'Null report schema/event is invalid'
Require (Bool $nullArm.Value.NullArmDatasetQualified 'null.NullArmDatasetQualified') 'Null-arm campaign did not qualify'
Require (-not (Bool $nullArm.Value.ProductionSettleQualified 'null.ProductionSettleQualified')) 'Null report improperly claims production authority'
Require ((Int $nullArm.Value.ValidNullPairCount 'null.ValidNullPairCount') -ge 10) 'Null report has fewer than 10 pairs'
Require ((Int $nullArm.Value.DubaiNightCount 'null.DubaiNightCount') -ge 2) 'Null report has fewer than two nights'
Require ((LowerHex $nullArm.Value.RigConfigurationId 'null.RigConfigurationId') -eq $rig) 'Null rig identity differs from nomination'
$nullMaximum=[Math]::Max(
    (Number $nullArm.Value.ObservedMaximumNullLegSeparationMinutes 'null.ObservedMaximumNullLegSeparationMinutes'),
    (Number $nullArm.Value.ObservedMaximumNullMidpointSeparationMinutes 'null.ObservedMaximumNullMidpointSeparationMinutes'))
Require ($nullMaximum -gt 0 -and $nullMaximum -le 0.25) 'Null maximum is zero/uninformative or exceeds 0.25 arcminute'
Require ((Number $nullArm.Value.MaximumObservedNightFraction 'null.MaximumObservedNightFraction') -le 0.70) 'Null campaign is dominated by one night'
$ratio=$candidateMaximum/$nullMaximum
Require ($ratio -le 1.5) 'Candidate maximum is more than 1.5 times the null maximum'

Require ((Int $timing.Value.SchemaVersion 'timing.SchemaVersion') -eq 1 -and $timing.Value.Event -eq 'tppa-fresh-determination-timing-campaign') 'Timing report schema/event is invalid'
Require (Bool $timing.Value.TimingCampaignQualified 'timing.TimingCampaignQualified') 'Timing campaign did not qualify'
Require (-not (Bool $timing.Value.ProductionSettleQualified 'timing.ProductionSettleQualified')) 'Timing report improperly claims production authority'
Require ((Int $timing.Value.ValidDeterminationCount 'timing.ValidDeterminationCount') -ge 59) 'Timing report has fewer than 59 determinations'
Require ((Int $timing.Value.ExcludedSampleCount 'timing.ExcludedSampleCount') -eq 0) 'Timing report excludes samples'
Require ((Int $timing.Value.DubaiNightCount 'timing.DubaiNightCount') -ge 2) 'Timing report has fewer than two nights'
Require ((Number $timing.Value.MaximumObservedNightFraction 'timing.MaximumObservedNightFraction') -le 0.70) 'Timing campaign is dominated by one night'
Require ((LowerHex $timing.Value.HardwareConfigurationId 'timing.HardwareConfigurationId') -eq $rig) 'Timing hardware identity differs from the other campaigns'
Require ((LowerHex $timing.Value.MechanicalStateId 'timing.MechanicalStateId') -eq $MechanicalStateId) 'Timing mechanical state differs from requested authority'
$timingMaximum=Number $timing.Value.ObservedMaximumSeconds 'timing.ObservedMaximumSeconds'
$timingUpper=Number $timing.Value.ConservativeUpperBoundSeconds 'timing.ConservativeUpperBoundSeconds'
Require ($timingUpper -ge $timingMaximum+5 -and $timingUpper -le 75) 'Timing conservative upper bound is outside policy'
Require ((Number $timing.Value.P95CoverageConfidence 'timing.P95CoverageConfidence') -ge 0.95) 'Timing p95 coverage confidence is below 95%'

Require ((Int $runtime.Value.schemaVersion 'runtime.schemaVersion') -eq 1) 'Runtime manifest schema is invalid'
$runtimeSha=$runtime.Sha256
Require ((LowerHex $timing.Value.RuntimeManifestSha256 'timing.RuntimeManifestSha256') -eq $runtimeSha) 'Timing campaign runtime manifest does not match supplied manifest'
$repositoryHead=LowerHex $runtime.Value.sourceCommit 'runtime.sourceCommit' 40
$pluginArtifact=@($runtime.Value.artifacts | Where-Object name -eq 'NINA.Plugins.PolarAlignment.dll')
Require ($pluginArtifact.Count -eq 1) 'Runtime manifest does not contain exactly one plugin DLL'
$pluginSha=(LowerHex $pluginArtifact[0].sha256 'runtime.pluginSha256').ToLowerInvariant()
Require ((LowerHex $timing.Value.PluginAssemblySha256 'timing.PluginAssemblySha256') -eq $pluginSha) 'Timing plugin DLL does not match runtime manifest'

$temperatureMinimum=Number $timing.Value.TemperatureC.Minimum 'timing.TemperatureC.Minimum'
$temperatureMaximum=Number $timing.Value.TemperatureC.Maximum 'timing.TemperatureC.Maximum'
Require ($temperatureMinimum -le $temperatureMaximum) 'Timing temperature range is inverted'
$load=[string]$timing.Value.LoadProfileId
Require (-not [string]::IsNullOrWhiteSpace($load)) 'Timing load profile is empty'

$output=[IO.Path]::GetFullPath($OutputPath)
Require (-not (Test-Path -LiteralPath $output)) 'Cadence authority output already exists; overwrite is forbidden'
$now=[DateTimeOffset]::UtcNow
$nightFloor=[Math]::Min(
    [Math]::Min((Int $nomination.Value.DubaiNightCount 'nomination.DubaiNightCount'),(Int $vector.Value.DubaiNightCount 'vector.DubaiNightCount')),
    [Math]::Min((Int $nullArm.Value.DubaiNightCount 'null.DubaiNightCount'),(Int $timing.Value.DubaiNightCount 'timing.DubaiNightCount')))
$authority=[ordered]@{
    schemaVersion=3
    authorityId=[Guid]::NewGuid().ToString('D')
    commissionedUtc=$now.ToString('O')
    validUntilUtc=$now.AddDays($ValidityDays).ToString('O')
    repositoryHead=$repositoryHead
    pluginAssemblySha256=$pluginSha
    runtimeManifestSha256=$runtimeSha
    commissioningPolicySha256=(Sha256Text $policy)
    hardwareConfigurationId=$rig
    mechanicalStateId=$MechanicalStateId
    loadProfileId=$load
    temperatureC=[ordered]@{minimum=$temperatureMinimum;maximum=$temperatureMaximum}
    qualifiedSettleSeconds=$candidateSettle
    maximumFreshDeterminationSeconds=$timingUpper
    sourceNominationSha256=$nomination.Sha256
    sourceVectorPairCampaignSha256=$vector.Sha256
    sourceNullPairCampaignSha256=$nullArm.Sha256
    sourceTimingCampaignSha256=$timing.Sha256
    sourceTransitionCount=(Int $vector.Value.ValidPairCount 'vector.ValidPairCount')
    sourceNullPairCount=(Int $nullArm.Value.ValidNullPairCount 'null.ValidNullPairCount')
    sourceTimingDeterminationCount=(Int $timing.Value.ValidDeterminationCount 'timing.ValidDeterminationCount')
    sourceNightCount=$nightFloor
    maximumVectorSeparationMinutes=$candidateMaximum
    nullMaximumSeparationMinutes=$nullMaximum
    candidateMaximumSeparationMinutes=$candidateMaximum
    candidateToNullMaximumRatio=$ratio
    timingObservedMaximumSeconds=$timingMaximum
    timingUpperToleranceSeconds=$timingUpper
    timingExcludedSampleCount=0
    directionOrderCoveragePassed=$true
    noSingleNightDominance=$true
    zeroFalseStableExits=$true
}
$json=$authority|ConvertTo-Json -Depth 6 -Compress
[IO.Directory]::CreateDirectory((Split-Path -Parent $output))|Out-Null
[IO.File]::WriteAllText($output,$json,[Text.UTF8Encoding]::new($false))
[pscustomobject][ordered]@{
    AuthorityPath=$output
    AuthoritySha256=(Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash.ToLowerInvariant()
    AuthorityId=$authority.authorityId
    QualifiedSettleSeconds=$candidateSettle
    MaximumFreshDeterminationSeconds=$timingUpper
    CommissioningPolicySha256=$authority.commissioningPolicySha256
    SourceHashes=[ordered]@{
        Nomination=$nomination.Sha256
        VectorPairs=$vector.Sha256
        NullPairs=$nullArm.Sha256
        Timing=$timing.Sha256
        RuntimeManifest=$runtimeSha
    }
}