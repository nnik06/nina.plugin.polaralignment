#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$toolRoot = Split-Path -Parent $PSScriptRoot
$builder = Join-Path $toolRoot 'new_tppa_field_readiness_packet.ps1'

function New-TestRepository([string]$Root) {
    $repo = Join-Path $Root 'repo'
    New-Item -ItemType Directory -Path (Join-Path $repo 'tools\field_packet_templates') -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $toolRoot 'new_tppa_runtime_manifest.ps1') -Destination (Join-Path $repo 'tools')
    Copy-Item -Path (Join-Path $toolRoot 'field_packet_templates\*') -Destination (Join-Path $repo 'tools\field_packet_templates')
    New-Item -ItemType Directory -Path (Join-Path $repo 'docs') -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $repo 'docs\COARSE_RESPONSE_AND_FIVE_DEGREE_FIELD_PROTOCOL.md'), '# exact supervisor protocol' + [Environment]::NewLine)
    & git -C $repo init --quiet | Out-Null
    & git -C $repo config user.name 'TPPA Test' | Out-Null
    & git -C $repo config user.email 'tppa-test@example.invalid' | Out-Null
    & git -C $repo add . | Out-Null
    & git -C $repo commit --quiet -m 'fixture' | Out-Null
    return $repo
}

function New-ProvenanceAssembly([string]$Root, [string]$Name, [string]$AssemblyVersion, [string]$InformationalVersion) {
    $project = Join-Path $Root $Name
    New-Item -ItemType Directory -Path $project -Force | Out-Null
    $projectText = @"
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework><AssemblyName>$Name</AssemblyName><Version>$AssemblyVersion</Version><AssemblyVersion>$AssemblyVersion</AssemblyVersion><FileVersion>$AssemblyVersion</FileVersion><InformationalVersion>$InformationalVersion</InformationalVersion><IncludeSourceRevisionInInformationalVersion>false</IncludeSourceRevisionInInformationalVersion></PropertyGroup></Project>
"@
    [IO.File]::WriteAllText((Join-Path $project "$Name.csproj"), $projectText, [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $project 'Fixture.cs'), "namespace TppaPacketFixture; public sealed class $($Name.Replace('.', '_')) {}", [Text.UTF8Encoding]::new($false))
    $output = Join-Path $project 'out'
    & dotnet build (Join-Path $project "$Name.csproj") -c Release -o $output --nologo | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Unable to build provenance fixture $Name." }
    return Join-Path $output "$Name.dll"
}
function New-MatchingRuntimePair([string]$Root, [string]$Commit) {
    [pscustomobject]@{ Plugin = New-ProvenanceAssembly $Root 'Fixture.Plugin' '2.2.6.104' "2.2.6.104+$Commit"; Core = New-ProvenanceAssembly $Root 'Fixture.Core' '1.0.0.0' "1.0.0+$Commit" }
}

Describe 'TPPA field readiness packet builder' {
    It 'atomically emits one provenance-bound packet with current qualification semantics' {
        $root = Join-Path $TestDrive 'success'
        New-Item -ItemType Directory -Path $root | Out-Null
        $repo = New-TestRepository $root
        $head = (& git -C $repo rev-parse HEAD).Trim()
        $runtime = New-MatchingRuntimePair $root $head
        $plugin = $runtime.Plugin
        $core = $runtime.Core
        $output = Join-Path $root 'packet'

        $result = & $builder -RepositoryRoot $repo -PluginDllPath $plugin `
            -QualificationCoreDllPath $core -OutputDirectory $output `
            -SourceCommit $head -SupervisorRepositoryRoot $repo -SupervisorSourceCommit $head -PluginVersion '2.2.6.104'

        $result.SourceCommit | Should Be $head
        $result.PluginVersion | Should Be '2.2.6.104'
        $result.SupervisorSourceCommit | Should Be $head
        $result.SupervisorProtocolSha256 | Should Be (Get-FileHash -Algorithm SHA256 (Join-Path $output 'SUPERVISOR_COARSE_RESPONSE_PROTOCOL.md')).Hash
        (Get-ChildItem -File $output).Count | Should Be 10
        $allText = (Get-ChildItem -File $output -Filter '*.md' |
            ForEach-Object { [IO.File]::ReadAllText($_.FullName) }) -join "`n"
        $allText | Should Not Match '__[A-Z0-9_]+__'
        $allText | Should Not Match '(?i)\bp95\b'
        $allText | Should Match 'Totals are 4, 3, 3, 3, 3 and 4 attempts'
        $allText | Should Match '0--30 and 240--300'
        $allText | Should Match 'upas-coarse-response-fit'
        $allText | Should Match 'upas-coarse-response-commission'
        $allText | Should Match 'one-degree reserve'
        $allText | Should Match $head
        $allText | Should Match $result.SupervisorProtocolSha256
        ([IO.File]::ReadAllText((Join-Path $output 'verify_field_preflight.ps1'))) |
            Should Match 'NextStage = ''Per-train coarse-response and travel commissioning'''

        $manifest = [IO.File]::ReadAllText((Join-Path $output 'TPPA.runtime-manifest.json')) | ConvertFrom-Json
        $manifest.sourceCommit | Should Be $head
        $manifest.pluginVersion | Should Be '2.2.6.104'
        $hashLines = [IO.File]::ReadAllLines((Join-Path $output 'HASHES.sha256'))
        $hashLines.Count | Should Be 9
        foreach ($line in $hashLines) {
            if ($line -notmatch '^([0-9A-F]{64})  ([A-Za-z0-9._-]+)$') {
                throw "Malformed hash line: $line"
            }
            $expectedHash = $Matches[1]
            $name = $Matches[2]
            (Get-FileHash -Algorithm SHA256 (Join-Path $output $name)).Hash | Should Be $expectedHash
        }
    }

    It 'refuses overwrite before staging any output' {
        $root = Join-Path $TestDrive 'overwrite'
        New-Item -ItemType Directory -Path $root | Out-Null
        $repo = New-TestRepository $root
        $head = (& git -C $repo rev-parse HEAD).Trim()
        $plugin = Join-Path $root 'plugin.dll'
        $core = Join-Path $root 'core.dll'
        [IO.File]::WriteAllBytes($plugin, [byte[]](1))
        [IO.File]::WriteAllBytes($core, [byte[]](2))
        $output = Join-Path $root 'packet'
        New-Item -ItemType Directory -Path $output | Out-Null

        $caught = $null
        try {
            & $builder -RepositoryRoot $repo -PluginDllPath $plugin `
                -QualificationCoreDllPath $core -OutputDirectory $output `
                -SourceCommit $head -SupervisorRepositoryRoot $repo -SupervisorSourceCommit $head -PluginVersion '2.2.6.104' | Out-Null
        } catch { $caught = $_ }
        $caught | Should Not BeNullOrEmpty
        $caught.Exception.Message | Should Match 'Refusing to overwrite'
        (Get-ChildItem -Force $output).Count | Should Be 0
    }

    It 'rejects tracked source drift before producing a packet' {
        $root = Join-Path $TestDrive 'dirty'
        New-Item -ItemType Directory -Path $root | Out-Null
        $repo = New-TestRepository $root
        $head = (& git -C $repo rev-parse HEAD).Trim()
        Add-Content -LiteralPath (Join-Path $repo 'tools\new_tppa_runtime_manifest.ps1') -Value '# drift'
        $plugin = Join-Path $root 'plugin.dll'
        $core = Join-Path $root 'core.dll'
        [IO.File]::WriteAllBytes($plugin, [byte[]](1))
        [IO.File]::WriteAllBytes($core, [byte[]](2))
        $output = Join-Path $root 'packet'

        $caught = $null
        try {
            & $builder -RepositoryRoot $repo -PluginDllPath $plugin `
                -QualificationCoreDllPath $core -OutputDirectory $output `
                -SourceCommit $head -SupervisorRepositoryRoot $repo -SupervisorSourceCommit $head -PluginVersion '2.2.6.104' | Out-Null
        } catch { $caught = $_ }
        $caught | Should Not BeNullOrEmpty
        $caught.Exception.Message | Should Match 'tracked changes'
        Test-Path -LiteralPath $output | Should Be $false
    }
    It 'rejects a stale supervisor checkpoint before producing a packet' {
        $root = Join-Path $TestDrive 'stale-supervisor'
        New-Item -ItemType Directory -Path $root | Out-Null
        $repo = New-TestRepository $root
        $head = (& git -C $repo rev-parse HEAD).Trim()
        $runtime = New-MatchingRuntimePair $root $head
        $output = Join-Path $root 'packet'
        $caught = $null
        try {
            & $builder -RepositoryRoot $repo -PluginDllPath $runtime.Plugin  `
                -QualificationCoreDllPath $runtime.Core -OutputDirectory $output  `
                -SourceCommit $head -SupervisorRepositoryRoot $repo  `
                -SupervisorSourceCommit ('0' * 40) -PluginVersion '2.2.6.104' | Out-Null
        } catch { $caught = $_ }
        $caught | Should Not BeNullOrEmpty
        $caught.Exception.Message | Should Match 'Supervisor repository HEAD does not match'
        Test-Path -LiteralPath $output | Should Be $false
    }
    It 'rejects a stale plugin version before producing a packet' {
        $root = Join-Path $TestDrive 'stale-version'; New-Item -ItemType Directory -Path $root | Out-Null
        $repo = New-TestRepository $root; $head = (& git -C $repo rev-parse HEAD).Trim(); $runtime = New-MatchingRuntimePair $root $head; $output = Join-Path $root 'packet'; $caught = $null
        try { & $builder -RepositoryRoot $repo -PluginDllPath $runtime.Plugin -QualificationCoreDllPath $runtime.Core -OutputDirectory $output -SourceCommit $head -SupervisorRepositoryRoot $repo -SupervisorSourceCommit $head -PluginVersion '2.2.6.105' | Out-Null } catch { $caught = $_ }
        $caught | Should Not BeNullOrEmpty; $caught.Exception.Message | Should Match 'assembly version does not match'; Test-Path -LiteralPath $output | Should Be $false
    }
    It 'rejects stale plugin provenance before producing a packet' {
        $root = Join-Path $TestDrive 'stale-commit'; New-Item -ItemType Directory -Path $root | Out-Null
        $repo = New-TestRepository $root; $head = (& git -C $repo rev-parse HEAD).Trim(); $plugin = New-ProvenanceAssembly $root 'Stale.Plugin' '2.2.6.104' '2.2.6.104+0000000000000000000000000000000000000000'; $core = New-ProvenanceAssembly $root 'Stale.Core' '1.0.0.0' "1.0.0+$head"; $output = Join-Path $root 'packet'; $caught = $null
        try { & $builder -RepositoryRoot $repo -PluginDllPath $plugin -QualificationCoreDllPath $core -OutputDirectory $output -SourceCommit $head -SupervisorRepositoryRoot $repo -SupervisorSourceCommit $head -PluginVersion '2.2.6.104' | Out-Null } catch { $caught = $_ }
        $caught | Should Not BeNullOrEmpty; $caught.Exception.Message | Should Match 'informational version is not bound'; Test-Path -LiteralPath $output | Should Be $false
    }
    It 'rejects stale qualification-core provenance before producing a packet' {
        $root = Join-Path $TestDrive 'stale-core'; New-Item -ItemType Directory -Path $root | Out-Null
        $repo = New-TestRepository $root; $head = (& git -C $repo rev-parse HEAD).Trim(); $plugin = New-ProvenanceAssembly $root 'Current.Plugin' '2.2.6.104' "2.2.6.104+$head"; $core = New-ProvenanceAssembly $root 'Stale.Core' '1.0.0.0' '1.0.0+0000000000000000000000000000000000000000'; $output = Join-Path $root 'packet'; $caught = $null
        try { & $builder -RepositoryRoot $repo -PluginDllPath $plugin -QualificationCoreDllPath $core -OutputDirectory $output -SourceCommit $head -SupervisorRepositoryRoot $repo -SupervisorSourceCommit $head -PluginVersion '2.2.6.104' | Out-Null } catch { $caught = $_ }
        $caught | Should Not BeNullOrEmpty; $caught.Exception.Message | Should Match 'core informational version is not bound'; Test-Path -LiteralPath $output | Should Be $false
    }
}
