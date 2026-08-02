param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('west', 'east', 'finalize')]
    [string]$Stage,
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]{0,79}$')]
    [string]$CampaignId,
    [ValidateRange(60, 3600)]
    [int]$TimeoutSeconds = 900,
    [string]$InteractiveUser = 'MeleQ4C\nnik0',
    [string]$CampaignRoot = 'C:\Users\nnik0\Documents\UPAS\ipolar-slew-stability',
    [string]$PowerShellPath = 'C:\Program Files\PowerShell\7\pwsh.exe'
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$campaignRunner = Join-Path $PSScriptRoot 'run_guarded_ipolar_pier_side_campaign.ps1'
foreach ($required in @($campaignRunner, $PowerShellPath)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Required executable or script is missing: $required"
    }
}

$campaignPath = Join-Path $CampaignRoot $CampaignId
$stageArtifact = if ($Stage -eq 'finalize') {
    Join-Path $campaignPath 'campaign-evaluation.json'
} else {
    Join-Path $campaignPath "$Stage-phase.json"
}
$receiptPath = Join-Path $campaignPath "$Stage-interactive-launch-receipt.json"

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

if (Test-Path -LiteralPath $receiptPath -PathType Leaf) {
    $existing = [IO.File]::ReadAllText($receiptPath) | ConvertFrom-Json
    if ([string]$existing.Stage -ne $Stage -or [string]$existing.CampaignId -ne $CampaignId -or
        -not (Test-Path -LiteralPath $stageArtifact -PathType Leaf)) {
        throw "Existing interactive receipt is inconsistent: $receiptPath"
    }
    $actualArtifactHash = (Get-FileHash -LiteralPath $stageArtifact -Algorithm SHA256).Hash
    if ([string]$existing.StageArtifactSha256 -ne $actualArtifactHash) {
        throw "Existing stage artifact hash no longer matches its receipt: $stageArtifact"
    }
    [IO.File]::ReadAllText($receiptPath)
    exit 0
}

if (Test-Path -LiteralPath $stageArtifact -PathType Leaf) {
    throw "Stage artifact exists without its interactive launch receipt: $stageArtifact"
}

$consoleUser = (Get-CimInstance Win32_ComputerSystem).UserName
if ([string]::IsNullOrWhiteSpace($consoleUser) -or $consoleUser -ne $InteractiveUser) {
    throw "Interactive console user '$consoleUser' does not match required '$InteractiveUser'."
}

$safeTaskId = ($CampaignId -replace '[^A-Za-z0-9_-]', '_')
$taskName = "CodexIPolar-$safeTaskId-$Stage"
$existingTask = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
if ($null -ne $existingTask) {
    throw "Interactive campaign task already exists: $taskName"
}

$runnerHash = (Get-FileHash -LiteralPath $campaignRunner -Algorithm SHA256).Hash
$quotedRunner = '"' + $campaignRunner.Replace('"', '""') + '"'
$quotedCampaignRoot = '"' + ([IO.Path]::GetFullPath($CampaignRoot)).Replace('"', '""') + '"'
$arguments = "-NoProfile -WindowStyle Hidden -File $quotedRunner -Stage $Stage -CampaignId $CampaignId -Root $quotedCampaignRoot"
$action = New-ScheduledTaskAction -Execute $PowerShellPath -Argument $arguments -WorkingDirectory $PSScriptRoot
$principal = New-ScheduledTaskPrincipal -UserId $InteractiveUser -LogonType Interactive -RunLevel Limited
$settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit (New-TimeSpan -Seconds $TimeoutSeconds) `
    -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries

$registered = $false
$startedUtc = [DateTime]::UtcNow
try {
    Register-ScheduledTask -TaskName $taskName -Action $action -Principal $principal -Settings $settings | Out-Null
    $registered = $true
    Start-ScheduledTask -TaskName $taskName

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    $observedRunning = $false
    do {
        Start-Sleep -Milliseconds 500
        $task = Get-ScheduledTask -TaskName $taskName -ErrorAction Stop
        if ([string]$task.State -eq 'Running') { $observedRunning = $true }
    } while ([string]$task.State -in @('Running', 'Queued') -and [DateTime]::UtcNow -lt $deadline)

    if ([string]$task.State -in @('Running', 'Queued')) {
        Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
        throw "Interactive campaign stage timed out after $TimeoutSeconds seconds."
    }
    $taskInfo = Get-ScheduledTaskInfo -TaskName $taskName
    if ([int64]$taskInfo.LastTaskResult -ne 0) {
        throw "Interactive campaign stage returned task result $($taskInfo.LastTaskResult)."
    }
    if ($taskInfo.LastRunTime.ToUniversalTime() -lt $startedUtc.AddSeconds(-2)) {
        throw "Interactive campaign task has no fresh run time for this invocation: $($taskInfo.LastRunTime)."
    }
    if (-not (Test-Path -LiteralPath $stageArtifact -PathType Leaf)) {
        throw "Interactive campaign stage returned success without its artifact: $stageArtifact"
    }

    $receipt = [ordered]@{
        SchemaVersion = 1
        CampaignId = $CampaignId
        Stage = $Stage
        StartedUtc = $startedUtc.ToString('o')
        CompletedUtc = [DateTime]::UtcNow.ToString('o')
        InteractiveUser = $InteractiveUser
        ConsoleUser = $consoleUser
        TaskName = $taskName
        ObservedRunning = $observedRunning
        TaskLastRunUtc = $taskInfo.LastRunTime.ToUniversalTime().ToString('o')
        TaskResult = [int64]$taskInfo.LastTaskResult
        CampaignRunnerPath = [IO.Path]::GetFullPath($campaignRunner)
        CampaignRunnerSha256 = $runnerHash
        StageArtifactPath = [IO.Path]::GetFullPath($stageArtifact)
        StageArtifactSha256 = (Get-FileHash -LiteralPath $stageArtifact -Algorithm SHA256).Hash
        GrantsUpasAuthority = $false
        GrantsAbsoluteAccuracyClaim = $false
    }
    Write-NewUtf8File $receiptPath ($receipt | ConvertTo-Json -Depth 5)
    [IO.File]::ReadAllText($receiptPath)
} finally {
    if ($registered) {
        $task = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
        if ($null -ne $task -and [string]$task.State -in @('Running', 'Queued')) {
            Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
        }
        Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue
    }
}
