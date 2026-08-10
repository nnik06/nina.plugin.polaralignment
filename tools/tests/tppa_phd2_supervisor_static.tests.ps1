Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"

$supervisorPath = Join-Path (Split-Path -Parent $PSScriptRoot) "tppa_phd2_supervisor.ps1"
$text = [IO.File]::ReadAllText($supervisorPath)

function Assert-Contains {
    param([string]$Needle, [string]$Message)
    if (-not $text.Contains($Needle)) { throw "Assertion failed: $Message" }
}

$tokens = $null
$errors = $null
[Management.Automation.Language.Parser]::ParseFile($supervisorPath, [ref]$tokens, [ref]$errors) | Out-Null
if ($errors.Count -gt 0) { throw "Supervisor has PowerShell parse errors: $($errors -join '; ')" }

Assert-Contains '$writer.NewLine = "`r`n"' "PHD2 JSON-RPC writer must retain a literal CRLF escape"
Assert-Contains '$text.Contains("`n")' "CSV quoting must test for a literal newline escape"
Assert-Contains '$text -split "`r?`n"' "NINA log splitting must retain its literal CRLF/LF regex"
Assert-Contains 'camera_dx_px,camera_dy_px,pixel_scale_arcsec_px' "passive capture must persist camera displacement and pixel scale"
Assert-Contains 'analyze_tppa_phd2_diagnostics.ps1' "valid captures must generate read-only PDA artifacts"
Assert-Contains '-PdaHemisphere $PdaHemisphere -PdaMirror $PdaMirror' "PDA orientation parameters must reach the analyzer"
Assert-Contains 'set_guide_output_enabled" -Params @($originalGuideOutput)' "guide output must be restored"
Assert-Contains '$obj.Event -eq "StartGuiding" -and $steps -eq 0' "the supervisor must accept only its expected pre-sample StartGuiding event"
Assert-Contains '$obj.Event -eq "LockPositionSet" -and $steps -eq 0 -and -not $startupLockPositionAccepted' "the supervisor must accept at most one startup lock event before passive samples"
Assert-Contains '$lockDistance -le 2.0' "startup lock acceptance must be tied to the selected guide-star coordinates"
Assert-Contains 'PHD2 freshly selected guide star' "a stopped PHD2 session must refresh its lock instead of trusting a stale retained coordinate"
Assert-Contains 'function Wait-NinaSequenceIdle' "supervisor must define a bounded NINA sequence-idle wait"
Assert-Contains 'if (-not (Wait-NinaSequenceIdle -Base $Base))' "fresh TPPA stop must confirm NINA is idle before another sequence load"
Assert-Contains 'refusing to reload it' "idle timeout must fail closed instead of racing a sequence reload"
Assert-Contains 'if ($Mode -eq "FreshMeasurement")' "supervisor must expose a one-result measurement-only mode"
Assert-Contains 'FreshMeasurement mode requires -SequencePath' "measurement-only mode must require a known sequence"
Assert-Contains 'FreshMeasurement mode armed. It will stop immediately after one fresh three-point result; no PHD2 capture is scheduled.' "measurement-only mode must explicitly deny PHD2 capture"
Assert-Contains '$lockPosition = $null' "strict mode must not swallow startup lock validation for an uninitialized coordinate"
Assert-Contains '@("Stopped", "Looping") -notcontains $preState' "passive drift must refuse to disturb an active guiding state"
Assert-Contains 'PHD2 drift capture attempt $attempt/$Phd2CaptureAttempts' "transient retries must be explicitly bounded and logged"
Assert-Contains 'Move-Item -LiteralPath $rejectedCsv' "rejected captures must be quarantined from analyzer input"
Assert-Contains 'PHD2 drift capture reached StopAt' "capture loop must honor the hard stop window"
Assert-Contains '$invalidatingPhd2Event = "MalformedGuidePulse"' "unparseable pulse data must invalidate the capture"
Assert-Contains 'function New-Phd2CaptureException' "PHD2 capture failures must carry structured retry metadata"
Assert-Contains '$_.Exception.Data["Phd2Transient"]' "retry classification must not depend on exception-message text"
Assert-Contains 'function Assert-PdaPointing' "PDA capture must define a mount-pointing preflight"
Assert-Contains '$poleDistance -gt $PdaMaxPoleDistanceDegrees' "PDA capture must reject fields outside the configured polar radius"
Assert-Contains '$altitude -lt $PdaMinimumAltitudeDegrees -or $altitude -gt $PdaMaximumAltitudeDegrees' "PDA capture must enforce the balcony altitude envelope"
Assert-Contains '$azimuth -ge $PdaWesternAzimuthMinimumDegrees -or $azimuth -le $PdaEasternAzimuthMaximumDegrees' "PDA capture must enforce the north-sector azimuth envelope"
Assert-Contains 'PdaMaximumAltitudeDegrees = 55.0' "the field supervisor must default to the balcony upper-altitude limit"
Assert-Contains 'PdaEasternAzimuthMaximumDegrees = 10.0' "the field supervisor must default to the balcony eastern-azimuth limit"
Assert-Contains 'if (-not [bool]$mount.TrackingEnabled)' "PDA capture must require tracking"
Assert-Contains 'if ([bool]$mount.Slewing)' "PDA capture must reject an active slew"
Assert-Contains 'Assert-PdaPointing' "every whole-capture attempt must rerun the PDA pointing preflight"
Assert-Contains 'PHD2 calibration data: ' "PHD2 calibration state must be logged before capture"
Assert-Contains 'PHD2 post-lock calibration data:' "declination-adjusted calibration must be logged after passive capture"
Assert-Contains 'get_exposure' "diagnostic exposure changes must preserve the original PHD2 exposure"
Assert-Contains 'set_exposure" -Params @{ exposure = $originalExposureMs }' "diagnostic exposure must be restored in the capture finally block"
if (([regex]::Matches($text, 'PHD2 exposure restored to')).Count -ne 1) {
    throw "Assertion failed: exposure restore logic must exist only in the PHD2 capture finally block"
}

$disableIndex = $text.IndexOf('set_guide_output_enabled" -Params @($false)')
$guideIndex = $text.IndexOf('-Method "guide" -Params $guideParams')
if ($disableIndex -lt 0 -or $guideIndex -lt 0 -or $disableIndex -ge $guideIndex) {
    throw "Assertion failed: guide output must be disabled before the guide command"
}

$freshMeasurementStart = $text.IndexOf('if ($Mode -eq "FreshMeasurement")')
$stabilityStart = $text.IndexOf('if ($Mode -eq "Stability")')
if ($freshMeasurementStart -lt 0 -or $stabilityStart -le $freshMeasurementStart) {
    throw "Assertion failed: FreshMeasurement mode must precede Stability mode."
}
$freshMeasurementBlock = $text.Substring($freshMeasurementStart, $stabilityStart - $freshMeasurementStart)
if ($freshMeasurementBlock.Contains('Capture-Phd2')) {
    throw "Assertion failed: FreshMeasurement mode must not call a PHD2 capture function."
}

foreach ($eventName in @(
    "StarLost", "GuidingDithered", "LockPositionSet", "LockPositionShiftLimitReached",
    "LockPositionLost", "GuidingStopped", "StartGuiding"
)) {
    Assert-Contains ('"' + $eventName + '"') "capture must reject $eventName"
}

Write-Host "All tppa_phd2_supervisor static tests passed."
