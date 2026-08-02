param(
    [switch]$LibraryOnly,
    [Parameter(Mandatory = $true)]
    [string]$RequestDirectory,
    [Parameter(Mandatory = $true)]
    [string]$OutcomeDirectory,
    [Parameter(Mandatory = $true)]
    [string]$EvidenceRoot,
    [Parameter(Mandatory = $true)]
    [string]$ObserverScriptPath,
    [Parameter(Mandatory = $true)]
    [string]$CaptureScriptPath,
    [Parameter(Mandatory = $true)]
    [string]$QualificationCliPath,
    [string]$StopFilePath,
    [ValidateRange(50, 5000)]
    [int]$PollMilliseconds = 200,
    [ValidateRange(1, 4)]
    [int]$MaximumRequests = 4,
    [ValidateRange(1, 60)]
    [int]$MaximumRuntimeMinutes = 15
)

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'

function Write-CreateNewUtf8([string]$Path, [string]$Text) {
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes($Text)
    $stream = [IO.FileStream]::new($Path, [IO.FileMode]::CreateNew,
        [IO.FileAccess]::Write, [IO.FileShare]::Read)
    try { $stream.Write($bytes, 0, $bytes.Length) } finally { $stream.Dispose() }
}

function Test-Sha256([string]$Value) {
    return $Value -match '^[0-9a-fA-F]{64}$'
}

function Invoke-QualificationCli([string[]]$Arguments) {
    $output = @(& $QualificationCliPath @Arguments 2>&1)
    return [pscustomobject]@{
        ExitCode = $LASTEXITCODE
        Text = (($output | ForEach-Object { [string]$_ }) -join
            [Environment]::NewLine)
    }
}

if ($LibraryOnly) { return }

foreach ($path in @($ObserverScriptPath, $CaptureScriptPath, $QualificationCliPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required witness-service component is missing: $path"
    }
}
foreach ($directory in @($RequestDirectory, $OutcomeDirectory, $EvidenceRoot)) {
    [IO.Directory]::CreateDirectory($directory) | Out-Null
}
$ledgerDirectory = Join-Path $EvidenceRoot 'service-ledger'
[IO.Directory]::CreateDirectory($ledgerDirectory) | Out-Null

$createdNew = $false
$mutex = [Threading.Mutex]::new(
    $false,
    'Global\TppaRaWitnessObserverService',
    [ref]$createdNew)
$ownsMutex = $false
try {
    $ownsMutex = $mutex.WaitOne(0)
    if (-not $ownsMutex) {
        throw 'Another TPPA RA witness observer service already owns the global lease.'
    }

    $deadline = [DateTime]::UtcNow.AddMinutes($MaximumRuntimeMinutes)
    $processed = 0
    while ([DateTime]::UtcNow -lt $deadline -and $processed -lt $MaximumRequests) {
        if ($StopFilePath -and (Test-Path -LiteralPath $StopFilePath)) { break }
        $requests = @(Get-ChildItem -LiteralPath $RequestDirectory -File |
            Where-Object { $_.Name.EndsWith('.witness-request.ready.json',
                [StringComparison]::OrdinalIgnoreCase) } |
            Sort-Object CreationTimeUtc, Name)
        if ($requests.Count -eq 0) {
            Start-Sleep -Milliseconds $PollMilliseconds
            continue
        }

        foreach ($requestFile in $requests) {
            if ($processed -ge $MaximumRequests) { break }
            if ($StopFilePath -and (Test-Path -LiteralPath $StopFilePath)) { break }

            $request = $null
            try {
                $request = [IO.File]::ReadAllText($requestFile.FullName) |
                    ConvertFrom-Json
                $requestDigest = ([string]$request.requestDigest).ToLowerInvariant()
                if (-not (Test-Sha256 $requestDigest)) {
                    throw 'Ready request has an invalid request digest.'
                }
                $requestValidation = Invoke-QualificationCli @(
                    'validate-witness-request',
                    '--request', $requestFile.FullName,
                    '--now-utc', [DateTime]::UtcNow.ToString('O'))
                if ($requestValidation.ExitCode -ne 0) {
                    throw "Ready request failed headless validation: $($requestValidation.Text)"
                }
                $runId = [Guid]$request.runId
                $sequenceIndex = [int]$request.sequenceIndex
                $positionId = [string]$request.positionId
                $prefix = '{0}-{1}-{2}' -f $runId.ToString('D'),
                    $sequenceIndex, $positionId
                $claimPath = Join-Path $ledgerDirectory "$requestDigest.claimed.json"
                $claim = [ordered]@{
                    schemaVersion = 1
                    requestDigest = $requestDigest
                    requestPath = $requestFile.FullName
                    claimedUtc = [DateTime]::UtcNow.ToString('O')
                    serviceProcessId = $PID
                    grantsMotionAuthority = $false
                    grantsCompletionAuthority = $false
                }
                try {
                    Write-CreateNewUtf8 $claimPath ($claim |
                        ConvertTo-Json -Compress)
                } catch [IO.IOException] {
                    continue
                }

                $runEvidenceDirectory = Join-Path $EvidenceRoot $runId.ToString('D')
                [IO.Directory]::CreateDirectory($runEvidenceDirectory) | Out-Null
                $outcomePath = Join-Path $OutcomeDirectory "$prefix-outcome.json"
                $serviceResultPath = Join-Path $runEvidenceDirectory "$prefix-service-result.json"
                if (Test-Path -LiteralPath $serviceResultPath) {
                    throw "Create-new service result already exists: $serviceResultPath"
                }
                $observerParameters = @{
                    RequestPath = $requestFile.FullName
                    OutcomePath = $outcomePath
                    EvidenceDirectory = $runEvidenceDirectory
                    CaptureScriptPath = $CaptureScriptPath
                    QualificationCliPath = $QualificationCliPath
                }
                try {
                    $observerOutput = @(& $ObserverScriptPath @observerParameters 2>&1)
                    if (-not (Test-Path -LiteralPath $outcomePath -PathType Leaf)) {
                        throw 'One-shot observer returned without a create-new outcome.'
                    }
                    $result = [ordered]@{
                        schemaVersion = 1
                        requestDigest = $requestDigest
                        status = 'captured'
                        completedUtc = [DateTime]::UtcNow.ToString('O')
                        observerOutput = (($observerOutput |
                            ForEach-Object { [string]$_ }) -join [Environment]::NewLine)
                        outcomePath = $outcomePath
                        grantsMotionAuthority = $false
                        grantsCompletionAuthority = $false
                    }
                } catch {
                    $result = [ordered]@{
                        schemaVersion = 1
                        requestDigest = $requestDigest
                        status = 'failed'
                        completedUtc = [DateTime]::UtcNow.ToString('O')
                        issue = $_.Exception.Message
                        outcomePath = $null
                        grantsMotionAuthority = $false
                        grantsCompletionAuthority = $false
                    }
                }
                Write-CreateNewUtf8 $serviceResultPath ($result |
                    ConvertTo-Json -Depth 5 -Compress)
                $processed++
            } catch {
                $identity = (Get-FileHash -LiteralPath $requestFile.FullName `
                    -Algorithm SHA256).Hash.ToLowerInvariant()
                $rejectionPath = Join-Path $ledgerDirectory "$identity.rejected.json"
                if (-not (Test-Path -LiteralPath $rejectionPath)) {
                    $rejection = [ordered]@{
                        schemaVersion = 1
                        requestPath = $requestFile.FullName
                        rejectedUtc = [DateTime]::UtcNow.ToString('O')
                        issue = $_.Exception.Message
                        grantsMotionAuthority = $false
                        grantsCompletionAuthority = $false
                    }
                    Write-CreateNewUtf8 $rejectionPath ($rejection |
                        ConvertTo-Json -Compress)
                }
                $processed++
            }
        }
    }

    [pscustomobject]@{
        Status = if ($StopFilePath -and (Test-Path -LiteralPath $StopFilePath)) {
            'stopped'
        } elseif ($processed -ge $MaximumRequests) {
            'request-limit-reached'
        } else {
            'runtime-expired'
        }
        ProcessedRequests = $processed
        MaximumRequests = $MaximumRequests
        GrantsMotionAuthority = $false
        GrantsCompletionAuthority = $false
    } | ConvertTo-Json -Compress
} finally {
    if ($ownsMutex) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}
