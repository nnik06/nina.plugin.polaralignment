param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Connect', 'Snapshot')]
    [string]$Action,
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]{0,79}$')]
    [string]$RunId,
    [ValidateRange(10, 120)]
    [int]$TimeoutSeconds = 30,
    [ValidateSet('Limited', 'Highest')]
    [string]$RunLevel = 'Limited',
    [ValidateSet('SendInput', 'WindowMessage')]
    [string]$InputMethod = 'SendInput',
    [string]$InteractiveUser = 'MeleQ4C\nnik0',
    [string]$OutputRoot = 'C:\Users\nnik0\Documents\UPAS\ipolar-ui-actions',
    [string]$PowerShellPath = 'C:\Program Files\PowerShell\7\pwsh.exe'
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$uiHelper = Join-Path $PSScriptRoot 'invoke_ipolar_ui_action.ps1'
foreach ($required in @($uiHelper, $PowerShellPath)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Required executable or script is missing: $required"
    }
}

$runPath = Join-Path $OutputRoot $RunId
$actionArtifact = Join-Path $runPath 'action.json'
$traceArtifact = Join-Path $runPath 'action.trace.log'
$screenshotArtifact = Join-Path $runPath 'desktop-after.png'
$receiptPath = Join-Path $runPath 'interactive-launch-receipt.json'
if (Test-Path -LiteralPath $runPath) {
    throw "Refusing to reuse an iPolar UI action run path: $runPath"
}
New-Item -ItemType Directory -Path $runPath | Out-Null

$consoleUser = (Get-CimInstance Win32_ComputerSystem).UserName
if ([string]::IsNullOrWhiteSpace($consoleUser) -or $consoleUser -ne $InteractiveUser) {
    throw "Interactive console user '$consoleUser' does not match required '$InteractiveUser'."
}

function Quote-Single([string]$Value) {
    "'" + $Value.Replace("'", "''") + "'"
}

$child = @"
`$ErrorActionPreference = 'Stop'
`$json = & $(Quote-Single $uiHelper) -Action $(Quote-Single $Action) -InputMethod $(Quote-Single $InputMethod) -TracePath $(Quote-Single $traceArtifact)
[IO.File]::WriteAllText($(Quote-Single $actionArtifact), (`$json -join [Environment]::NewLine), [Text.UTF8Encoding]::new(`$false))
Start-Sleep -Milliseconds 750
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
`$bounds = [Windows.Forms.SystemInformation]::VirtualScreen
`$bitmap = [Drawing.Bitmap]::new(`$bounds.Width, `$bounds.Height, [Drawing.Imaging.PixelFormat]::Format24bppRgb)
try {
    `$graphics = [Drawing.Graphics]::FromImage(`$bitmap)
    try {
        `$graphics.CopyFromScreen(`$bounds.Left, `$bounds.Top, 0, 0, `$bounds.Size, [Drawing.CopyPixelOperation]::SourceCopy)
    } finally {
        `$graphics.Dispose()
    }
    `$bitmap.Save($(Quote-Single $screenshotArtifact), [Drawing.Imaging.ImageFormat]::Png)
} finally {
    `$bitmap.Dispose()
}
"@
$encodedChild = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($child))

$safeTaskId = ($RunId -replace '[^A-Za-z0-9_-]', '_')
$taskName = "CodexIPolarUi-$safeTaskId"
if ($null -ne (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue)) {
    throw "Interactive iPolar UI task already exists: $taskName"
}

$arguments = "-NoProfile -WindowStyle Hidden -EncodedCommand $encodedChild"
$taskAction = New-ScheduledTaskAction -Execute $PowerShellPath -Argument $arguments -WorkingDirectory $PSScriptRoot
$principal = New-ScheduledTaskPrincipal -UserId $InteractiveUser -LogonType Interactive -RunLevel $RunLevel
$settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit (New-TimeSpan -Seconds $TimeoutSeconds) `
    -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries

$registered = $false
$startedUtc = [DateTime]::UtcNow
try {
    Register-ScheduledTask -TaskName $taskName -Action $taskAction -Principal $principal -Settings $settings | Out-Null
    $registered = $true
    Start-ScheduledTask -TaskName $taskName

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    $observedRunning = $false
    do {
        Start-Sleep -Milliseconds 250
        $task = Get-ScheduledTask -TaskName $taskName -ErrorAction Stop
        if ([string]$task.State -eq 'Running') { $observedRunning = $true }
    } while ([string]$task.State -in @('Running', 'Queued') -and [DateTime]::UtcNow -lt $deadline)

    if ([string]$task.State -in @('Running', 'Queued')) {
        Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
        throw "Interactive iPolar UI action timed out after $TimeoutSeconds seconds."
    }
    $taskInfo = Get-ScheduledTaskInfo -TaskName $taskName
    if ([int64]$taskInfo.LastTaskResult -ne 0) {
        throw "Interactive iPolar UI action returned task result $($taskInfo.LastTaskResult)."
    }
    if ($taskInfo.LastRunTime.ToUniversalTime() -lt $startedUtc.AddSeconds(-2)) {
        throw "Interactive iPolar UI task has no fresh run time for this invocation: $($taskInfo.LastRunTime)."
    }
    foreach ($artifact in @($actionArtifact, $screenshotArtifact)) {
        if (-not (Test-Path -LiteralPath $artifact -PathType Leaf)) {
            throw "Interactive iPolar UI action returned success without artifact: $artifact"
        }
        if ((Get-Item -LiteralPath $artifact).LastWriteTimeUtc -lt $startedUtc.AddSeconds(-2)) {
            throw "Interactive iPolar UI artifact is stale: $artifact"
        }
    }
    $actionResult = [IO.File]::ReadAllText($actionArtifact) | ConvertFrom-Json
    if ([string]$actionResult.Action -ne $Action) {
        throw "iPolar UI action artifact reports '$($actionResult.Action)', expected '$Action'."
    }

    $receipt = [ordered]@{
        SchemaVersion = 1
        RunId = $RunId
        Action = $Action
        InputMethod = $InputMethod
        StartedUtc = $startedUtc.ToString('o')
        CompletedUtc = [DateTime]::UtcNow.ToString('o')
        InteractiveUser = $InteractiveUser
        ConsoleUser = $consoleUser
        RunLevel = $RunLevel
        TaskName = $taskName
        ObservedRunning = $observedRunning
        TaskResult = [int64]$taskInfo.LastTaskResult
        UiHelperPath = [IO.Path]::GetFullPath($uiHelper)
        UiHelperSha256 = (Get-FileHash -LiteralPath $uiHelper -Algorithm SHA256).Hash
        ActionArtifactPath = [IO.Path]::GetFullPath($actionArtifact)
        ActionArtifactSha256 = (Get-FileHash -LiteralPath $actionArtifact -Algorithm SHA256).Hash
        TraceArtifactPath = [IO.Path]::GetFullPath($traceArtifact)
        TraceArtifactSha256 = if (Test-Path -LiteralPath $traceArtifact -PathType Leaf) { (Get-FileHash -LiteralPath $traceArtifact -Algorithm SHA256).Hash } else { $null }
        ScreenshotArtifactPath = [IO.Path]::GetFullPath($screenshotArtifact)
        ScreenshotArtifactSha256 = (Get-FileHash -LiteralPath $screenshotArtifact -Algorithm SHA256).Hash
        GrantsMountMotionAuthority = $false
        GrantsUpasAuthority = $false
        GrantsReadinessAuthority = $false
    }
    [IO.File]::WriteAllText($receiptPath, ($receipt | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
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
