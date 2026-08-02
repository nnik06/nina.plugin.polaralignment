param(
    [Parameter(Mandatory = $true)]
    [string]$RunId,
    [ValidateRange(10, 3600)]
    [int]$DurationSeconds = 120,
    [ValidateRange(100, 60000)]
    [int]$CadenceMilliseconds = 500,
    [ValidateSet('Ordinary', 'Zoom')]
    [string]$DeclaredDisplayMode = 'Ordinary',
    [ValidateRange(640, 4096)]
    [int]$ExpectedWindowWidthPixels = 1295,
    [ValidateRange(480, 2160)]
    [int]$ExpectedWindowHeightPixels = 735,
    [string]$Root = 'C:\Users\nnik0\Documents\UPAS\field-20260801\ipolar-slew-stability'
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
$failureReceiptPath = Join-Path $Root "$RunId-recorder-failure.json"
trap {
    $failureSessionId = $null
    try { $failureSessionId = (Get-Process -Id $PID).SessionId } catch { }
    $failure = [ordered]@{
        SchemaVersion = 1
        RunId = $RunId
        FailedUtc = [DateTime]::UtcNow.ToString('o')
        SessionId = $failureSessionId
        Error = $_.Exception.Message
        Stack = $_.ScriptStackTrace
        Position = $_.InvocationInfo.PositionMessage
        GrantsMountMotionAuthority = $false
        GrantsUpasAuthority = $false
    }
    try {
        if (-not (Test-Path -LiteralPath $Root -PathType Container)) {
            New-Item -ItemType Directory -Force -Path $Root | Out-Null
        }
        $stream = [IO.File]::Open($failureReceiptPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
        try {
            $writer = [IO.StreamWriter]::new($stream, [Text.UTF8Encoding]::new($false))
            try { $writer.Write(($failure | ConvertTo-Json -Depth 5)) } finally { $writer.Dispose() }
        } finally { $stream.Dispose() }
    } catch { }
    exit 1
}

Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class IPolarCaptureNative {
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

    [DllImport("user32.dll")]
    public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr hwnd);
}
'@

$run = Join-Path $Root $RunId
$frames = Join-Path $run 'frames'
$samples = Join-Path $run 'samples.jsonl'
if (Test-Path -LiteralPath $run) { throw "Run directory already exists: $run" }
New-Item -ItemType Directory -Force -Path $frames | Out-Null

$currentSessionId = (Get-Process -Id $PID).SessionId
$allIPolarProcesses = @(Get-Process -Name 'iOptron iPolar' -ErrorAction SilentlyContinue)
$process = $allIPolarProcesses |
    Where-Object { $_.SessionId -eq $currentSessionId -and $_.MainWindowHandle -ne [IntPtr]::Zero } |
    Select-Object -First 1
if ($null -eq $process) {
    $observed = if ($allIPolarProcesses.Count -eq 0) {
        'none'
    } else {
        ($allIPolarProcesses | ForEach-Object {
            "pid=$($_.Id),session=$($_.SessionId),hwnd=$($_.MainWindowHandle)"
        }) -join '; '
    }
    throw "iPolar has no capturable main window in session $currentSessionId; observed: $observed. Run this capture in the logged-in interactive session."
}

$nativeRect = New-Object IPolarCaptureNative+RECT
if (-not [IPolarCaptureNative]::GetWindowRect($process.MainWindowHandle, [ref]$nativeRect)) {
    throw 'GetWindowRect failed for the iPolar window.'
}
$windowWidth = $nativeRect.Right - $nativeRect.Left
$windowHeight = $nativeRect.Bottom - $nativeRect.Top
$windowDpi = [int][IPolarCaptureNative]::GetDpiForWindow($process.MainWindowHandle)
if ($windowWidth -ne $ExpectedWindowWidthPixels -or $windowHeight -ne $ExpectedWindowHeightPixels) {
    throw "iPolar window ${windowWidth}x${windowHeight} does not match the qualified ${ExpectedWindowWidthPixels}x${ExpectedWindowHeightPixels} capture profile."
}
$binaryPath = $process.MainModule.FileName
$captureProfile = [ordered]@{
    SchemaVersion = 1
    RunId = $RunId
    CapturedUtc = [DateTime]::UtcNow.ToString('o')
    ProcessId = $process.Id
    ProcessSessionId = $process.SessionId
    ProcessStartUtc = $process.StartTime.ToUniversalTime().ToString('o')
    WindowHandle = $process.MainWindowHandle.ToInt64()
    WindowTitle = $process.MainWindowTitle
    WindowLeftPixels = $nativeRect.Left
    WindowTopPixels = $nativeRect.Top
    WindowWidthPixels = $windowWidth
    WindowHeightPixels = $windowHeight
    WindowDpi = $windowDpi
    DeclaredDisplayMode = $DeclaredDisplayMode
    CaptureMethod = 'PrintWindow'
    ExecutablePath = $binaryPath
    ExecutableSha256 = (Get-FileHash -LiteralPath $binaryPath -Algorithm SHA256).Hash
}
$captureProfilePath = Join-Path $run 'capture-profile.json'
$profileJson = $captureProfile | ConvertTo-Json -Depth 4
$profileStream = [IO.File]::Open($captureProfilePath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
try {
    $profileWriter = [IO.StreamWriter]::new($profileStream, [Text.UTF8Encoding]::new($false))
    try { $profileWriter.Write($profileJson) } finally { $profileWriter.Dispose() }
} finally { $profileStream.Dispose() }
$captureProfileDigest = (Get-FileHash -LiteralPath $captureProfilePath -Algorithm SHA256).Hash

$rect = New-Object Drawing.Rectangle
$rect.X = 0
$rect.Y = 0
$rect.Width = $ExpectedWindowWidthPixels
$rect.Height = $ExpectedWindowHeightPixels
$api = 'http://127.0.0.1:1888/v2/api'
$deadline = [DateTime]::UtcNow.AddSeconds($DurationSeconds)
$stopwatch = [Diagnostics.Stopwatch]::StartNew()
$index = 0

while ([DateTime]::UtcNow -lt $deadline) {
    $cycleStarted = [DateTime]::UtcNow
    $index++
    $stamp = $cycleStarted.ToString('yyyyMMddTHHmmss.fffZ')
    $name = '{0:D4}-{1}-ipolar.png' -f $index, $stamp
    $path = Join-Path $frames $name
    $captureError = $null
    $mountError = $null
    $mountReceipt = $null

    try {
        $process.Refresh()
        $cycleRect = New-Object IPolarCaptureNative+RECT
        if ($process.HasExited -or $process.SessionId -ne $currentSessionId -or
            $process.MainWindowHandle -eq [IntPtr]::Zero -or
            -not [IPolarCaptureNative]::GetWindowRect($process.MainWindowHandle, [ref]$cycleRect)) {
            throw 'iPolar source-window identity is no longer valid.'
        }
        $cycleWidth = $cycleRect.Right - $cycleRect.Left
        $cycleHeight = $cycleRect.Bottom - $cycleRect.Top
        $cycleDpi = [int][IPolarCaptureNative]::GetDpiForWindow($process.MainWindowHandle)
        if ($process.MainWindowHandle.ToInt64() -ne $captureProfile.WindowHandle -or
            $cycleWidth -ne $captureProfile.WindowWidthPixels -or
            $cycleHeight -ne $captureProfile.WindowHeightPixels -or
            $cycleDpi -ne $captureProfile.WindowDpi) {
            throw 'iPolar source-window profile changed during capture.'
        }
        $bitmap = New-Object Drawing.Bitmap $rect.Width, $rect.Height
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        try {
            $hdc = $graphics.GetHdc()
            try {
                if (-not [IPolarCaptureNative]::PrintWindow($process.MainWindowHandle, $hdc, 0)) {
                    throw 'PrintWindow returned false.'
                }
            } finally {
                $graphics.ReleaseHdc($hdc)
            }
            $bitmap.Save($path, [Drawing.Imaging.ImageFormat]::Png)
        } finally {
            $graphics.Dispose()
            $bitmap.Dispose()
        }
    } catch {
        $captureError = $_.Exception.Message
    }

    try {
        $mount = (Invoke-RestMethod -Uri "$api/equipment/mount/info" -TimeoutSec 2).Response
        $mountReceipt = [ordered]@{
            Connected = [bool]$mount.Connected
            Slewing = [bool]$mount.Slewing
            TrackingEnabled = [bool]$mount.TrackingEnabled
            TrackingMode = [string]$mount.TrackingMode
            SideOfPier = [string]$mount.SideOfPier
            RightAscension = [double]$mount.RightAscension
            Declination = [double]$mount.Declination
            Azimuth = [double]$mount.Azimuth
            Altitude = [double]$mount.Altitude
            AtHome = [bool]$mount.AtHome
            AtPark = [bool]$mount.AtPark
        }
    } catch {
        $mountError = $_.Exception.Message
    }

    $sha = $null
    if (Test-Path -LiteralPath $path) {
        $sha = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    }

    [ordered]@{
        Index = $index
        CapturedUtc = $cycleStarted.ToString('o')
        ElapsedMilliseconds = [Math]::Round($stopwatch.Elapsed.TotalMilliseconds, 1)
        Frame = if ($sha) { $name } else { $null }
        FrameSha256 = $sha
        CaptureProfileDigest = $captureProfileDigest
        CaptureError = $captureError
        MountError = $mountError
        Mount = $mountReceipt
    } | ConvertTo-Json -Compress -Depth 6 | Add-Content -LiteralPath $samples -Encoding utf8

    $remaining = $CadenceMilliseconds - ([DateTime]::UtcNow - $cycleStarted).TotalMilliseconds
    if ($remaining -gt 0) {
        Start-Sleep -Milliseconds ([int][Math]::Ceiling($remaining))
    }
}

[ordered]@{
    RunId = $RunId
    SampleCount = $index
    Samples = $samples
    Frames = $frames
    CaptureProfile = $captureProfilePath
    CaptureProfileDigest = $captureProfileDigest
} | ConvertTo-Json -Depth 3
