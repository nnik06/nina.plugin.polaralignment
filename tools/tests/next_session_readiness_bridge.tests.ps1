#requires -Version 7.0

$ErrorActionPreference = 'Stop'
$ToolPath = Join-Path (Split-Path -Parent $PSScriptRoot) 'test_next_session_readiness.ps1'
$TestRoot = Join-Path ([IO.Path]::GetTempPath()) (
    'upas-readiness-bridge-' + [guid]::NewGuid().ToString('N')
)
$PluginRoot = Join-Path $TestRoot 'plugin'
$PluginDll = Join-Path $PluginRoot 'NINA.Plugins.PolarAlignment.dll'

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) {
        throw "ASSERTION FAILED: $Message"
    }
}

function Invoke-Readiness {
    param(
        [int]$Port,
        [string]$ProcessName = ''
    )
    $arguments = @(
        '-NoProfile',
        '-File', $ToolPath,
        '-PluginDirectory', $PluginRoot,
        '-ExpectedSha256', (Get-FileHash -Algorithm SHA256 $PluginDll).Hash,
        '-UpasBridgeHost', '127.0.0.1',
        '-UpasBridgePort', $Port,
        '-RequireUpasBridge'
    )
    if ($ProcessName) {
        $arguments += @('-UpasBridgeClientProcessName', $ProcessName)
    }
    $output = & (Get-Process -Id $PID).Path @arguments 2>&1
    [pscustomobject]@{
        ExitCode = $LASTEXITCODE
        Result = (($output -join [Environment]::NewLine) | ConvertFrom-Json)
    }
}

try {
    New-Item -ItemType Directory -Force -Path $PluginRoot | Out-Null
    [IO.File]::WriteAllBytes($PluginDll, [byte[]](1, 2, 3, 4))

    $listener = [Net.Sockets.TcpListener]::new(
        [Net.IPAddress]::Loopback,
        0
    )
    $listener.Start()
    $port = ([Net.IPEndPoint]$listener.LocalEndpoint).Port
    $heldClient = [Net.Sockets.TcpClient]::new()
    $heldClient.Connect([Net.IPAddress]::Loopback, $port)
    $heldServer = $listener.AcceptTcpClient()
    Start-Sleep -Milliseconds 100

    $ownerName = (Get-Process -Id $PID).ProcessName
    $session = Invoke-Readiness -Port $port -ProcessName $ownerName
    Assert-True ($session.ExitCode -eq 0) (
        'an established session owned by the expected process must pass'
    )
    $bridgeGate = @($session.Result.Gates |
        Where-Object Name -eq 'UpasPiBridge')
    Assert-True (
        $bridgeGate.Count -eq 1 -and $bridgeGate[0].Passed
    ) 'UpasPiBridge must pass for the expected established session'
    Assert-True (
        $bridgeGate[0].Detail.Contains('no active bridge probe was issued')
    ) 'UpasPiBridge must report the non-invasive session path'
    Start-Sleep -Milliseconds 100
    Assert-True (-not $listener.Pending()) (
        'session inspection must not open a second TCP connection'
    )

    $wrongOwner = Invoke-Readiness -Port $port -ProcessName 'not-the-owner'
    Assert-True ($wrongOwner.ExitCode -eq 2) (
        'a session owned by another process must fail closed'
    )
    $wrongOwnerGate = @($wrongOwner.Result.Gates |
        Where-Object Name -eq 'UpasPiBridge')
    Assert-True (
        $wrongOwnerGate.Count -eq 1 -and
        -not $wrongOwnerGate[0].Passed -and
        $wrongOwnerGate[0].Detail.Contains('no active bridge probe was issued')
    ) 'owner mismatch must fail without probing'
    Start-Sleep -Milliseconds 100
    Assert-True (-not $listener.Pending()) (
        'owner mismatch must not open a second TCP connection'
    )

    $heldServer.Dispose()
    $heldClient.Dispose()
    $listener.Stop()

    $probeListener = [Net.Sockets.TcpListener]::new(
        [Net.IPAddress]::Loopback,
        0
    )
    $probeListener.Start()
    $probePort = ([Net.IPEndPoint]$probeListener.LocalEndpoint).Port
    $probe = Invoke-Readiness -Port $probePort
    Assert-True ($probe.ExitCode -eq 0) 'the generic bounded probe must pass'
    $probeGate = @($probe.Result.Gates | Where-Object Name -eq 'UpasPiBridge')
    Assert-True (
        $probeGate.Count -eq 1 -and
        $probeGate[0].Passed -and
        $probeGate[0].Detail.Contains('Serial and GRBL health were not tested')
    ) 'the generic probe must report its limited evidence'
    $probeListener.Stop()

    Write-Host 'All next-session bridge readiness tests passed.'
} finally {
    if ($heldServer) { $heldServer.Dispose() }
    if ($heldClient) { $heldClient.Dispose() }
    if ($listener) { $listener.Stop() }
    if ($probeListener) { $probeListener.Stop() }
    Remove-Item -LiteralPath $TestRoot -Recurse -Force -ErrorAction SilentlyContinue
}
