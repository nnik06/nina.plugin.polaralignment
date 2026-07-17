param(
    [ValidateSet("Probe", "Phd2Drift", "Cycle", "BurstThenDrift", "Stability")]
    [string]$Mode = "Probe",
    [string]$NinaBaseUrl = "",
    [int[]]$NinaCandidatePorts = @(1888, 1889, 5000, 5001, 59590, 8080, 8081, 9000),
    [string]$Phd2Host = "127.0.0.1",
    [int]$Phd2Port = 4400,
    [double]$DriftMinutes = 12,
    [int]$Cycles = 3,
    [int]$RepeatBursts = 1,
    [datetime]$StopAt = [datetime]::MaxValue,
    [int]$SettleSeconds = 60,
    [int]$TppaInterRunSettleSeconds = 10,
    [double]$StabilityCadenceMinutes = 10,
    [int]$StabilityBlocks = 0,
    [int]$TppaTimeoutMinutes = 12,
    [int]$TppaAutoStopSeconds = 0,
    [int]$PlateSolveFailureBackoffCount = 8,
    [bool]$EnableExplicitCloudPreflight = $true,
    [int]$PlateSolveFailureBackoffMinutes = 30,
    [bool]$RequireRecentAutofocus = $true,
    [int]$MaxAutofocusAgeMinutes = 90,
    [string]$AutofocusLogDirectory = "",
    [string]$NinaLogDirectory = "",
    [string]$SequencePath = "",
    [string]$OutputRoot = "$env:USERPROFILE\Documents\TPPA-PHD2-tests"
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"
$script:RpcId = 1000
$script:RunLog = $null
$script:LastSequenceStartLocal = $null
$script:LastWaitReason = "NotStarted"

function New-TestFolder {
    $dir = Join-Path $OutputRoot ("run-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $script:RunLog = Join-Path $dir "supervisor.log"
    return $dir
}

function Log {
    param([string]$Text)
    $line = "{0:O} {1}" -f (Get-Date), $Text
    Write-Host $line
    if ($script:RunLog) { Add-Content -LiteralPath $script:RunLog -Value $line -Encoding UTF8 }
}

function Get-LatestSuccessfulAutofocus {
    $directory = $AutofocusLogDirectory
    if (-not $directory) {
        $directory = Join-Path $env:LOCALAPPDATA "NINA\AutoFocus"
    }
    if (-not (Test-Path -LiteralPath $directory -PathType Container)) { return $null }

    foreach ($file in @(Get-ChildItem -LiteralPath $directory -File -Filter "*.json" -ErrorAction Stop |
        Sort-Object LastWriteTime -Descending)) {
        try {
            $result = Get-Content -LiteralPath $file.FullName -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
            if (-not $result.PSObject.Properties["Timestamp"] -or
                -not $result.PSObject.Properties["FinalHFR"] -or
                -not $result.PSObject.Properties["CalculatedFocusPoint"]) {
                continue
            }

            $rawTimestamp = $result.Timestamp
            if ($rawTimestamp -is [datetimeoffset]) {
                $timestamp = [datetimeoffset]$rawTimestamp
            } elseif ($rawTimestamp -is [datetime]) {
                $timestamp = [datetimeoffset]([datetime]$rawTimestamp)
            } else {
                $timestamp = [datetimeoffset]::Parse(
                    [string]$rawTimestamp,
                    [Globalization.CultureInfo]::InvariantCulture,
                    [Globalization.DateTimeStyles]::RoundtripKind)
            }
            $hfr = [double]$result.FinalHFR
            $position = [double]$result.CalculatedFocusPoint.Position
            if ($hfr -le 0 -or [double]::IsNaN($hfr) -or [double]::IsInfinity($hfr) -or
                [double]::IsNaN($position) -or [double]::IsInfinity($position)) {
                continue
            }

            return [pscustomobject]@{
                Path = $file.FullName
                Timestamp = $timestamp
                FinalHFR = $hfr
                Position = $position
                Filter = if ($result.PSObject.Properties["Filter"]) { [string]$result.Filter } else { "" }
                Temperature = if ($result.PSObject.Properties["Temperature"]) { [double]$result.Temperature } else { [double]::NaN }
            }
        } catch {
            Log "Ignoring unreadable autofocus result '$($file.FullName)': $($_.Exception.Message)"
        }
    }
    return $null
}

function Test-NinaAutofocusCompletionLog {
    param([datetimeoffset]$Timestamp)

    $directory = $NinaLogDirectory
    if (-not $directory) {
        $directory = Join-Path $env:LOCALAPPDATA "NINA\Logs"
    }
    if (-not (Test-Path -LiteralPath $directory -PathType Container)) { return $false }

    $focusLocal = $Timestamp.LocalDateTime
    $candidateFiles = @(Get-ChildItem -LiteralPath $directory -File -Filter "*.log" -ErrorAction Stop |
        Where-Object { $_.LastWriteTime -ge $focusLocal.AddMinutes(-5) } |
        Sort-Object LastWriteTime -Descending)
    foreach ($file in $candidateFiles) {
        try {
            foreach ($line in @(Read-NinaLogSharedTail -Path $file.FullName -Count 100000)) {
                $successMarker =
                    $line -match '\|FocuserMediator[.]cs\|BroadcastSuccessfulAutoFocusRun\|' -or
                    ($line -match '\|HocusFocusVM[.]cs\|AutoFocusEngine_Completed\|' -and $line -match 'AutoFocus completed') -or
                    ($line -match '\|AutoFocusVM[.]cs\|StartAutoFocus\|' -and $line -match '\|AutoFocus completed$')
                if (-not $successMarker) { continue }

                $timestampMatch = [regex]::Match($line, '^(?<timestamp>\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:[.]\d+)?)\|')
                if (-not $timestampMatch.Success) { continue }
                $lineTimestamp = [datetime]::Parse(
                    $timestampMatch.Groups['timestamp'].Value,
                    [Globalization.CultureInfo]::InvariantCulture,
                    [Globalization.DateTimeStyles]::AssumeLocal)
                if ([Math]::Abs(($lineTimestamp - $focusLocal).TotalSeconds) -le 30) {
                    return $true
                }
            }
        } catch {
            Log "Ignoring unreadable NINA log '$($file.FullName)' during autofocus-success correlation: $($_.Exception.Message)"
        }
    }
    return $false
}

function Assert-RecentSuccessfulAutofocus {
    if (-not $RequireRecentAutofocus) {
        Log "Focus preflight disabled by -RequireRecentAutofocus:false."
        return
    }
    if ($MaxAutofocusAgeMinutes -le 0) {
        throw "MaxAutofocusAgeMinutes must be greater than zero when focus preflight is enabled."
    }

    $focus = Get-LatestSuccessfulAutofocus
    if ($null -eq $focus) {
        throw "Focus preflight failed: no valid successful NINA autofocus result was found. Run autofocus once, then restart the diagnostic."
    }

    $age = [datetimeoffset]::Now.Subtract([datetimeoffset]$focus.Timestamp).TotalMinutes
    if ($age -lt -5) {
        throw "Focus preflight failed: latest autofocus timestamp is in the future ($($focus.Timestamp.ToString('O'))). Check the Mele clock."
    }
    if ($age -gt $MaxAutofocusAgeMinutes) {
        throw ("Focus preflight failed: latest successful autofocus is {0:N1} minutes old (limit {1} minutes). Run autofocus once, then restart the diagnostic." -f $age, $MaxAutofocusAgeMinutes)
    }
    if (-not (Test-NinaAutofocusCompletionLog -Timestamp $focus.Timestamp)) {
        throw "Focus preflight failed: the latest autofocus report is not paired with a NINA successful-autofocus event. The run may have been rejected; run autofocus once successfully, then restart the diagnostic."
    }

    $temperatureText = if ([double]::IsNaN($focus.Temperature)) { "unknown" } else { "{0:N1} C" -f $focus.Temperature }
    Log ("Focus preflight passed: autofocus age={0:N1}min, final HFR={1:N3}, position={2:N0}, filter='{3}', temperature={4}." -f $age, $focus.FinalHFR, $focus.Position, $focus.Filter, $temperatureText)
}

function Test-StopWindow {
    if ((Get-Date) -ge $StopAt) {
        Log "Supervisor stop time reached: $StopAt"
        return $true
    }
    return $false
}

function Test-Port {
    param([string]$HostName, [int]$Port)
    try {
        $client = [Net.Sockets.TcpClient]::new()
        $async = $client.BeginConnect($HostName, $Port, $null, $null)
        if (-not $async.AsyncWaitHandle.WaitOne(1500, $false)) { $client.Close(); return $false }
        $client.EndConnect($async)
        $client.Close()
        return $true
    } catch { return $false }
}

function Invoke-Nina {
    param([string]$Base, [string]$Path, [string]$Method = "GET", [object]$Body = $null, [int]$TimeoutSec = 5)
    $req = @{ Uri = ($Base.TrimEnd("/") + $Path); Method = $Method; TimeoutSec = $TimeoutSec }
    if ($null -ne $Body) { $req.ContentType = "application/json"; $req.Body = $Body | ConvertTo-Json -Depth 20 -Compress }
    Invoke-RestMethod @req
}

function Find-Nina {
    if ($NinaBaseUrl) {
        try { Invoke-Nina -Base $NinaBaseUrl -Path "/version" -TimeoutSec 2 | Out-Null; return $NinaBaseUrl.TrimEnd("/") }
        catch { Log "Configured NINA API did not answer at ${NinaBaseUrl}: $($_.Exception.Message)" }
    }
    $candidateHosts = New-Object System.Collections.Generic.List[string]
    foreach ($hostName in @("127.0.0.1", "localhost")) { [void]$candidateHosts.Add($hostName) }
    try {
        Get-NetIPAddress -AddressFamily IPv4 -ErrorAction Stop |
            Where-Object { $_.IPAddress -notlike "169.254.*" -and $_.IPAddress -ne "0.0.0.0" } |
            ForEach-Object { if (-not $candidateHosts.Contains($_.IPAddress)) { [void]$candidateHosts.Add($_.IPAddress) } }
    } catch {
    }
    foreach ($hostName in $candidateHosts) {
        foreach ($port in $NinaCandidatePorts) {
            if (-not (Test-Port -HostName $hostName -Port $port)) { continue }
            $portBase = ("http://{0}:{1}" -f $hostName, $port)
            foreach ($prefix in @("", "/v2/api", "/api")) {
                $base = $portBase + $prefix
                foreach ($path in @("/version", "/version/nina", "/sequence/state", "/application/logs")) {
                    try { Invoke-Nina -Base $base -Path $path -TimeoutSec 2 | Out-Null; Log "Found NINA API at $base using $path"; return $base }
                    catch {}
                }
            }
        }
    }
    return $null
}

function Connect-Phd2 {
    $client = [Net.Sockets.TcpClient]::new()
    $client.Connect($Phd2Host, $Phd2Port)
    $stream = $client.GetStream()
    $stream.ReadTimeout = 1000
    $stream.WriteTimeout = 3000
    $reader = [IO.StreamReader]::new($stream, [Text.Encoding]::UTF8)
    $writer = [IO.StreamWriter]::new($stream, [Text.UTF8Encoding]::new($false))
    $writer.NewLine = "`r`n"
    $writer.AutoFlush = $true
    [pscustomobject]@{ Client = $client; Reader = $reader; Writer = $writer }
}

function Read-Phd2Line {
    param($Conn, [int]$TimeoutMs = 1000)
    $Conn.Client.GetStream().ReadTimeout = $TimeoutMs
    try { return $Conn.Reader.ReadLine() } catch { return $null }
}

function Send-Phd2 {
    param($Conn, [string]$Method, [object]$Params = $null)
    $script:RpcId += 1
    $msg = [ordered]@{ method = $Method; id = $script:RpcId }
    if ($null -ne $Params) { $msg.params = $Params }
    $Conn.Writer.WriteLine(($msg | ConvertTo-Json -Depth 20 -Compress))
    return $script:RpcId
}

function Wait-Phd2 {
    param($Conn, [int]$Id, [int]$TimeoutSec = 8, [string]$Jsonl = "")
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        $line = Read-Phd2Line -Conn $Conn -TimeoutMs 1000
        if (-not $line) { continue }
        if ($Jsonl) { Add-Content -LiteralPath $Jsonl -Value $line -Encoding UTF8 }
        try {
            $obj = $line | ConvertFrom-Json
            if (($obj.PSObject.Properties.Name -contains "id") -and ([int]$obj.id -eq $Id)) { return $obj }
        } catch {}
    }
    throw "Timed out waiting for PHD2 RPC id $Id"
}

function Invoke-Phd2 {
    param($Conn, [string]$Method, [object]$Params = $null, [int]$TimeoutSec = 8, [string]$Jsonl = "")
    $id = Send-Phd2 -Conn $Conn -Method $Method -Params $Params
    Wait-Phd2 -Conn $Conn -Id $id -TimeoutSec $TimeoutSec -Jsonl $Jsonl
}

function Get-JsonValue {
    param($Object, [string]$Name)
    if ($null -eq $Object) { return "" }
    $prop = $Object.PSObject.Properties[$Name]
    if ($null -eq $prop) { return "" }
    if ($null -eq $prop.Value) { return "" }
    return $prop.Value
}

function ConvertTo-CsvCell {
    param($Value)
    if ($null -eq $Value) { return "" }
    $text = [string]$Value
    if ($text.Contains('"') -or $text.Contains(",") -or $text.Contains("`n") -or $text.Contains("`r")) {
        return '"' + $text.Replace('"', '""') + '"'
    }
    return $text
}

function Capture-Phd2Drift {
    param([string]$OutDir, [double]$Minutes, [string]$Label)
    $jsonl = Join-Path $OutDir "$Label-phd2-events.jsonl"
    $csv = Join-Path $OutDir "$Label-phd2-guidesteps.csv"
    "timestamp_utc,elapsed_s,frame,ra_raw_px,dec_raw_px,ra_guide_px,dec_guide_px,ra_ms,dec_ms,snr,hfd,star_mass,event_json" | Set-Content -LiteralPath $csv -Encoding UTF8

    Log "Connecting to PHD2 at ${Phd2Host}:${Phd2Port}"
    $conn = Connect-Phd2
    $originalGuideOutput = $true
    $preState = "Unknown"
    $guideOutputConfirmed = $false
    $stopAtEnd = $true

    try {
        $drainUntil = (Get-Date).AddSeconds(2)
        while ((Get-Date) -lt $drainUntil) { $line = Read-Phd2Line -Conn $conn -TimeoutMs 250; if ($line) { Add-Content -LiteralPath $jsonl -Value $line -Encoding UTF8 } }

        $appState = Invoke-Phd2 -Conn $conn -Method "get_app_state" -Jsonl $jsonl
        if ($appState.PSObject.Properties.Name -contains "result") { $preState = [string]$appState.result }
        Log "PHD2 initial app state: $preState"
        if ($preState -eq "Calibrating") { throw "PHD2 is currently calibrating; refusing to interrupt it." }

        try { $equip = Invoke-Phd2 -Conn $conn -Method "get_current_equipment" -Jsonl $jsonl; Log ("PHD2 equipment: " + ($equip | ConvertTo-Json -Compress -Depth 8)) }
        catch { Log "PHD2 equipment query failed" }

        $connected = Invoke-Phd2 -Conn $conn -Method "get_connected" -Jsonl $jsonl
        Log ("PHD2 get_connected: " + ($connected | ConvertTo-Json -Compress -Depth 5))
        if (($connected.PSObject.Properties.Name -contains "result") -and (-not [bool]$connected.result)) { throw "PHD2 equipment is not connected; refusing drift capture." }

        $calibrated = Invoke-Phd2 -Conn $conn -Method "get_calibrated" -Jsonl $jsonl
        Log ("PHD2 get_calibrated: " + ($calibrated | ConvertTo-Json -Compress -Depth 5))
        if (($calibrated.PSObject.Properties.Name -contains "result") -and (-not [bool]$calibrated.result)) { throw "PHD2 is not calibrated; refusing to call guide because PHD2 could run calibration and move the mount." }
        try { $calData = Invoke-Phd2 -Conn $conn -Method "get_calibration_data" -Params @("Mount") -Jsonl $jsonl; Log ("PHD2 calibration data: " + ($calData | ConvertTo-Json -Compress -Depth 8)) }
        catch { Log "PHD2 calibration data read failed: $($_.Exception.Message)" }

        try {
            $guideOutput = Invoke-Phd2 -Conn $conn -Method "get_guide_output_enabled" -Jsonl $jsonl
            if ($guideOutput.PSObject.Properties.Name -contains "result") { $originalGuideOutput = [bool]$guideOutput.result; $guideOutputConfirmed = $true }
        } catch { Log "PHD2 get_guide_output_enabled failed; fail-safe restore will enable guide output: $($_.Exception.Message)"; $originalGuideOutput = $true }
        Log "PHD2 original guide-output enabled: $originalGuideOutput confirmed=$guideOutputConfirmed"

        Invoke-Phd2 -Conn $conn -Method "set_guide_output_enabled" -Params @($false) -Jsonl $jsonl | Out-Null
        Log "PHD2 guide outputs disabled"

        $guideParams = @{ settle = @{ pixels = 99.0; time = 0; timeout = 10 }; recalibrate = $false }
        Invoke-Phd2 -Conn $conn -Method "guide" -Params $guideParams -TimeoutSec 15 -Jsonl $jsonl | Out-Null
        Log "PHD2 guide command accepted"

        $start = Get-Date
        $mark = [ordered]@{ Event = "SupervisorMark"; Label = $Label; CaptureStartUtc = $start.ToUniversalTime().ToString("O"); PrePHD2State = $preState; GuideOutputWasEnabled = $originalGuideOutput }
        Add-Content -LiteralPath $jsonl -Value ($mark | ConvertTo-Json -Compress) -Encoding UTF8
        Log "PHD2 drift capture marker written for $Label at $($start.ToUniversalTime().ToString("O"))"

        $end = $start.AddMinutes($Minutes)
        $firstStepDeadline = $start.AddSeconds(30)
        $steps = 0
        while ((Get-Date) -lt $end) {
            $line = Read-Phd2Line -Conn $conn -TimeoutMs 1000
            if (-not $line) {
                if ($steps -eq 0 -and (Get-Date) -gt $firstStepDeadline) { Log "ERROR: no PHD2 GuideStep received within 30 seconds; capture is probably unusable."; break }
                continue
            }
            Add-Content -LiteralPath $jsonl -Value $line -Encoding UTF8
            try {
                $obj = $line | ConvertFrom-Json
                if ((Get-JsonValue -Object $obj -Name "Event") -eq "GuideStep") {
                    $steps += 1
                    $elapsed = ((Get-Date) - $start).TotalSeconds
                    $raDuration = Get-JsonValue -Object $obj -Name "RADuration"
                    $decDuration = Get-JsonValue -Object $obj -Name "DECDuration"
                    $cells = @(
                        (Get-Date).ToUniversalTime().ToString("O"),
                        ("{0:F3}" -f $elapsed),
                        (Get-JsonValue -Object $obj -Name "Frame"),
                        (Get-JsonValue -Object $obj -Name "RADistanceRaw"),
                        (Get-JsonValue -Object $obj -Name "DECDistanceRaw"),
                        (Get-JsonValue -Object $obj -Name "RADistanceGuide"),
                        (Get-JsonValue -Object $obj -Name "DECDistanceGuide"),
                        $raDuration,
                        $decDuration,
                        (Get-JsonValue -Object $obj -Name "SNR"),
                        (Get-JsonValue -Object $obj -Name "HFD"),
                        (Get-JsonValue -Object $obj -Name "StarMass"),
                        $line
                    )
                    $row = ($cells | ForEach-Object { ConvertTo-CsvCell $_ }) -join ","
                    Add-Content -LiteralPath $csv -Value $row -Encoding UTF8
                    if (($raDuration -ne "" -and [double]$raDuration -gt 0) -or ($decDuration -ne "" -and [double]$decDuration -gt 0)) {
                        Log "WARNING: Guiding pulses (RA: ${raDuration}ms, DEC: ${decDuration}ms) detected! Another client (e.g., NINA) may have re-enabled guide output."
                    }
                } elseif ($obj.Event -eq "Alert") { Log "PHD2 alert: $($obj.Msg)"
                } elseif ($obj.Event -eq "AppState") { Log "PHD2 state changed to: $($obj.State)" }
            } catch {}
        }
        if ($steps -eq 0) { Log "ERROR: PHD2 drift capture ended with zero GuideStep rows." }
        Log "PHD2 drift capture finished. GuideStep rows: $steps"
    } finally {
        try { Invoke-Phd2 -Conn $conn -Method "set_guide_output_enabled" -Params @($originalGuideOutput) -TimeoutSec 5 -Jsonl $jsonl | Out-Null; Log "PHD2 guide-output restored to $originalGuideOutput" }
        catch { Log "WARNING: PHD2 guide-output restore failed; manually verify guide output before imaging: $($_.Exception.Message)" }

        if ($preState -eq "Guiding") { $stopAtEnd = $false; Log "PHD2 was already guiding before capture; leaving capture/guiding running after restoring guide output." }
        if ($stopAtEnd) {
            try { Invoke-Phd2 -Conn $conn -Method "stop_capture" -TimeoutSec 5 -Jsonl $jsonl | Out-Null; Log "PHD2 capture stopped" } catch { Log "PHD2 stop_capture failed: $($_.Exception.Message)" }
            if ($preState -eq "Looping") { try { Invoke-Phd2 -Conn $conn -Method "loop" -TimeoutSec 5 -Jsonl $jsonl | Out-Null; Log "PHD2 looping restored" } catch { Log "PHD2 loop restore failed: $($_.Exception.Message)" } }
        }
        try { $conn.Client.Close() } catch {}
    }
}

function Assert-ApiSuccess {
    param([object]$Response, [string]$Operation)
    if ($Response.PSObject.Properties["Success"] -and -not $Response.Success) {
        throw "$Operation failed: $($Response.Error)"
    }
}

function Load-NinaSequence {
    param([string]$Base)
    if (-not $SequencePath) { return }

    $sequenceName = [IO.Path]::GetFileNameWithoutExtension($SequencePath)
    Log "Loading NINA sequence '$sequenceName' from $SequencePath"
    $loadPath = "/sequence/load?sequenceName=$([uri]::EscapeDataString($sequenceName))"
    $loadResponse = Invoke-Nina -Base $Base -Path $loadPath -Method "GET" -TimeoutSec 30
    Assert-ApiSuccess -Response $loadResponse -Operation "NINA sequence load"
    Log "NINA sequence load response: $($loadResponse.Response)"
}

function Start-NinaSequence {
    param([string]$Base)
    if ($SequencePath) {
        Load-NinaSequence -Base $Base
    } else {
        Log "No -SequencePath provided; attempting to start current sequence. If it is already FINISHED/SKIPPED, reload it in NINA or provide -SequencePath."
        try {
            $resetResponse = Invoke-Nina -Base $Base -Path "/sequence/reset" -Method "POST" -Body @{} -TimeoutSec 20
            Assert-ApiSuccess -Response $resetResponse -Operation "NINA sequence reset"
            Log "NINA sequence reset before start"
        } catch { Log "NINA sequence reset warning: $($_.Exception.Message)" }
    }
    Log "Starting NINA sequence"
    $script:LastSequenceStartLocal = Get-Date
    try {
        $startResponse = Invoke-Nina -Base $Base -Path "/sequence/start" -Method "POST" -Body @{ skipValidation = $true } -TimeoutSec 20
        Assert-ApiSuccess -Response $startResponse -Operation "NINA sequence start"
    } catch {
        $startResponse = Invoke-Nina -Base $Base -Path "/sequence/start?skipValidation=true" -Method "GET" -TimeoutSec 20
        Assert-ApiSuccess -Response $startResponse -Operation "NINA sequence start"
    }
}

function Get-NinaSequenceStatuses {
    param([object]$Node)
    $statuses = New-Object System.Collections.Generic.List[string]
    if ($null -eq $Node) { return $statuses }
    if ($Node -is [System.Array]) {
        foreach ($item in $Node) {
            foreach ($status in (Get-NinaSequenceStatuses -Node $item)) { [void]$statuses.Add($status) }
        }
        return $statuses
    }
    if ($Node.PSObject.Properties["Status"]) { [void]$statuses.Add([string]$Node.Status) }
    if ($Node.PSObject.Properties["Response"]) {
        foreach ($status in (Get-NinaSequenceStatuses -Node $Node.Response)) { [void]$statuses.Add($status) }
    }
    if ($Node.PSObject.Properties["Items"]) {
        foreach ($status in (Get-NinaSequenceStatuses -Node $Node.Items)) { [void]$statuses.Add($status) }
    }
    return $statuses
}

function Get-LatestNinaLogPath {
    $logDir = Join-Path $env:LOCALAPPDATA "NINA\Logs"
    Get-ChildItem -LiteralPath $logDir -File -Filter "*.log" -ErrorAction Stop |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1 -ExpandProperty FullName
}

function Read-NinaLogSharedTail {
    param([string]$Path, [int]$Count = 5000)
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
    $reader = [IO.StreamReader]::new($stream)
    try {
        $text = $reader.ReadToEnd()
    } finally {
        $reader.Dispose()
        $stream.Dispose()
    }
    @($text -split "`r?`n" | Select-Object -Last $Count)
}

function Test-NinaLogSequenceFinished {
    param([datetime]$SinceLocal)
    try {
        $logPath = Get-LatestNinaLogPath
        $lines = Read-NinaLogSharedTail -Path $logPath -Count 2000
        $sawStart = $false
        foreach ($line in $lines) {
            if ($line.Length -lt 23) { continue }
            $stampText = $line.Substring(0, 23)
            $stamp = [datetime]::MinValue
            if (-not [datetime]::TryParse($stampText, [ref]$stamp)) { continue }
            if ($stamp -lt $SinceLocal.AddSeconds(-2)) { continue }
            if ($line -match "Advanced Sequence starting|Starting Category: Polar Alignment") { $sawStart = $true }
            if ($line -match "Advanced Sequence finished") {
                Log "NINA log completion marker found in $([IO.Path]::GetFileName($logPath)): $line"
                return $true
            }
        }
    } catch { Log "NINA log completion check failed: $($_.Exception.Message)" }
    return $false
}

function Test-NinaSolveFailureBackoff {
    param([datetime]$SinceLocal, [int]$Threshold)
    if ($Threshold -le 0) { return $false }

    try {
        $logPath = Get-LatestNinaLogPath
        $lines = Read-NinaLogSharedTail -Path $logPath -Count 5000
        $failuresSinceProgress = 0
        foreach ($line in $lines) {
            if ($line.Length -lt 23) { continue }
            $stampText = $line.Substring(0, 23)
            $stamp = [datetime]::MinValue
            if (-not [datetime]::TryParse($stampText, [ref]$stamp)) { continue }
            if ($stamp -lt $SinceLocal.AddSeconds(-2)) { continue }

            if ($line -match "Platesolve successful:|TPPA fresh 3-point calculated error|TPPA correction-loop calculated error|Advanced Sequence finished") {
                $failuresSinceProgress = 0
                continue
            }

            if ($line -match "ASTAP - Plate solve failed") {
                $failuresSinceProgress += 1
                if ($failuresSinceProgress -ge $Threshold) {
                    Log "Detected $failuresSinceProgress consecutive ASTAP plate-solve failures without TPPA progress in $([IO.Path]::GetFileName($logPath))."
                    return $true
                }
            }
        }
    } catch {
        Log "NINA solve-failure backoff check failed: $($_.Exception.Message)"
    }

    return $false
}

function Test-NinaExplicitCloudFailure {
    param([datetime]$SinceLocal)
    if (-not $EnableExplicitCloudPreflight) { return $false }

    try {
        $logPath = Get-LatestNinaLogPath
        $lines = Read-NinaLogSharedTail -Path $logPath -Count 5000
        $lastTimestamp = [datetime]::MinValue
        $pendingAstapFailure = $false

        foreach ($line in $lines) {
            $stamp = [datetime]::MinValue
            $hasTimestamp = $line.Length -ge 23 -and [datetime]::TryParse($line.Substring(0, 23), [ref]$stamp)
            if ($hasTimestamp) {
                $lastTimestamp = $stamp
                if ($stamp -lt $SinceLocal.AddSeconds(-2)) {
                    $pendingAstapFailure = $false
                    continue
                }

                if ($line -match "Platesolve successful:|TPPA fresh 3-point calculated error|TPPA correction-loop calculated error|Advanced Sequence finished") {
                    $pendingAstapFailure = $false
                    continue
                }

                $pendingAstapFailure = $line -match "ASTAP - Plate solve failed"
                if ($pendingAstapFailure -and $line -match "Not enough stars") {
                    Log "ASTAP explicitly reported too few stars in $([IO.Path]::GetFileName($logPath)); treating the current frame as cloud-obscured."
                    return $true
                }
                continue
            }

            if ($lastTimestamp -lt $SinceLocal.AddSeconds(-2)) { continue }
            if ($pendingAstapFailure -and $line.Trim() -match "^Not enough stars[.]?$") {
                Log "ASTAP explicitly reported too few stars in $([IO.Path]::GetFileName($logPath)); treating the current frame as cloud-obscured."
                return $true
            }
            if (-not [string]::IsNullOrWhiteSpace($line)) {
                $pendingAstapFailure = $false
            }
        }
    } catch {
        Log "NINA explicit cloud-preflight check failed: $($_.Exception.Message)"
    }

    return $false
}

function Wait-CloudBackoff {
    param([int]$Minutes)
    if ($Minutes -le 0) { return (-not (Test-StopWindow)) }

    $end = (Get-Date).AddMinutes($Minutes)
    if ($end -ge $StopAt) {
        Log "Cloud/solve backoff would end at $($end.ToString("O")), after supervisor stop time $($StopAt.ToString("O")); ending instead of scheduling another TPPA retry."
        return $false
    }
    Log "Cloud/solve backoff: pausing TPPA restarts until $($end.ToString("O"))"
    while ((Get-Date) -lt $end) {
        if (Test-StopWindow) { return $false }
        $remaining = [Math]::Max(1, [int][Math]::Ceiling(($end - (Get-Date)).TotalSeconds))
        Start-Sleep -Seconds ([Math]::Min(60, $remaining))
    }
    return $true
}

function Get-NinaLogLinesSince {
    param([datetime]$SinceLocal, [int]$Count = 5000)
    $logPath = Get-LatestNinaLogPath
    $result = New-Object System.Collections.Generic.List[string]
    foreach ($line in (Read-NinaLogSharedTail -Path $logPath -Count $Count)) {
        if ($line.Length -lt 23) { continue }
        $stamp = [datetime]::MinValue
        if (-not [datetime]::TryParse($line.Substring(0, 23), [ref]$stamp)) { continue }
        if ($stamp -ge $SinceLocal.AddSeconds(-2)) { [void]$result.Add($line) }
    }
    return $result
}

function Stop-NinaSequence {
    param([string]$Base)
    try { Invoke-Nina -Base $Base -Path "/sequence/stop" -Method "POST" -Body @{} -TimeoutSec 10 | Out-Null }
    catch { Invoke-Nina -Base $Base -Path "/sequence/stop" -Method "GET" -TimeoutSec 10 | Out-Null }
}

function Wait-NinaFreshDetermination {
    param([string]$Base, [datetime]$SinceLocal)
    $deadline = $SinceLocal.AddMinutes($TppaTimeoutMinutes)
    $movementPattern = 'Nudging Avalon Polar Alignment System along [XY] axis|Sending command: \$J=.*[XY]'
    $script:LastWaitReason = "Running"
    while ((Get-Date) -lt $deadline) {
        if (Test-StopWindow) {
            Stop-NinaSequence -Base $Base
            $script:LastWaitReason = "StopWindow"
            return $null
        }

        $lines = @(Get-NinaLogLinesSince -SinceLocal $SinceLocal -Count 6000)
        $movement = @($lines | Where-Object { $_ -match $movementPattern })
        if ($movement.Count -gt 0) {
            Stop-NinaSequence -Base $Base
            throw "UPAS movement was detected during the fixed-tripod stability test: $($movement[-1])"
        }

        $fresh = @($lines | Where-Object {
            if ($_ -notmatch 'TPPA fresh 3-point calculated error:' -or $_.Length -lt 23) { return $false }
            $freshStamp = [datetime]::MinValue
            if (-not [datetime]::TryParse($_.Substring(0, 23), [ref]$freshStamp)) { return $false }
            return $freshStamp -ge $SinceLocal
        } | Select-Object -Last 1)
        if ($fresh.Count -gt 0) {
            Log "Fresh TPPA determination captured; stopping before continuous correction: $($fresh[0])"
            Stop-NinaSequence -Base $Base
            Start-Sleep -Seconds 2
            $postStopLines = @(Get-NinaLogLinesSince -SinceLocal $SinceLocal -Count 6000)
            $postStopMovement = @($postStopLines | Where-Object { $_ -match $movementPattern })
            if ($postStopMovement.Count -gt 0) {
                throw "UPAS movement occurred before the stability stop completed: $($postStopMovement[-1])"
            }
            $script:LastWaitReason = "Completed"
            return [pscustomobject]@{ CapturedAt = Get-Date; Line = $fresh[0] }
        }

        if (Test-NinaExplicitCloudFailure -SinceLocal $SinceLocal) {
            Log "Explicit cloud preflight failed; stopping this TPPA run before exhausting the normal solve-failure budget."
            Stop-NinaSequence -Base $Base
            if (Wait-CloudBackoff -Minutes $PlateSolveFailureBackoffMinutes) {
                $script:LastWaitReason = "CloudBackoff"
            } else {
                $script:LastWaitReason = "StopWindow"
            }
            return $null
        }

        if (Test-NinaSolveFailureBackoff -SinceLocal $SinceLocal -Threshold $PlateSolveFailureBackoffCount) {
            Stop-NinaSequence -Base $Base
            if (Wait-CloudBackoff -Minutes $PlateSolveFailureBackoffMinutes) {
                $script:LastWaitReason = "CloudBackoff"
            } else {
                $script:LastWaitReason = "StopWindow"
            }
            return $null
        }
        Start-Sleep -Seconds 2
    }

    Stop-NinaSequence -Base $Base
    $script:LastWaitReason = "Timeout"
    Log "Timed out waiting for a fresh TPPA three-point determination; stopping this stability block."
    return $null
}

function Wait-NinaDone {
    param([string]$Base)
    $deadline = (Get-Date).AddMinutes($TppaTimeoutMinutes)
    $seenRunning = $false
    $autoStopSent = $false
    $script:LastWaitReason = "Running"
    if ($script:LastSequenceStartLocal -eq $null) { $script:LastSequenceStartLocal = Get-Date }
    while ((Get-Date) -lt $deadline) {
        if (Test-StopWindow) {
            Log "Stop time reached while TPPA was running; asking NINA to stop the sequence."
            try { Invoke-Nina -Base $Base -Path "/sequence/stop" -Method "POST" -Body @{} -TimeoutSec 10 | Out-Null } catch {}
            $script:LastWaitReason = "StopWindow"
            return $false
        }
        if (Test-NinaExplicitCloudFailure -SinceLocal $script:LastSequenceStartLocal) {
            Log "Explicit cloud preflight failed; stopping this TPPA run before exhausting the normal solve-failure budget."
            try { Stop-NinaSequence -Base $Base } catch { Log "NINA sequence stop warning after explicit cloud detection: $($_.Exception.Message)" }
            if (Wait-CloudBackoff -Minutes $PlateSolveFailureBackoffMinutes) {
                $script:LastWaitReason = "CloudBackoff"
            } else {
                $script:LastWaitReason = "StopWindow"
            }
            return $false
        }
        if (Test-NinaLogSequenceFinished -SinceLocal $script:LastSequenceStartLocal) {
            $script:LastWaitReason = "Completed"
            return $true
        }
        if (Test-NinaSolveFailureBackoff -SinceLocal $script:LastSequenceStartLocal -Threshold $PlateSolveFailureBackoffCount) {
            Log "Too many plate-solve failures without TPPA progress; stopping this TPPA run and waiting for clouds to pass."
            try { Invoke-Nina -Base $Base -Path "/sequence/stop" -Method "POST" -Body @{} -TimeoutSec 10 | Out-Null } catch {
                try { Invoke-Nina -Base $Base -Path "/sequence/stop" -Method "GET" -TimeoutSec 10 | Out-Null } catch {}
            }
            if (Wait-CloudBackoff -Minutes $PlateSolveFailureBackoffMinutes) {
                $script:LastWaitReason = "CloudBackoff"
            } else {
                $script:LastWaitReason = "StopWindow"
            }
            return $false
        }
        if ($TppaAutoStopSeconds -gt 0 -and -not $autoStopSent -and $seenRunning -and ((Get-Date) - $script:LastSequenceStartLocal).TotalSeconds -ge $TppaAutoStopSeconds) {
            Log "Test auto-stop: TPPA has been running for at least $TppaAutoStopSeconds seconds; asking NINA to stop the sequence"
            try {
                $stopResponse = Invoke-Nina -Base $Base -Path "/sequence/stop" -Method "POST" -Body @{} -TimeoutSec 10
                Assert-ApiSuccess -Response $stopResponse -Operation "NINA sequence stop"
            } catch {
                try {
                    $stopResponse = Invoke-Nina -Base $Base -Path "/sequence/stop" -Method "GET" -TimeoutSec 10
                    Assert-ApiSuccess -Response $stopResponse -Operation "NINA sequence stop"
                } catch { Log "Test auto-stop warning: NINA sequence stop failed: $($_.Exception.Message)" }
            }
            $autoStopSent = $true
        }
        try {
            $state = Invoke-Nina -Base $Base -Path "/sequence/state" -TimeoutSec 3
            $statuses = @(Get-NinaSequenceStatuses -Node $state.Response)
            $hasRunningStatus = $statuses -contains "RUNNING"
            Log "NINA sequence status summary: $($statuses -join ',')"
            if ($hasRunningStatus) { $seenRunning = $true }
            if ($seenRunning -and -not $hasRunningStatus -and $statuses.Count -gt 0) {
                Start-Sleep -Seconds 2
                if (Test-NinaLogSequenceFinished -SinceLocal $script:LastSequenceStartLocal) {
                    $script:LastWaitReason = "Completed"
                    return $true
                }
                return $true
            }
        } catch {
            Log "NINA sequence state read failed; relying on NINA log marker: $($_.Exception.Message)"
        }
        Start-Sleep -Seconds 5
    }
    Log "NINA timeout reached; asking sequence to stop. This cycle's data is suspect and should be excluded."
    try { Invoke-Nina -Base $Base -Path "/sequence/stop" -Method "POST" -Body @{} -TimeoutSec 10 | Out-Null } catch {}
    $script:LastWaitReason = "Timeout"
    return $false
}

$runDir = New-TestFolder
Log "TPPA/PHD2 supervisor started. Mode=$Mode Output=$runDir"

if ($Mode -eq "Probe") {
    $nina = Find-Nina
    if ($nina) {
        Log "NINA API found at $nina"
        foreach ($path in @("/version", "/version/nina", "/application/logs")) { try { Log ("NINA $path OK: " + ((Invoke-Nina -Base $nina -Path $path -TimeoutSec 5) | ConvertTo-Json -Depth 5 -Compress)) } catch { Log "NINA $path failed: $($_.Exception.Message)" } }
        try {
            $state = Invoke-Nina -Base $nina -Path "/sequence/state" -TimeoutSec 20
            $statuses = @(Get-NinaSequenceStatuses -Node $state.Response)
            Log "NINA /sequence/state OK. Status summary: $($statuses -join ',')"
        } catch { Log "NINA /sequence/state failed: $($_.Exception.Message)" }
    } else { Log "NINA API not found" }

    if (Test-Port -HostName $Phd2Host -Port $Phd2Port) {
        Log "PHD2 TCP port is open"
        try { $conn = Connect-Phd2; $appState = Invoke-Phd2 -Conn $conn -Method "get_app_state" -TimeoutSec 5; Log ("PHD2 get_app_state OK: " + ($appState | ConvertTo-Json -Depth 5 -Compress)); $conn.Client.Close() }
        catch { Log "PHD2 RPC probe failed: $($_.Exception.Message)" }
    } else { Log "PHD2 TCP port is closed" }
    Log "Probe complete"
    return
}

if ($Mode -eq "Phd2Drift") { Capture-Phd2Drift -OutDir $runDir -Minutes $DriftMinutes -Label "daylight"; Log "Phd2Drift mode complete"; return }

if ($Mode -eq "Stability") {
    $nina = Find-Nina
    if (-not $nina) { throw "NINA API not found. Start NINA and enable Advanced API first." }
    if ($SequencePath) {
        Load-NinaSequence -Base $nina
        Log "Loaded the requested stability sequence before validating its UPAS pre-seat and start-position settings."
    }
    $state = Invoke-Nina -Base $nina -Path "/sequence/state" -TimeoutSec 20
    $stateJson = $state.Response | ConvertTo-Json -Depth 30 -Compress
    if ($stateJson -match '"PreSeatAzimuthBeforeMeasurement":true') {
        throw "The loaded TPPA sequence has UPAS azimuth pre-seat enabled. Stability mode refuses to start."
    }
    if ($stateJson -match '"StartFromCurrentPosition":true') {
        throw "The loaded TPPA sequence starts from the current position. Stability mode requires fixed configured start coordinates."
    }
    Assert-RecentSuccessfulAutofocus

    $measurementCsv = Join-Path $runDir "tppa-stability-measurements.csv"
    'captured_local,fresh_error_log_line' | Set-Content -LiteralPath $measurementCsv -Encoding UTF8
    Log "Fixed-tripod stability mode armed. Each run will stop immediately after the fresh three-point result, before continuous correction."
    Log "Loaded sequence pre-seat is disabled and uses fixed configured start coordinates. Cadence=${StabilityCadenceMinutes}min; TPPA runs per PHD2 block=$Cycles; stop=$StopAt"

    $block = 0
    while (-not (Test-StopWindow)) {
        $block += 1
        $completed = 0
        for ($cycle = 1; $cycle -le $Cycles; $cycle++) {
            if (Test-StopWindow) { break }
            Log "Stability block ${block}, TPPA ${cycle}/${Cycles}: starting same-position fresh measurement"
            Start-NinaSequence -Base $nina
            $started = Get-Date
            $measurement = Wait-NinaFreshDetermination -Base $nina -SinceLocal $started
            if ($null -eq $measurement) {
                if (Test-StopWindow) { break }
                if ($script:LastWaitReason -eq "StopWindow") {
                    Log "Stability block ${block}, TPPA ${cycle}: stopping because no complete cloud-backoff/retry window remains."
                    break
                }
                if ($script:LastWaitReason -eq "Timeout") {
                    Log "Stability block ${block}, TPPA ${cycle}: stopping after a TPPA determination timeout."
                    break
                }
                Log "Stability block ${block}, TPPA ${cycle}: no usable fresh result; retrying after backoff"
                $cycle -= 1
                continue
            }

            $completed += 1
            $csvLine = '"{0}","{1}"' -f $measurement.CapturedAt.ToString('O'), ([string]$measurement.Line).Replace('"','""')
            Add-Content -LiteralPath $measurementCsv -Value $csvLine -Encoding UTF8

            if ($cycle -lt $Cycles) {
                $nextStart = $started.AddMinutes($StabilityCadenceMinutes)
                while ((Get-Date) -lt $nextStart -and -not (Test-StopWindow)) {
                    Start-Sleep -Seconds ([Math]::Min(30, [Math]::Max(1, [int][Math]::Ceiling(($nextStart - (Get-Date)).TotalSeconds))))
                }
            }
        }

        if ($completed -ne $Cycles -or (Test-StopWindow)) { break }
        $remainingMinutes = ($StopAt - (Get-Date)).TotalMinutes
        if ($remainingMinutes -le ($DriftMinutes + 1)) {
            Log "Not enough dark-window time remains for a full passive PHD2 capture."
            break
        }
        if (Test-Port -HostName $Phd2Host -Port $Phd2Port) {
            Log "Stability block ${block}: capturing passive PHD2 drift for $DriftMinutes minutes"
            Capture-Phd2Drift -OutDir $runDir -Minutes $DriftMinutes -Label ("stability-block-{0:00}" -f $block)
        } else {
            Log "WARNING: PHD2 TCP port is closed; continuing TPPA stability measurements without this drift block."
        }

        if ($StabilityBlocks -gt 0 -and $block -ge $StabilityBlocks) {
            Log "Requested stability block count reached: $StabilityBlocks"
            break
        }
    }
    Log "Stability mode complete. Measurement CSV: $measurementCsv"
    return
}


if ($Mode -eq "BurstThenDrift") {
    $nina = Find-Nina
    if (-not $nina) { throw "NINA API not found. Start NINA and enable Advanced API first." }
    if (-not (Test-Port -HostName $Phd2Host -Port $Phd2Port)) { throw "PHD2 TCP port is closed. Start PHD2 first." }
    $completedBursts = 0
    for ($burst = 1; $burst -le $RepeatBursts; $burst++) {
        if (Test-StopWindow) { break }
        Log "Full cycle ${burst}/${RepeatBursts}: starting TPPA burst of $Cycles run(s)"
        $completed = 0
        for ($cycle = 1; $cycle -le $Cycles; $cycle++) {
            if (Test-StopWindow) { break }
            Log "Full cycle ${burst}/${RepeatBursts}, TPPA ${cycle}/${Cycles}: starting TPPA sequence"
            Start-NinaSequence -Base $nina
            if (-not (Wait-NinaDone -Base $nina)) {
                if ($script:LastWaitReason -eq "CloudBackoff" -and -not (Test-StopWindow)) {
                    Log "Full cycle ${burst}, TPPA ${cycle}: cloud backoff complete; retrying this TPPA run."
                    $cycle -= 1
                    continue
                }
                Log "Full cycle ${burst}, TPPA ${cycle}: TPPA did not finish before timeout; skipping this cycle's PHD2 drift capture and stopping the supervisor"
                break
            }
            $completed += 1
            $doneUtc = (Get-Date).ToUniversalTime().ToString("O")
            Log "Full cycle ${burst}, TPPA ${cycle}: TPPA completion marker UTC $doneUtc"
            if ($cycle -lt $Cycles) {
                Log "Full cycle ${burst}, TPPA ${cycle}: waiting $TppaInterRunSettleSeconds seconds before next fresh TPPA run"
                Start-Sleep -Seconds $TppaInterRunSettleSeconds
            }
        }
        if ($completed -ne $Cycles) {
            Log "Full cycle ${burst}/${RepeatBursts}: only completed $completed/$Cycles TPPA runs; stopping before PHD2"
            break
        }
        $completedBursts += 1
        if (Test-StopWindow) { break }
        Log "Full cycle ${burst}/${RepeatBursts}: TPPA burst complete; settling for $SettleSeconds seconds before passive PHD2 drift capture"
        Start-Sleep -Seconds $SettleSeconds
        if (Test-StopWindow) { break }
        Capture-Phd2Drift -OutDir $runDir -Minutes $DriftMinutes -Label ("full-cycle-{0:00}-post-tppa-burst" -f $burst)
        if ($burst -lt $RepeatBursts) {
            Log "Full cycle ${burst}/${RepeatBursts}: PHD2 capture complete; waiting $TppaInterRunSettleSeconds seconds before next TPPA burst"
            Start-Sleep -Seconds $TppaInterRunSettleSeconds
        }
    }
    Log "BurstThenDrift mode complete; completed full cycles: $completedBursts/$RepeatBursts; TPPA runs per full cycle: $Cycles"
    return
}

if ($Mode -eq "Cycle") {
    $nina = Find-Nina
    if (-not $nina) { throw "NINA API not found. Start NINA and enable Advanced API first." }
    if (-not (Test-Port -HostName $Phd2Host -Port $Phd2Port)) { throw "PHD2 TCP port is closed. Start PHD2 first." }
    for ($cycle = 1; $cycle -le $Cycles; $cycle++) {
        Log "Cycle ${cycle}/${Cycles}: starting TPPA sequence"
        Start-NinaSequence -Base $nina
        if (-not (Wait-NinaDone -Base $nina)) {
            if ($script:LastWaitReason -eq "CloudBackoff" -and -not (Test-StopWindow)) {
                Log "Cycle ${cycle}: cloud backoff complete; retrying this TPPA run."
                $cycle -= 1
                continue
            }
            Log "Cycle ${cycle}: TPPA did not finish before timeout"
            break
        }
        $doneUtc = (Get-Date).ToUniversalTime().ToString("O")
        Log "Cycle ${cycle}: TPPA completion marker UTC $doneUtc"
        Log "Cycle ${cycle}: settling for $SettleSeconds seconds"
        Start-Sleep -Seconds $SettleSeconds
        Capture-Phd2Drift -OutDir $runDir -Minutes $DriftMinutes -Label ("cycle-{0:00}" -f $cycle)
    }
    Log "Cycle mode complete"
}
