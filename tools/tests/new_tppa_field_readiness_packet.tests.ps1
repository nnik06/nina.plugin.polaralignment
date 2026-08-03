#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$toolRoot = Split-Path -Parent $PSScriptRoot
$builder = Join-Path $toolRoot 'new_tppa_field_readiness_packet.ps1'

function New-TestRepository([string]$Root) {
    $repo = Join-Path $Root 'repo'
    New-Item -ItemType Directory -Path (Join-Path $repo 'tools\field_packet_templates') -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $toolRoot 'new_tppa_runtime_manifest.ps1') -Destination (Join-Path $repo 'tools')
    Copy-Item -Path (Join-Path $toolRoot 'field_packet_templates\*') -Destination (Join-Path $repo 'tools\field_packet_templates')
    & git -C $repo init --quiet | Out-Null
    & git -C $repo config user.name 'TPPA Test' | Out-Null
    & git -C $repo config user.email 'tppa-test@example.invalid' | Out-Null
    & git -C $repo add . | Out-Null
    & git -C $repo commit --quiet -m 'fixture' | Out-Null
    return $repo
}

Describe 'TPPA field readiness packet builder' {
    It 'atomically emits one provenance-bound packet with current qualification semantics' {
        $root = Join-Path $TestDrive 'success'
        New-Item -ItemType Directory -Path $root | Out-Null
        $repo = New-TestRepository $root
        $head = (& git -C $repo rev-parse HEAD).Trim()
        $plugin = Join-Path $root 'plugin.dll'
        $core = Join-Path $root 'core.dll'
        [IO.File]::WriteAllBytes($plugin, [byte[]](1, 2, 3, 4))
        [IO.File]::WriteAllBytes($core, [byte[]](5, 6, 7, 8))
        $output = Join-Path $root 'packet'

        $result = & $builder -RepositoryRoot $repo -PluginDllPath $plugin `
            -QualificationCoreDllPath $core -OutputDirectory $output `
            -SourceCommit $head -PluginVersion '2.2.6.104'

        $result.SourceCommit | Should Be $head
        $result.PluginVersion | Should Be '2.2.6.104'
        (Get-ChildItem -File $output).Count | Should Be 8
        $allText = (Get-ChildItem -File $output -Filter '*.md' |
            ForEach-Object { [IO.File]::ReadAllText($_.FullName) }) -join "`n"
        $allText | Should Not Match '__[A-Z0-9_]+__'
        $allText | Should Not Match '(?i)\bp95\b'
        $allText | Should Match 'Totals are 4, 3, 3, 3, 3 and 4 attempts'
        $allText | Should Match '0--30 and 240--300'

        $manifest = [IO.File]::ReadAllText((Join-Path $output 'TPPA.runtime-manifest.json')) | ConvertFrom-Json
        $manifest.sourceCommit | Should Be $head
        $manifest.pluginVersion | Should Be '2.2.6.104'
        $hashLines = [IO.File]::ReadAllLines((Join-Path $output 'HASHES.sha256'))
        $hashLines.Count | Should Be 7
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
                -SourceCommit $head -PluginVersion '2.2.6.104' | Out-Null
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
                -SourceCommit $head -PluginVersion '2.2.6.104' | Out-Null
        } catch { $caught = $_ }
        $caught | Should Not BeNullOrEmpty
        $caught.Exception.Message | Should Match 'tracked changes'
        Test-Path -LiteralPath $output | Should Be $false
    }
}
