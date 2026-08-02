$ErrorActionPreference = 'Stop'

Describe 'TPPA solution CI contract' {
    BeforeAll {
        $script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
        $script:Solution = [IO.File]::ReadAllText((Join-Path $script:RepoRoot 'PolarAlignment.sln'))
        $script:Pipeline = [IO.File]::ReadAllText((Join-Path $script:RepoRoot 'bitbucket-pipelines.yml'))
        $script:TestProjectGuid = '{BA6590F4-4B69-42CF-AFC3-82FFDA0116C4}'
    }

    It 'builds the NUnit project in the Release Any CPU solution configuration' {
        $mapping = "$($script:TestProjectGuid).Release|Any CPU.Build.0 = Release|Any CPU"
        $script:Solution.Contains($mapping) | Should Be $true
    }

    It 'builds the complete solution before executing the no-build NUnit run' {
        $build = 'dotnet build PolarAlignment.sln -c Release --no-restore'
        $test = 'dotnet test NINA.Plugins.PolarAlignment.Test/NINA.Plugins.PolarAlignment.Test.csproj -c Release --no-build --no-restore'
        $buildIndex = $script:Pipeline.IndexOf($build, [StringComparison]::Ordinal)
        $testIndex = $script:Pipeline.IndexOf($test, [StringComparison]::Ordinal)
        ($buildIndex -ge 0) | Should Be $true
        ($testIndex -gt $buildIndex) | Should Be $true
    }
}
