#requires -Version 7.0
param(
    [string]$ExpectedHead = '',
    [ValidateRange(30, 600)]
    [int]$TimeoutSeconds = 180,
    [switch]$SkipLiveCalls
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$globalPreflight = Join-Path $env:USERPROFILE '.codex\bridge\test_council_bridges.ps1'
if (-not (Test-Path -LiteralPath $globalPreflight -PathType Leaf)) {
    throw "Missing codex-wide bridge preflight: $globalPreflight"
}

$parameters = @{
    RepositoryRoot = $repositoryRoot
    TimeoutSeconds = $TimeoutSeconds
}
if ($ExpectedHead) {
    $parameters.ExpectedHead = $ExpectedHead
}
if ($SkipLiveCalls) {
    $parameters.SkipLiveCalls = $true
}

& $globalPreflight @parameters
