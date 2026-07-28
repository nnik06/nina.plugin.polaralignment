#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$SwitchName,

    [ValidateNotNullOrEmpty()]
    [string]$BaseUri = 'http://127.0.0.1:1888/v2/api',

    [ValidateNotNullOrEmpty()]
    [string]$ExpectedDeviceId = 'ASCOM.SVBONY.Switch',

    [ValidateRange(1, 120)]
    [int]$OffSeconds = 12,

    [ValidateRange(1, 30)]
    [int]$PollTimeoutSeconds = 10,

    [switch]$Execute
)

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'

function Invoke-NinaApi {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [hashtable]$Query = @{}
    )

    $uri = "$($BaseUri.TrimEnd('/'))/$($Path.TrimStart('/'))"
    if ($Query.Count -gt 0) {
        $queryString = @($Query.GetEnumerator() | Sort-Object Key | ForEach-Object {
            '{0}={1}' -f
                [Uri]::EscapeDataString([string]$_.Key),
                [Uri]::EscapeDataString(
                    [Convert]::ToString($_.Value, [Globalization.CultureInfo]::InvariantCulture)
                )
        }) -join '&'
        $uri = "$uri`?$queryString"
    }

    $response = Invoke-RestMethod -Uri $uri -Method Get -TimeoutSec $PollTimeoutSeconds
    if (-not $response.Success) {
        throw "NINA API call '$Path' failed: $($response.Error)"
    }
    return $response
}

function Get-SwitchInfo {
    $envelope = Invoke-NinaApi -Path 'equipment/switch/info'
    $info = $envelope.Response
    if (-not $info.Connected) {
        throw 'NINA switch is not connected. Connect and verify it before running this tool.'
    }
    if ([string]$info.DeviceId -ne $ExpectedDeviceId) {
        throw "Connected switch '$($info.DeviceId)' does not match expected '$ExpectedDeviceId'."
    }
    return $info
}

function Get-WritableSnapshot {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Info
    )

    $switches = @($Info.WritableSwitches)
    return @(
        for ($index = 0; $index -lt $switches.Count; $index++) {
            $item = $switches[$index]
            [pscustomobject]@{
                Index = $index
                Id = [int]$item.Id
                Name = [string]$item.Name
                TargetValue = [double]$item.TargetValue
                Value = [double]$item.Value
            }
        }
    )
}

function Assert-OtherTargetsUnchanged {
    param(
        [Parameter(Mandatory = $true)]
        [object[]]$Baseline,
        [Parameter(Mandatory = $true)]
        [object[]]$Current,
        [Parameter(Mandatory = $true)]
        [int]$TargetIndex
    )

    if ($Baseline.Count -ne $Current.Count) {
        throw "Writable switch count changed from $($Baseline.Count) to $($Current.Count)."
    }

    for ($index = 0; $index -lt $Baseline.Count; $index++) {
        if ($index -eq $TargetIndex) {
            continue
        }
        $before = $Baseline[$index]
        $after = $Current[$index]
        if ($before.Id -ne $after.Id -or $before.Name -ne $after.Name) {
            throw "Writable switch identity changed at index $index."
        }
        if ($before.TargetValue -ne $after.TargetValue) {
            throw "Unexpected target change at index $index ($($before.Name)): " +
                "$($before.TargetValue) -> $($after.TargetValue)."
        }
    }
}

function Set-SwitchValue {
    param(
        [Parameter(Mandatory = $true)]
        [int]$Index,
        [Parameter(Mandatory = $true)]
        [double]$Value
    )

    # Advanced API SwitchSet(index, value) addresses the zero-based writable collection index,
    # not the ASCOM switch Id exposed in equipment/switch/info.
    [void](Invoke-NinaApi -Path 'equipment/switch/set' -Query @{
        index = $Index
        value = $Value
    })
}

function Wait-SwitchValue {
    param(
        [Parameter(Mandatory = $true)]
        [int]$Index,
        [Parameter(Mandatory = $true)]
        [double]$ExpectedValue,
        [Parameter(Mandatory = $true)]
        [object[]]$Baseline
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($PollTimeoutSeconds)
    do {
        $info = Get-SwitchInfo
        $snapshot = @(Get-WritableSnapshot -Info $info)
        Assert-OtherTargetsUnchanged -Baseline $Baseline -Current $snapshot -TargetIndex $Index
        $target = $snapshot[$Index]
        if ($target.TargetValue -eq $ExpectedValue -and $target.Value -eq $ExpectedValue) {
            return [pscustomobject]@{
                Info = $info
                Snapshot = $snapshot
            }
        }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)

    throw "Switch index $Index did not reach value $ExpectedValue within " +
        "$PollTimeoutSeconds seconds."
}

$beforeInfo = Get-SwitchInfo
$baseline = @(Get-WritableSnapshot -Info $beforeInfo)
$matches = @($baseline | Where-Object Name -CEQ $SwitchName)
if ($matches.Count -ne 1) {
    throw "Expected exactly one writable switch named '$SwitchName'; found $($matches.Count)."
}

$target = $matches[0]
$deviceSwitch = @($beforeInfo.WritableSwitches)[$target.Index]
if ([double]$deviceSwitch.Minimum -ne 0 -or
    [double]$deviceSwitch.Maximum -ne 1 -or
    [double]$deviceSwitch.StepSize -ne 1) {
    throw "Switch '$SwitchName' is not a binary 0/1 output."
}
if ($target.TargetValue -ne 1 -or $target.Value -ne 1) {
    throw "Switch '$SwitchName' must be fully On before a recovery cycle; " +
        "target=$($target.TargetValue), value=$($target.Value)."
}

$plan = [pscustomobject]@{
    TimestampUtc = [DateTime]::UtcNow.ToString('o')
    Execute = [bool]$Execute
    DeviceId = [string]$beforeInfo.DeviceId
    SwitchName = $target.Name
    WritableIndex = $target.Index
    AscomId = $target.Id
    InitialValue = $target.Value
    OffSeconds = $OffSeconds
    Voltage = @($beforeInfo.ReadonlySwitches | Where-Object Name -EQ 'VOLTAGE(V)')[0].Value
}
if (-not $Execute) {
    return $plan
}

$offConfirmed = $false
try {
    Set-SwitchValue -Index $target.Index -Value 0
    [void](Wait-SwitchValue -Index $target.Index -ExpectedValue 0 -Baseline $baseline)
    $offConfirmed = $true
    Start-Sleep -Seconds $OffSeconds
} finally {
    Set-SwitchValue -Index $target.Index -Value 1
}

$restored = Wait-SwitchValue -Index $target.Index -ExpectedValue 1 -Baseline $baseline
$finalSnapshot = @($restored.Snapshot)
# The SVBONY aggregate "Time sequence" value can lag an individual output transition.
Start-Sleep -Seconds 2
$restored = Wait-SwitchValue -Index $target.Index -ExpectedValue 1 -Baseline $baseline
$finalSnapshot = @($restored.Snapshot)
for ($index = 0; $index -lt $baseline.Count; $index++) {
    if ($baseline[$index].TargetValue -ne $finalSnapshot[$index].TargetValue -or
        $baseline[$index].Value -ne $finalSnapshot[$index].Value) {
        throw "Final switch state differs from baseline at index $index " +
            "($($baseline[$index].Name))."
    }
}

[pscustomobject]@{
    TimestampUtc = [DateTime]::UtcNow.ToString('o')
    Executed = $true
    DeviceId = [string]$restored.Info.DeviceId
    SwitchName = $target.Name
    WritableIndex = $target.Index
    AscomId = $target.Id
    OffConfirmed = $offConfirmed
    RestoredValue = $finalSnapshot[$target.Index].Value
    VoltageBefore = $plan.Voltage
    VoltageAfter = @(
        $restored.Info.ReadonlySwitches | Where-Object Name -EQ 'VOLTAGE(V)'
    )[0].Value
}
