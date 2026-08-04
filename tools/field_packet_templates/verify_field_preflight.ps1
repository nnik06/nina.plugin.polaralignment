[CmdletBinding()]
param(
    [string]$RepositoryRoot = 'C:\Dev\upas-nina-tppa-plugin',
    [string]$PacketRoot = $PSScriptRoot,
    [ValidatePattern('^[0-9a-f]{40}$')]
    [string]$ExpectedHead = '__SOURCE_COMMIT__',
    [string]$OutputPath = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Require([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function FullPath([string]$Path, [string]$Label) {
    Require (-not [string]::IsNullOrWhiteSpace($Path)) "$Label is empty."
    return [IO.Path]::GetFullPath($Path)
}

function Sha256([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    try {
        $algorithm = [Security.Cryptography.SHA256]::Create()
        try { return [Convert]::ToHexString($algorithm.ComputeHash($stream)) }
        finally { $algorithm.Dispose() }
    } finally { $stream.Dispose() }
}

function Invoke-GitCommand([string[]]$Arguments) {
    $gitExe = @(Get-Command git.exe -CommandType Application -ErrorAction Stop)[0].Source
    $output = @(& $gitExe -c "safe.directory=$script:Repo" -C $script:Repo @Arguments 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed: $($output -join [Environment]::NewLine)"
    }
    return @($output | ForEach-Object { [string]$_ })
}

$script:Repo = FullPath $RepositoryRoot 'RepositoryRoot'
$packet = FullPath $PacketRoot 'PacketRoot'
Require ([IO.Directory]::Exists($script:Repo)) "Repository does not exist: $script:Repo"
Require ([IO.Directory]::Exists($packet)) "Packet does not exist: $packet"

$hashFile = [IO.Path]::Combine($packet, 'HASHES.sha256')
Require ([IO.File]::Exists($hashFile)) "Checksum manifest is missing: $hashFile"

$expected = [ordered]@{}
foreach ($line in [IO.File]::ReadAllLines($hashFile)) {
    if ([string]::IsNullOrWhiteSpace($line)) { continue }
    Require ($line -cmatch '^([0-9A-F]{64})  ([A-Za-z0-9._-]+)$') "Malformed checksum line: $line"
    $name = $Matches[2]
    Require (-not $expected.Contains($name)) "Duplicate checksum entry: $name"
    $expected[$name] = $Matches[1]
}

$requiredPacketFiles = @(
    'COVARIANCE_PREREQUISITE.md',
    'FIELD_CAMPAIGN_MATRIX.md',
    'FIELD_READINESS.md',
    'NINA.Plugins.PolarAlignment.dll',
    'NINA.Plugins.PolarAlignment.QualificationCore.dll',
    'SUPERVISOR_COARSE_RESPONSE_PROTOCOL.md',
    'UPAS_SUPERVISOR_SOURCE.zip',
    'TPPA.runtime-manifest.json',
    'verify_field_preflight.ps1'
)
foreach ($name in $requiredPacketFiles) {
    Require ($expected.Contains($name)) "Checksum manifest omits required packet file: $name"
}

$verified = [ordered]@{}
foreach ($entry in $expected.GetEnumerator()) {
    $path = [IO.Path]::Combine($packet, $entry.Key)
    Require ([IO.File]::Exists($path)) "Checksummed packet file is missing: $($entry.Key)"
    $actual = Sha256 $path
    Require ($actual -ceq $entry.Value) "Checksum mismatch for $($entry.Key): expected $($entry.Value), found $actual"
    $verified[$entry.Key] = $actual
}

$headLines = @(Invoke-GitCommand @('rev-parse', 'HEAD'))
$head = ([string]$headLines[0]).Trim()
Require ($head -ceq $ExpectedHead) "Repository HEAD drift: expected $ExpectedHead, found $head"
$trackedDirty = @(Invoke-GitCommand @('status', '--porcelain', '--untracked-files=no'))
Require ($trackedDirty.Count -eq 0) "Repository has tracked changes: $($trackedDirty -join '; ')"
$untracked = @(Invoke-GitCommand @('status', '--porcelain', '--untracked-files=all')) |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

$fieldReadiness = [IO.File]::ReadAllText([IO.Path]::Combine($packet, 'FIELD_READINESS.md'))
$fieldMatrix = [IO.File]::ReadAllText([IO.Path]::Combine($packet, 'FIELD_CAMPAIGN_MATRIX.md'))
$coarsePrerequisite = [IO.File]::ReadAllText([IO.Path]::Combine($packet, 'COARSE_RESPONSE_PREREQUISITE.md'))
$expectedSupervisorCommit = '__SUPERVISOR_SOURCE_COMMIT__'
$expectedSupervisorProtocolSha256 = '__SUPERVISOR_PROTOCOL_SHA256__'
$expectedSupervisorSourceArchiveSha256 = '__SUPERVISOR_SOURCE_ARCHIVE_SHA256__'
Require ($fieldReadiness -notmatch '(?i)\bp95\b') 'Field readiness retains retired p95 cadence semantics.'
Require ($fieldReadiness -cmatch 'observed null maximum') 'Field readiness omits schema-3 null-maximum semantics.'
Require ($fieldReadiness -cmatch 'four-attempt starting-error stratum requires at least two successes') 'Field readiness omits the edge-stratum success floor.'
Require ($fieldMatrix -cmatch '0--30 and 240--300') 'Field matrix omits the four-attempt endpoint strata.'
Require ($fieldMatrix -cmatch 'Totals are 4, 3, 3, 3, 3 and 4 attempts') 'Field matrix disagrees with the sealed generator stratum counts.'
Require ($fieldMatrix -cmatch 'require at least two successes') 'Field matrix omits the endpoint-stratum success floor.'
Require ($coarsePrerequisite -notmatch '__[A-Z0-9_]+__') 'Coarse prerequisite contains unresolved provenance tokens.'
Require ($coarsePrerequisite.Contains($expectedSupervisorCommit)) `
    "Coarse prerequisite omits exact supervisor source commit: $expectedSupervisorCommit"
Require ($coarsePrerequisite.Contains($expectedSupervisorProtocolSha256)) `
    "Coarse prerequisite omits exact supervisor protocol SHA-256: $expectedSupervisorProtocolSha256"
Require ($verified['SUPERVISOR_COARSE_RESPONSE_PROTOCOL.md'] -ceq $expectedSupervisorProtocolSha256) `
    'Embedded supervisor protocol does not match the generated supervisor protocol SHA-256.'
Require ($verified['UPAS_SUPERVISOR_SOURCE.zip'] -ceq $expectedSupervisorSourceArchiveSha256) `
    'Supervisor source archive does not match the generated exact-checkpoint SHA-256.'

$manifestPath = [IO.Path]::Combine($packet, 'TPPA.runtime-manifest.json')
$manifest = [IO.File]::ReadAllText($manifestPath) | ConvertFrom-Json
Require ([int]$manifest.schemaVersion -eq 1) 'Runtime manifest schemaVersion is not 1.'
Require ([string]$manifest.packageId -ceq 'NINA.Plugins.PolarAlignment') 'Runtime manifest packageId mismatch.'
Require ([string]$manifest.pluginVersion -ceq '__PLUGIN_VERSION__') 'Runtime manifest pluginVersion mismatch.'
Require ([string]$manifest.sourceCommit -ceq $ExpectedHead) 'Runtime manifest sourceCommit mismatch.'

$manifestArtifacts = @($manifest.artifacts)
Require ($manifestArtifacts.Count -eq 2) 'Runtime manifest must bind exactly two DLL artifacts.'
foreach ($artifact in $manifestArtifacts) {
    $name = [string]$artifact.name
    Require ($name -cin @('NINA.Plugins.PolarAlignment.dll',
            'NINA.Plugins.PolarAlignment.QualificationCore.dll')) "Unexpected runtime artifact: $name"
    Require ($verified.Contains($name)) "Runtime artifact is absent from packet checksums: $name"
    Require ([string]$artifact.sha256 -ceq $verified[$name]) "Runtime manifest hash mismatch for $name"
}

$requiredTools = @(
    'analyze_tppa_settle_qualification.ps1',
    'analyze_tppa_settle_vector_pairs.ps1',
    'analyze_tppa_settle_null_pairs.ps1',
    'new_tppa_fresh_determination_timing_receipts.ps1',
    'analyze_tppa_fresh_determination_timing.ps1',
    'new_tppa_covariance_authority.ps1',
    'new_tppa_cadence_authority.ps1',
    'new_tppa_fast_alignment_campaign.ps1',
    'analyze_tppa_fast_alignment_runs.ps1',
    'new_derived_oag_geometry_receipt.ps1',
    'new_actual_exposure_saturation_scout.ps1',
    'new_actual_exposure_sequence.ps1',
    'test_guided_900s_readiness.ps1',
    'capture_phd2_guided_evidence.ps1',
    'analyze_actual_exposure_bracket.ps1'
)

$toolHashes = [ordered]@{}
foreach ($name in $requiredTools) {
    $path = [IO.Path]::Combine($script:Repo, 'tools', $name)
    Require ([IO.File]::Exists($path)) "Required field tool is missing: $name"
    $tokens = $null
    $errors = $null
    [void][Management.Automation.Language.Parser]::ParseFile($path, [ref]$tokens, [ref]$errors)
    Require (@($errors).Count -eq 0) "Required field tool has parse errors: $name"
    $toolHashes[$name] = Sha256 $path
}

$result = [ordered]@{
    SchemaVersion = 1
    Verdict = 'PASS'
    CheckedUtc = [DateTimeOffset]::UtcNow.ToString('o')
    RepositoryRoot = $script:Repo
    RepositoryHead = $head
    TrackedWorktreeClean = $true
    UntrackedEntries = $untracked
    PacketRoot = $packet
    PacketChecksumManifestSha256 = Sha256 $hashFile
    VerifiedPacketFiles = $verified
    RuntimeManifestSha256 = $verified['TPPA.runtime-manifest.json']
    SupervisorSourceCommit = $expectedSupervisorCommit
    SupervisorProtocolSha256 = $verified['SUPERVISOR_COARSE_RESPONSE_PROTOCOL.md']
    SupervisorSourceArchiveSha256 = $verified['UPAS_SUPERVISOR_SOURCE.zip']
    RequiredToolSha256 = $toolHashes
    NextStage = 'Per-train coarse-response and travel commissioning'
    GrantsDeviceConnection = $false
    GrantsMotion = $false
    GrantsCampaignStart = $false
}

$json = $result | ConvertTo-Json -Depth 8
if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
    $fullOutput = FullPath $OutputPath 'OutputPath'
    Require (-not [IO.File]::Exists($fullOutput)) "OutputPath already exists: $fullOutput"
    $parent = [IO.Path]::GetDirectoryName($fullOutput)
    if (-not [string]::IsNullOrWhiteSpace($parent)) { [IO.Directory]::CreateDirectory($parent) | Out-Null }
    [IO.File]::WriteAllText($fullOutput, $json + [Environment]::NewLine,
        [Text.UTF8Encoding]::new($false))
}

$json
