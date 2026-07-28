#requires -Version 7.0
param(
    [Parameter(Mandatory = $true)]
    [string]$PluginDirectory,
    [Parameter(Mandatory = $true)]
    [string]$ExpectedSha256,
    [string]$IPolarPattern = 'iPolar|iOptron.*Polar',
    [string]$UpasPattern = 'USB-SERIAL|CH340|CH341|FTDI|GRBL|Avalon|UPAS',
    [string]$ExpectedUpasComPort = '',
    [ValidateRange(1, 60)]
    [int]$DeviceQueryTimeoutSeconds = 10,
    [string]$UpasBridgeHost = '',
    [ValidateRange(1, 65535)]
    [int]$UpasBridgePort = 4001,
    [ValidateRange(100, 30000)]
    [int]$NetworkTimeoutMilliseconds = 3000,
    [string]$AdbTarget = '',
    [switch]$RequireNinaClosed,
    [switch]$RequireIPolar,
    [switch]$RequireUpas,
    [switch]$RequireUpasBridge,
    [switch]$RequireAdb
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

function New-ReadinessGate {
    param(
        [string]$Name,
        [bool]$Passed,
        [string]$Detail
    )
    [pscustomobject]@{
        Name = $Name
        Passed = $Passed
        Detail = $Detail
    }
}

function Invoke-BoundedDeviceQuery {
    param(
        [Parameter(Mandatory = $true)]
        [scriptblock]$ScriptBlock,
        [Parameter(Mandatory = $true)]
        [string]$Operation
    )

    $job = Start-Job -ScriptBlock $ScriptBlock
    try {
        if (-not (Wait-Job -Job $job -Timeout $DeviceQueryTimeoutSeconds)) {
            throw "$Operation timed out after $DeviceQueryTimeoutSeconds seconds."
        }
        if ($job.State -ne 'Completed') {
            $reason = if ($job.ChildJobs.Count -gt 0) {
                [string]$job.ChildJobs[0].JobStateInfo.Reason
            } else {
                [string]$job.JobStateInfo.Reason
            }
            throw "$Operation ended in state $($job.State): $reason"
        }
        return @(Receive-Job -Job $job -ErrorAction Stop)
    } finally {
        Stop-Job -Job $job -ErrorAction SilentlyContinue
        Remove-Job -Job $job -Force -ErrorAction SilentlyContinue
    }
}

function Find-MatchingDevice {
    param(
        [object[]]$Devices,
        [string]$Pattern
    )
    return @($Devices | Where-Object {
        ([string]$_.FriendlyName -match $Pattern) -or
        ([string]$_.InstanceId -match $Pattern)
    })
}

function Test-TcpEndpoint {
    param(
        [Parameter(Mandatory = $true)]
        [string]$HostName,
        [Parameter(Mandatory = $true)]
        [int]$Port
    )

    $client = [System.Net.Sockets.TcpClient]::new()
    try {
        $connect = $client.ConnectAsync($HostName, $Port)
        if (-not $connect.Wait($NetworkTimeoutMilliseconds)) {
            return [pscustomobject]@{
                Connected = $false
                Detail = "TCP $HostName`:$Port timed out after $NetworkTimeoutMilliseconds ms."
            }
        }
        $connect.GetAwaiter().GetResult()
        return [pscustomobject]@{
            Connected = $client.Connected
            Detail = "TCP $HostName`:$Port connected."
        }
    } catch {
        return [pscustomobject]@{
            Connected = $false
            Detail = "TCP $HostName`:$Port failed: $($_.Exception.GetBaseException().Message)"
        }
    } finally {
        $client.Dispose()
    }
}

$gates = [System.Collections.Generic.List[object]]::new()
$validator = Join-Path $PSScriptRoot 'validate_tppa_plugin_install.ps1'
if (-not (Test-Path -LiteralPath $validator -PathType Leaf)) {
    throw "Plugin validator is missing: $validator"
}

try {
    $install = & $validator -PluginDirectory $PluginDirectory -ExpectedSha256 $ExpectedSha256
    $gates.Add($(New-ReadinessGate -Name 'PluginInstall' -Passed $true `
        -Detail "One live TPPA assembly; SHA256 $($install.Sha256)."))
} catch {
    $gates.Add($(New-ReadinessGate -Name 'PluginInstall' -Passed $false `
        -Detail $_.Exception.Message))
}

$ninaProcesses = @(Get-Process -Name 'NINA' -ErrorAction SilentlyContinue)
$ninaClosed = $ninaProcesses.Count -eq 0
$ninaDetail = if ($ninaClosed) {
    'NINA is not running.'
} else {
    "NINA process IDs: $(($ninaProcesses.Id | Sort-Object) -join ', ')."
}
$gates.Add($(New-ReadinessGate -Name 'NinaProcess' `
    -Passed (-not $RequireNinaClosed -or $ninaClosed) -Detail $ninaDetail))

$pnpError = ''
try {
    $pnpDevices = @(Invoke-BoundedDeviceQuery -Operation 'PnP device enumeration' -ScriptBlock {
        if (-not (Get-Command Get-PnpDevice -ErrorAction SilentlyContinue)) {
            throw 'Get-PnpDevice is unavailable.'
        }
        Get-PnpDevice -PresentOnly -ErrorAction Stop |
            Select-Object Status, Class, FriendlyName, InstanceId
    })
} catch {
    $pnpDevices = @()
    $pnpError = $_.Exception.Message
}
$ipolar = @(Find-MatchingDevice $pnpDevices $IPolarPattern)
$ipolarDetail = if ($ipolar.Count -gt 0) {
    ($ipolar | ForEach-Object { "$($_.Status): $($_.FriendlyName)" }) -join '; '
} elseif ($pnpError) {
    "PnP device enumeration failed: $pnpError"
} elseif ($pnpDevices.Count -eq 0) {
    'PnP device enumeration returned no present devices.'
} else {
    "No present device matched '$IPolarPattern'."
}
$gates.Add($(New-ReadinessGate -Name 'IPolarUsb' `
    -Passed (-not $RequireIPolar -or $ipolar.Count -gt 0) -Detail $ipolarDetail))

$serialError = ''
try {
    $serialPorts = @(Invoke-BoundedDeviceQuery -Operation 'Serial-port enumeration' -ScriptBlock {
        Get-CimInstance Win32_SerialPort -ErrorAction Stop |
            Select-Object DeviceId, Name, PnpDeviceId
    })
} catch {
    $serialPorts = @()
    $serialError = $_.Exception.Message
}
$upas = @($serialPorts | Where-Object {
    ([string]$_.Name -match $UpasPattern) -or
    ([string]$_.PnpDeviceId -match $UpasPattern) -or
    ($ExpectedUpasComPort -and [string]$_.DeviceId -ieq $ExpectedUpasComPort)
})
$expectedPortPresent = -not $ExpectedUpasComPort -or
    @($serialPorts | Where-Object { [string]$_.DeviceId -ieq $ExpectedUpasComPort }).Count -gt 0
$upasPassed = (-not $RequireUpas) -or
    ($upas.Count -gt 0 -and $expectedPortPresent)
$upasDetail = if ($upas.Count -gt 0) {
    ($upas | ForEach-Object { "$($_.DeviceId): $($_.Name)" }) -join '; '
} elseif ($serialError) {
    "Serial-port enumeration failed: $serialError"
} else {
    "No serial port matched '$UpasPattern'."
}
if ($ExpectedUpasComPort -and -not $expectedPortPresent) {
    $upasDetail += " Expected port $ExpectedUpasComPort is absent."
}
$gates.Add($(New-ReadinessGate -Name 'UpasSerial' -Passed $upasPassed `
    -Detail $upasDetail))

$bridgePassed = -not $RequireUpasBridge
$bridgeDetail = 'Pi bridge check not requested.'
if ($UpasBridgeHost) {
    $bridge = Test-TcpEndpoint -HostName $UpasBridgeHost -Port $UpasBridgePort
    $bridgePassed = $bridge.Connected
    $bridgeDetail = $bridge.Detail
} elseif ($RequireUpasBridge) {
    $bridgePassed = $false
    $bridgeDetail = 'RequireUpasBridge was specified without UpasBridgeHost.'
}
$gates.Add($(New-ReadinessGate -Name 'UpasPiBridge' -Passed $bridgePassed `
    -Detail $bridgeDetail))

$adbPassed = -not $RequireAdb
$adbDetail = 'ADB check not requested.'
if ($AdbTarget) {
    $adb = Get-Command adb -ErrorAction SilentlyContinue
    if (-not $adb) {
        $adbPassed = $false
        $adbDetail = 'adb.exe was not found.'
    } else {
        $state = (& $adb.Source -s $AdbTarget get-state 2>&1 | Out-String).Trim()
        $adbPassed = $state -eq 'device'
        $adbDetail = "ADB target $AdbTarget state: $state."
    }
} elseif ($RequireAdb) {
    $adbPassed = $false
    $adbDetail = 'RequireAdb was specified without AdbTarget.'
}
$gates.Add($(New-ReadinessGate -Name 'P20Adb' -Passed $adbPassed `
    -Detail $adbDetail))

$failed = @($gates | Where-Object { -not $_.Passed })
$result = [pscustomobject]@{
    TimestampUtc = [DateTime]::UtcNow
    Ready = $failed.Count -eq 0
    FailedGateCount = $failed.Count
    Gates = @($gates)
    SerialPorts = @($serialPorts)
}
$result | ConvertTo-Json -Depth 6

if (-not $result.Ready) {
    exit 2
}
