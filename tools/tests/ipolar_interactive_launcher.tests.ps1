$ErrorActionPreference = 'Stop'

Describe 'interactive iPolar campaign launcher' {
    BeforeAll {
        $tools = Split-Path -Parent $PSScriptRoot
        $launcher = Get-Content -LiteralPath (Join-Path $tools 'invoke_interactive_ipolar_campaign_stage.ps1') -Raw
        $capture = Get-Content -LiteralPath (Join-Path $tools 'ipolar_slew_capture.ps1') -Raw
        $uiAction = Get-Content -LiteralPath (Join-Path $tools 'invoke_ipolar_ui_action.ps1') -Raw
        $uiLauncher = Get-Content -LiteralPath (Join-Path $tools 'invoke_interactive_ipolar_ui_action.ps1') -Raw
    }

    It 'uses an Interactive scheduled-task principal and bounded execution time' {
        $launcher | Should Match 'New-ScheduledTaskPrincipal.+-LogonType Interactive'
        $launcher | Should Match 'New-ScheduledTaskSettingsSet.+-ExecutionTimeLimit'
        $launcher | Should Match 'Interactive console user'
    }

    It 'requires a fresh task run, zero result, and the expected stage artifact' {
        $launcher | Should Match 'ObservedRunning'
        $launcher | Should Match 'LastTaskResult'
        $launcher | Should Match 'LastRunTime\.ToUniversalTime\(\) -lt \$startedUtc'
        $launcher | Should Match 'success without its artifact'
        $launcher | Should Match 'StageArtifactSha256'
    }

    It 'is immutable and idempotent only when receipt and artifact hashes agree' {
        $launcher | Should Match '\[IO\.FileMode\]::CreateNew'
        $launcher | Should Match 'Existing stage artifact hash no longer matches'
        $launcher | Should Match 'Stage artifact exists without its interactive launch receipt'
    }

    It 'never grants UPAS or absolute-accuracy authority' {
        $launcher | Should Match 'GrantsUpasAuthority = \$false'
        $launcher | Should Match 'GrantsAbsoluteAccuracyClaim = \$false'
    }

    It 'captures only the visible iPolar window in the current Windows session' {
        $capture | Should Match '\$currentSessionId = \(Get-Process -Id \$PID\)\.SessionId'
        $capture | Should Match '\$_.SessionId -eq \$currentSessionId'
        $capture | Should Match 'Run this capture in the logged-in interactive session'
    }

    It 'bounds iPolar UI automation to named actions in the interactive session' {
        $uiAction | Should Match "ValidateSet\('Connect', 'Snapshot'\)"
        $uiAction | Should Match '\$_.SessionId -eq \$currentSessionId'
        $uiAction | Should Match 'SetForegroundWindow'
        $uiAction | Should Match 'AttachThreadInput'
        $uiAction | Should Match 'SendInput'
        $uiAction | Should Match 'WindowFromPoint'
        $uiAction | Should Match 'SendMessageTimeout'
        $uiAction | Should Match "ValidateSet\('SendInput', 'WindowMessage'\)"
        $uiAction | Should Match 'send-input-start'
        $uiAction | Should Match 'send-input-complete'
        $uiAction | Should Match 'Start-Sleep -Milliseconds 120'
        $uiAction | Should Match 'ClientToScreen'
        $uiAction | Should Match "'Connect' \{ \[pscustomobject\]@\{ X = 68; Y = 94 \} \}"
        $uiAction | Should Match 'GrantsMountMotionAuthority = \$false'
        $uiAction | Should Match 'GrantsUpasAuthority = \$false'
        $uiAction | Should Match 'if \(\$Action -eq ''Snapshot''\)'
    }

    It 'launches UI actions interactively and preserves fresh hashed evidence' {
        $uiLauncher | Should Match 'New-ScheduledTaskPrincipal.+-LogonType Interactive'
        $uiLauncher | Should Match "ValidateSet\('Limited', 'Highest'\)"
        $uiLauncher | Should Match "ValidateSet\('SendInput', 'WindowMessage'\)"
        $uiLauncher | Should Match '-RunLevel \$RunLevel'
        $uiLauncher | Should Match 'New-ScheduledTaskSettingsSet.+-ExecutionTimeLimit'
        $uiLauncher | Should Match 'CopyFromScreen'
        $uiLauncher | Should Match 'LastRunTime\.ToUniversalTime\(\) -lt \$startedUtc'
        $uiLauncher | Should Match 'ScreenshotArtifactSha256'
        $uiLauncher | Should Match 'TraceArtifactSha256'
        $uiLauncher | Should Match 'GrantsReadinessAuthority = \$false'
        $uiLauncher | Should Match 'Unregister-ScheduledTask'
    }
}
