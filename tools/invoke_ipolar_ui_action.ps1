param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Connect', 'Snapshot')]
    [string]$Action,
    [ValidateSet('SendInput', 'WindowMessage')]
    [string]$InputMethod = 'SendInput',
    [string]$TracePath
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

function Write-Trace([string]$Message) {
    if ([string]::IsNullOrWhiteSpace($TracePath)) { return }
    $parent = Split-Path -Parent ([IO.Path]::GetFullPath($TracePath))
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) {
        New-Item -ItemType Directory -Force -Path $parent | Out-Null
    }
    $line = '{0:o} {1}{2}' -f [DateTime]::UtcNow, $Message, [Environment]::NewLine
    [IO.File]::AppendAllText($TracePath, $line, [Text.UTF8Encoding]::new($false))
}

Write-Trace 'start'

Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class IPolarUiNative {
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr hWnd, ref POINT point);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int command);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern IntPtr SetFocus(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr processId);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint attach, uint attachTo, bool value);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT point);
    [DllImport("user32.dll")] public static extern bool ScreenToClient(IntPtr hWnd, ref POINT point);
    [DllImport("user32.dll", CharSet=CharSet.Auto)] public static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder text, int maxCount);
    [DllImport("user32.dll", CharSet=CharSet.Auto)] public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int maxCount);
    [DllImport("user32.dll", SetLastError=true)]
    public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint message, UIntPtr wParam, IntPtr lParam, uint flags, uint timeout, out UIntPtr result);
    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }
    [StructLayout(LayoutKind.Explicit)]
    public struct INPUTUNION {
        [FieldOffset(0)] public MOUSEINPUT mi;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT {
        public uint type;
        public INPUTUNION U;
    }
    [DllImport("user32.dll", SetLastError=true)]
    public static extern uint SendInput(uint count, INPUT[] inputs, int size);
}
'@

$currentSessionId = (Get-Process -Id $PID).SessionId
$process = Get-Process -Name 'iOptron iPolar' -ErrorAction SilentlyContinue |
    Where-Object { $_.SessionId -eq $currentSessionId -and $_.MainWindowHandle -ne [IntPtr]::Zero } |
    Select-Object -First 1
if ($null -eq $process) {
    throw "No visible iPolar window exists in interactive session $currentSessionId."
}
Write-Trace "selected pid=$($process.Id) hwnd=$($process.MainWindowHandle) session=$currentSessionId"

if ($Action -eq 'Snapshot') {
    Write-Trace 'snapshot-reading-window'
    $snapshotRect = New-Object IPolarUiNative+RECT
    if (-not [IPolarUiNative]::GetWindowRect($process.MainWindowHandle, [ref]$snapshotRect)) {
        throw 'Could not read the iPolar window rectangle.'
    }
    [ordered]@{
        SchemaVersion = 1
        ExecutedUtc = [DateTime]::UtcNow.ToString('o')
        Action = $Action
        SessionId = $currentSessionId
        ProcessId = $process.Id
        WindowTitle = $process.MainWindowTitle
        WindowRectangle = [ordered]@{ Left=$snapshotRect.Left; Top=$snapshotRect.Top; Right=$snapshotRect.Right; Bottom=$snapshotRect.Bottom }
        GrantsMountMotionAuthority = $false
        GrantsUpasAuthority = $false
    } | ConvertTo-Json -Depth 5
    Write-Trace 'snapshot-complete'
    exit 0
}

$clientPoint = switch ($Action) {
    'Connect' { [pscustomobject]@{ X = 68; Y = 94 } }
}

$rect = New-Object IPolarUiNative+RECT
if (-not [IPolarUiNative]::ShowWindow($process.MainWindowHandle, 9)) {
    # ShowWindow returns the prior visibility state, not action success.
}
Write-Trace 'window-shown'
$currentThreadId = [IPolarUiNative]::GetCurrentThreadId()
$windowThreadId = [IPolarUiNative]::GetWindowThreadProcessId($process.MainWindowHandle, [IntPtr]::Zero)
$threadInputAttached = $false
if ($windowThreadId -ne 0 -and $windowThreadId -ne $currentThreadId) {
    $threadInputAttached = [IPolarUiNative]::AttachThreadInput($currentThreadId, $windowThreadId, $true)
}
Write-Trace "thread-input-attached=$threadInputAttached"
try {
    [void][IPolarUiNative]::BringWindowToTop($process.MainWindowHandle)
    if (-not [IPolarUiNative]::SetForegroundWindow($process.MainWindowHandle)) {
        throw 'Could not bring the iPolar window to the foreground.'
    }
    [void][IPolarUiNative]::SetFocus($process.MainWindowHandle)
    Write-Trace 'window-focused'
    Start-Sleep -Milliseconds 500
} finally {
    if ($threadInputAttached) {
        [void][IPolarUiNative]::AttachThreadInput($currentThreadId, $windowThreadId, $false)
    }
}
if (-not [IPolarUiNative]::GetWindowRect($process.MainWindowHandle, [ref]$rect)) {
    throw 'Could not read the iPolar window rectangle.'
}
Write-Trace "window-rect=$($rect.Left),$($rect.Top),$($rect.Right),$($rect.Bottom)"

$point = New-Object IPolarUiNative+POINT
$point.X = [int]$clientPoint.X
$point.Y = [int]$clientPoint.Y
if (-not [IPolarUiNative]::ClientToScreen($process.MainWindowHandle, [ref]$point)) {
    throw 'Could not transform the iPolar client point to the desktop.'
}
$screenX = $point.X
$screenY = $point.Y
if (-not [IPolarUiNative]::SetCursorPos($screenX, $screenY)) {
    throw 'Could not position the pointer for the iPolar action.'
}
Write-Trace "cursor-positioned=$screenX,$screenY"
$targetWindow = [IPolarUiNative]::WindowFromPoint($point)
if ($targetWindow -eq [IntPtr]::Zero) {
    throw "No window exists at iPolar action point $screenX,$screenY."
}
$className = [Text.StringBuilder]::new(256)
$targetTitle = [Text.StringBuilder]::new(256)
[void][IPolarUiNative]::GetClassName($targetWindow, $className, $className.Capacity)
[void][IPolarUiNative]::GetWindowText($targetWindow, $targetTitle, $targetTitle.Capacity)
Write-Trace "target-window=$targetWindow class=$className title=$targetTitle"

if ($InputMethod -eq 'SendInput') {
    $mouseDown = New-Object 'IPolarUiNative+INPUT[]' 1
    $mouseDown[0].type = 0
    $mouseDown[0].U.mi.dwFlags = 0x0002
    $mouseUp = New-Object 'IPolarUiNative+INPUT[]' 1
    $mouseUp[0].type = 0
    $mouseUp[0].U.mi.dwFlags = 0x0004
    $inputSize = [Runtime.InteropServices.Marshal]::SizeOf([type]'IPolarUiNative+INPUT')
    Write-Trace 'send-input-start'
    $downSent = [IPolarUiNative]::SendInput(1, $mouseDown, $inputSize)
    Start-Sleep -Milliseconds 120
    $upSent = [IPolarUiNative]::SendInput(1, $mouseUp, $inputSize)
    Write-Trace "send-input-complete=down:$downSent,up:$upSent"
    if ($downSent -ne 1 -or $upSent -ne 1) {
        throw "SendInput delivered down=$downSent and up=$upSent (Win32=$([Runtime.InteropServices.Marshal]::GetLastWin32Error()))."
    }
} else {
    $targetPoint = New-Object IPolarUiNative+POINT
    $targetPoint.X = $screenX
    $targetPoint.Y = $screenY
    if (-not [IPolarUiNative]::ScreenToClient($targetWindow, [ref]$targetPoint)) {
        throw 'Could not transform the iPolar action point into the target window.'
    }
    $lParam = [IntPtr]((($targetPoint.Y -band 0xffff) -shl 16) -bor ($targetPoint.X -band 0xffff))
    $messageResult = [UIntPtr]::Zero
    Write-Trace "window-message-start=client:$($targetPoint.X),$($targetPoint.Y)"
    foreach ($message in @(0x0200, 0x0201)) {
        $wParam = if ($message -eq 0x0201) { [UIntPtr]1 } else { [UIntPtr]::Zero }
        if ([IPolarUiNative]::SendMessageTimeout($targetWindow, $message, $wParam, $lParam, 2, 1000, [ref]$messageResult) -eq [IntPtr]::Zero) {
            throw "Window message 0x$($message.ToString('X4')) failed or timed out."
        }
    }
    Start-Sleep -Milliseconds 120
    if ([IPolarUiNative]::SendMessageTimeout($targetWindow, 0x0202, [UIntPtr]::Zero, $lParam, 2, 1000, [ref]$messageResult) -eq [IntPtr]::Zero) {
        throw 'Window message WM_LBUTTONUP failed or timed out.'
    }
    Write-Trace 'window-message-complete'
}
Start-Sleep -Milliseconds 500
Write-Trace 'complete'

[ordered]@{
    SchemaVersion = 1
    ExecutedUtc = [DateTime]::UtcNow.ToString('o')
    Action = $Action
    InputMethod = $InputMethod
    SessionId = $currentSessionId
    ProcessId = $process.Id
    CurrentThreadId = $currentThreadId
    WindowThreadId = $windowThreadId
    WindowTitle = $process.MainWindowTitle
    WindowRectangle = [ordered]@{ Left=$rect.Left; Top=$rect.Top; Right=$rect.Right; Bottom=$rect.Bottom }
    ClientPoint = $clientPoint
    ScreenPoint = [ordered]@{ X=$screenX; Y=$screenY }
    TargetWindow = [int64]$targetWindow
    TargetWindowClass = $className.ToString()
    TargetWindowTitle = $targetTitle.ToString()
    GrantsMountMotionAuthority = $false
    GrantsUpasAuthority = $false
} | ConvertTo-Json -Depth 5
