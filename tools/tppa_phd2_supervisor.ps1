param(
    [ValidateSet("Probe", "Phd2Drift", "Cycle", "BurstThenDrift")]
    [string]$Mode = "Probe",
    [string]$NinaBaseUrl = "",
    [int[]]$NinaCandidatePorts = @(1888, 1889, 5000, 5001, 59590, 8080, 8081, 9000),
    [string]$Phd2Host = "127.0.0.1",
    [int]$Phd2Port = 4400,
    [double]$DriftMinutes = 8,
    [int]$Cycles = 3,
    [int]$RepeatBursts = 1,
    [datetime]$StopAt = [datetime]::MaxValue,
    [int]$SettleSeconds = 60,
    [int]$TppaInterRunSettleSeconds = 10,
    [int]$TppaTimeoutMinutes = 12,
    [int]$TppaAutoStopSeconds = 0,
    [int]$PlateSolveFailureBackoffCount = 8,
    [int]$PlateSolveFailureBackoffMinutes = 30,
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

function Start-NinaSequence {
    param([string]$Base)
    if ($SequencePath) {
        $sequenceName = [IO.Path]::GetFileNameWithoutExtension($SequencePath)
        Log "Loading NINA sequence '$sequenceName' from $SequencePath"
        $loadPath = "/sequence/load?sequenceName=$([uri]::EscapeDataString($sequenceName))"
        $loadResponse = Invoke-Nina -Base $Base -Path $loadPath -Method "GET" -TimeoutSec 30
        Assert-ApiSuccess -Response $loadResponse -Operation "NINA sequence load"
        Log "NINA sequence load response: $($loadResponse.Response)"
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

function Test-NinaLogSequenceFinished {
    param([datetime]$SinceLocal)
    try {
        $logPath = Get-LatestNinaLogPath
        $lines = Get-Content -LiteralPath $logPath -Tail 2000 -ErrorAction Stop
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
        $lines = Get-Content -LiteralPath $logPath -Tail 5000 -ErrorAction Stop
        $failuresSinceProgress = 0
        foreach ($line in $lines) {
            if ($line.Length -lt 23) { continue }
            $stampText = $line.Substring(0, 23)
            $stamp = [datetime]::MinValue
            if (-not [datetime]::TryParse($stampText, [ref]$stamp)) { continue }
            if ($stamp -lt $SinceLocal.AddSeconds(-2)) { continue }

            if ($line -match "TPPA fresh 3-point calculated error|TPPA correction-loop calculated error|Advanced Sequence finished") {
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

function Wait-CloudBackoff {
    param([int]$Minutes)
    if ($Minutes -le 0) { return }

    $end = (Get-Date).AddMinutes($Minutes)
    Log "Cloud/solve backoff: pausing TPPA restarts until $($end.ToString("O"))"
    while ((Get-Date) -lt $end) {
        if (Test-StopWindow) { return }
        $remaining = [Math]::Max(1, [int][Math]::Ceiling(($end - (Get-Date)).TotalSeconds))
        Start-Sleep -Seconds ([Math]::Min(60, $remaining))
    }
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
        if (Test-NinaLogSequenceFinished -SinceLocal $script:LastSequenceStartLocal) {
            $script:LastWaitReason = "Completed"
            return $true
        }
        if (Test-NinaSolveFailureBackoff -SinceLocal $script:LastSequenceStartLocal -Threshold $PlateSolveFailureBackoffCount) {
            Log "Too many plate-solve failures without TPPA progress; stopping this TPPA run and waiting for clouds to pass."
            try { Invoke-Nina -Base $Base -Path "/sequence/stop" -Method "POST" -Body @{} -TimeoutSec 10 | Out-Null } catch {
                try { Invoke-Nina -Base $Base -Path "/sequence/stop" -Method "GET" -TimeoutSec 10 | Out-Null } catch {}
            }
            Wait-CloudBackoff -Minutes $PlateSolveFailureBackoffMinutes
            $script:LastWaitReason = "CloudBackoff"
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
