#requires -Version 7.0
param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$ExpectedRoot = 'C:\Dev\upas-nina-tppa-plugin',
    [string]$ExpectedHead = '',
    [ValidateRange(30, 600)]
    [int]$TimeoutSeconds = 180,
    [switch]$SkipLiveCalls
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

function Invoke-CapturedProcess {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string[]]$ArgumentList,
        [Parameter(Mandatory = $true)]
        [string]$WorkingDirectory,
        [Parameter(Mandatory = $true)]
        [int]$Timeout
    )

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FilePath
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in $ArgumentList) {
        [void]$startInfo.ArgumentList.Add($argument)
    }

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    if (-not $process.Start()) {
        throw "Could not start $FilePath."
    }

    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit($Timeout * 1000)) {
        try { $process.Kill($true) } catch {}
        throw "$FilePath timed out after $Timeout seconds."
    }

    $stdout = $stdoutTask.GetAwaiter().GetResult().Trim()
    $stderr = $stderrTask.GetAwaiter().GetResult().Trim()
    [pscustomobject]@{
        ExitCode = $process.ExitCode
        Stdout = $stdout
        Stderr = $stderr
    }
}

function Assert-Success {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Result,
        [Parameter(Mandatory = $true)]
        [string]$Step
    )
    if ($Result.ExitCode -ne 0) {
        throw "$Step failed with exit code $($Result.ExitCode): $($Result.Stderr ?? $Result.Stdout)"
    }
}

function Resolve-NativeCommand([string]$Name) {
    $command = Get-Command $Name -CommandType Application -ErrorAction Stop |
        Select-Object -First 1
    $command.Source
}

function Normalize-Path([string]$Path) {
    [IO.Path]::GetFullPath($Path).TrimEnd('\')
}

$root = Normalize-Path $RepositoryRoot
$expected = Normalize-Path $ExpectedRoot
if (-not [string]::Equals($root, $expected, [StringComparison]::OrdinalIgnoreCase)) {
    throw "RepositoryRoot is $root; expected $expected."
}

$git = Resolve-NativeCommand 'git'
$gitRootResult = Invoke-CapturedProcess $git @('-C', $root, 'rev-parse', '--show-toplevel') $root 30
Assert-Success $gitRootResult 'git root verification'
$actualGitRoot = Normalize-Path $gitRootResult.Stdout
if (-not [string]::Equals($actualGitRoot, $expected, [StringComparison]::OrdinalIgnoreCase)) {
    throw "git resolved $actualGitRoot; expected $expected."
}

$headResult = Invoke-CapturedProcess $git @('-C', $root, 'rev-parse', 'HEAD') $root 30
Assert-Success $headResult 'git HEAD verification'
$head = $headResult.Stdout.Trim()
if ($ExpectedHead -and -not [string]::Equals($head, $ExpectedHead, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Repository HEAD is $head; expected $ExpectedHead."
}

$python = 'C:\Program Files\Python312\python.exe'
$claudeBridge = Join-Path $env:USERPROFILE 'Documents\Codex\bridges\claude_fable_mcp_server.py'
$geminiBridge = Join-Path $env:USERPROFILE 'OneDrive\Documents\Astro\gemini_mcp_server.py'
$codexConfig = Join-Path $env:USERPROFILE '.codex\config.toml'
foreach ($requiredPath in @($python, $claudeBridge, $geminiBridge, $codexConfig)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Required bridge component is missing: $requiredPath"
    }
}

$configText = [IO.File]::ReadAllText($codexConfig)
foreach ($requiredToken in @(
    '[mcp_servers.claude-cli-bridge]',
    'claude_fable_mcp_server.py',
    '[mcp_servers.gemini-bridge]',
    'gemini_mcp_server.py')) {
    if (-not $configText.Contains($requiredToken)) {
        throw "Codex MCP config is missing: $requiredToken"
    }
}

foreach ($bridge in @($claudeBridge, $geminiBridge)) {
    $compile = Invoke-CapturedProcess $python @('-m', 'py_compile', $bridge) $root 30
    Assert-Success $compile "Python syntax check for $bridge"
}

$claude = Resolve-NativeCommand 'claude'
$agy = Resolve-NativeCommand 'agy'
$claudeVersion = Invoke-CapturedProcess $claude @('--version') $root 30
Assert-Success $claudeVersion 'Claude version check'
$agyVersion = Invoke-CapturedProcess $agy @('--version') $root 30
Assert-Success $agyVersion 'Antigravity version check'

$claudeAuth = Invoke-CapturedProcess $claude @('auth', 'status') $root 30
Assert-Success $claudeAuth 'Claude authentication check'
try {
    $claudeAuthJson = $claudeAuth.Stdout | ConvertFrom-Json
} catch {
    throw "Claude auth status was not valid JSON: $($claudeAuth.Stdout)"
}
if (-not [bool]$claudeAuthJson.loggedIn) {
    throw 'Claude CLI is not logged in.'
}

$claudeLive = 'SKIPPED'
$geminiLive = 'SKIPPED'
if (-not $SkipLiveCalls) {
    $claudePing = Invoke-CapturedProcess $claude @(
        '-p',
        '--safe-mode',
        '--permission-mode', 'plan',
        '--tools', '',
        '--no-session-persistence',
        '--effort', 'high',
        '--output-format', 'text',
        '--model', 'fable',
        'Reply exactly CLAUDE_BRIDGE_OK and nothing else.'
    ) $root $TimeoutSeconds
    Assert-Success $claudePing 'Claude Fable live ping'
    if ($claudePing.Stdout.Trim() -ne 'CLAUDE_BRIDGE_OK') {
        throw "Claude live ping returned unexpected output: $($claudePing.Stdout)"
    }
    $claudeLive = 'PASS'

    $projectFile = Join-Path $root 'PolarAlignment\NINA.Plugins.PolarAlignment.csproj'
    $versionMatch = [regex]::Match([IO.File]::ReadAllText($projectFile), '<Version>([^<]+)</Version>')
    if (-not $versionMatch.Success) {
        throw "Could not read the plugin version from $projectFile."
    }
    $pluginVersion = $versionMatch.Groups[1].Value

    $geminiFileProbe = @'
import importlib.util
import sys

bridge_path, repository_root, expected_version = sys.argv[1:4]
spec = importlib.util.spec_from_file_location("gemini_bridge_preflight", bridge_path)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
result = module.execute_gemini_with_files(
    f"Use only the attached file. If its Version element is {expected_version}, reply exactly GEMINI_BRIDGE_FILE_OK. Do not invoke tools or commands.",
    repository_root,
    ["PolarAlignment/NINA.Plugins.PolarAlignment.csproj"],
    "Gemini 3.1 Pro (High)",
    180,
    "inline",
)
print(result)
'@
    $geminiPing = Invoke-CapturedProcess $python @(
        '-c', $geminiFileProbe, $geminiBridge, $root, $pluginVersion
    ) $root $TimeoutSeconds
    Assert-Success $geminiPing 'Gemini bridge nested-file live ping'
    if ($geminiPing.Stdout.Trim() -ne 'GEMINI_BRIDGE_FILE_OK') {
        throw "Gemini file bridge returned unexpected output: $($geminiPing.Stdout)"
    }
    $geminiLive = 'PASS'
}

[pscustomobject]@{
    RepositoryRoot = $actualGitRoot
    Head = $head
    ClaudeVersion = $claudeVersion.Stdout
    ClaudeAuthenticated = [bool]$claudeAuthJson.loggedIn
    ClaudeBridgeSha256 = (Get-FileHash -LiteralPath $claudeBridge -Algorithm SHA256).Hash
    ClaudeLive = $claudeLive
    GeminiVersion = $agyVersion.Stdout
    GeminiBridgeSha256 = (Get-FileHash -LiteralPath $geminiBridge -Algorithm SHA256).Hash
    GeminiLive = $geminiLive
    ReadOnlyModes = 'Claude plan/no-tools; Gemini plan/new-project'
}
