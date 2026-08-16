#requires -Version 7.0
param(
    [Parameter(Mandatory = $true)]
    [string]$PluginDirectory,
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9A-Fa-f]{64}$')]
    [string]$ExpectedRuntimeManifestSha256,
    [string]$IPolarPattern = 'iPolar|iOptron.*Polar',
    [string]$MainCameraPattern = 'ASI2600|VID_03C3&PID_260E',
    [string]$GuideCameraPattern = 'ASI220|VID_03C3&PID_2209',
    [string]$FilterWheelPattern = 'EFW|VID_03C3&PID_1F01',
    [string]$UpasPattern = 'USB-SERIAL|CH340|CH341|FTDI|GRBL|Avalon|UPAS',
    [string]$ExpectedUpasComPort = '',
    [ValidateRange(1, 60)]
    [int]$DeviceQueryTimeoutSeconds = 10,
    [string]$UpasBridgeHost = '',
    [ValidateRange(1, 65535)]
    [int]$UpasBridgePort = 4001,
    [string]$UpasBridgeClientProcessName = '',
    [ValidateRange(100, 30000)]
    [int]$NetworkTimeoutMilliseconds = 3000,
    [string]$AdbTarget = '',
    [string]$AdbExecutable = '',
    [switch]$RequireNinaClosed,
    [switch]$RequireIPolar,
    [switch]$RequireMainCamera,
    [switch]$RequireGuideCamera,
    [switch]$RequireFilterWheel,
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
        [void]$connect.GetAwaiter().GetResult()
        return [pscustomobject]@{
            Connected = $client.Connected
            Detail = "TCP transport probe to $HostName`:$Port connected. " +
                'Serial and GRBL health were not tested.'
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

function Test-EstablishedTcpSession {
    param(
        [Parameter(Mandatory = $true)]
        [string]$HostName,
        [Parameter(Mandatory = $true)]
        [int]$Port,
        [Parameter(Mandatory = $true)]
        [string]$ProcessName
    )

    $expectedProcessName = [IO.Path]::GetFileNameWithoutExtension($ProcessName)
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        try {
            $remoteAddresses = [System.Collections.Generic.HashSet[string]]::new(
                [StringComparer]::OrdinalIgnoreCase
            )
            [void]$remoteAddresses.Add($HostName)
            foreach ($address in [Net.Dns]::GetHostAddresses($HostName)) {
                [void]$remoteAddresses.Add($address.IPAddressToString)
            }
            $connections = @(Get-NetTCPConnection -State Established -ErrorAction Stop |
                Where-Object {
                    $_.RemotePort -eq $Port
                })
        } catch {
            return [pscustomobject]@{
                Connected = $false
                Detail = "Could not inspect established TCP sessions: " +
                    $_.Exception.GetBaseException().Message
            }
        }

        foreach ($connection in $connections) {
            $owner = Get-Process -Id $connection.OwningProcess -ErrorAction SilentlyContinue
            if ($owner -and $owner.ProcessName -ieq $expectedProcessName) {
                $resolvedDestination = $remoteAddresses.Contains(
                    [string]$connection.RemoteAddress
                )
                $ownerCommandLine = [string](Get-CimInstance Win32_Process `
                    -Filter "ProcessId = $($owner.Id)" -ErrorAction SilentlyContinue |
                    Select-Object -ExpandProperty CommandLine)
                $configuredDestination = $ownerCommandLine -match
                    [regex]::Escape($HostName) -and $ownerCommandLine -match
                    "(^|\s)$Port(\s|$)"
                if (-not $resolvedDestination -and -not $configuredDestination) {
                    continue
                }
                return [pscustomobject]@{
                    Connected = $true
                    Detail = "Existing TCP session to $HostName`:$Port " +
                        "($($connection.RemoteAddress):$($connection.RemotePort)) is established " +
                        "by $($owner.ProcessName) (PID $($owner.Id)); no active bridge " +
                        'probe was issued. This verifies the transport session only; ' +
                        'serial and GRBL health were not tested.'
                }
            }
        }

        if ($attempt -lt 3) {
            Start-Sleep -Milliseconds 250
        }
    }

    return [pscustomobject]@{
        Connected = $false
        Detail = "No established TCP session to $HostName`:$Port is owned by " +
            "$expectedProcessName; no active bridge probe was issued."
    }
}

$gates = [System.Collections.Generic.List[object]]::new()
$validator = Join-Path $PSScriptRoot 'validate_tppa_plugin_install.ps1'
if (-not (Test-Path -LiteralPath $validator -PathType Leaf)) {
    throw "Plugin validator is missing: $validator"
}

try {
    $install = & $validator -PluginDirectory $PluginDirectory -ExpectedRuntimeManifestSha256 $ExpectedRuntimeManifestSha256
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

$pnpRequirements = @(
    [pscustomobject]@{
        Name = 'MainCameraUsb'
        Required = [bool]$RequireMainCamera
        Pattern = $MainCameraPattern
    },
    [pscustomobject]@{
        Name = 'GuideCameraUsb'
        Required = [bool]$RequireGuideCamera
        Pattern = $GuideCameraPattern
    },
    [pscustomobject]@{
        Name = 'FilterWheelUsb'
        Required = [bool]$RequireFilterWheel
        Pattern = $FilterWheelPattern
    }
)
foreach ($requirement in $pnpRequirements) {
    $matches = @(Find-MatchingDevice $pnpDevices $requirement.Pattern)
    $detail = if ($matches.Count -gt 0) {
        ($matches | ForEach-Object { "$($_.Status): $($_.FriendlyName)" }) -join '; '
    } elseif ($pnpError) {
        "PnP device enumeration failed: $pnpError"
    } elseif ($pnpDevices.Count -eq 0) {
        'PnP device enumeration returned no present devices.'
    } else {
        "No present device matched '$($requirement.Pattern)'."
    }
    $gates.Add($(New-ReadinessGate -Name $requirement.Name `
        -Passed (-not $requirement.Required -or $matches.Count -gt 0) -Detail $detail))
}

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
    if ($UpasBridgeClientProcessName) {
        $bridgeResults = @(Test-EstablishedTcpSession -HostName $UpasBridgeHost `
            -Port $UpasBridgePort -ProcessName $UpasBridgeClientProcessName)
    } else {
        $bridgeResults = @(Test-TcpEndpoint -HostName $UpasBridgeHost `
            -Port $UpasBridgePort)
    }
    if ($bridgeResults.Count -ne 1 -or
        -not $bridgeResults[0].PSObject.Properties['Connected'] -or
        -not $bridgeResults[0].PSObject.Properties['Detail']) {
        $bridgePassed = $false
        $bridgeDetail = "Pi bridge probe returned an invalid result count/schema: $($bridgeResults.Count)."
    } else {
        $bridgePassed = [bool]$bridgeResults[0].Connected
        $bridgeDetail = [string]$bridgeResults[0].Detail
    }
} elseif ($RequireUpasBridge) {
    $bridgePassed = $false
    $bridgeDetail = 'RequireUpasBridge was specified without UpasBridgeHost.'
}
$gates.Add($(New-ReadinessGate -Name 'UpasPiBridge' -Passed $bridgePassed `
    -Detail $bridgeDetail))

$adbPassed = -not $RequireAdb
$adbDetail = 'ADB check not requested.'
if ($AdbTarget) {
    $adbSource = ''
    if ($AdbExecutable) {
        if (Test-Path -LiteralPath $AdbExecutable -PathType Leaf) {
            $adbSource = (Resolve-Path -LiteralPath $AdbExecutable).Path
        } else {
            $adbDetail = "Configured adb executable does not exist: $AdbExecutable"
        }
    } else {
        $adb = Get-Command adb -ErrorAction SilentlyContinue
        if ($adb) {
            $adbSource = $adb.Source
        }
    }
    if (-not $adbSource -and -not $adbDetail.StartsWith('Configured adb executable')) {
        $adbPassed = $false
        $adbDetail = 'adb.exe was not found.'
    } elseif ($adbSource) {
        $stateOutput = (& $adbSource -s $AdbTarget get-state 2>&1 | Out-String).Trim()
        $stateLines = @($stateOutput -split '\r?\n' |
            ForEach-Object { $_.Trim() } | Where-Object { $_ })
        $reportedStates = @($stateLines | Where-Object {
                $_ -in @('device', 'offline', 'unauthorized', 'unknown',
                    'bootloader', 'recovery', 'sideload')
            })
        $state = if ($reportedStates.Count -gt 0) {
            $reportedStates[-1]
        } elseif ($stateLines.Count -gt 0) {
            $stateLines[-1]
        } else {
            ''
        }
        $adbPassed = $state -eq 'device'
        $adbDetail = "ADB target $AdbTarget state: $state. Executable: $adbSource"
        if ($stateOutput -ne $state) {
            $adbDetail += " Raw output: $stateOutput"
        }
    } else {
        $adbPassed = $false
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
