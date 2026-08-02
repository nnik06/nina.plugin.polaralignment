$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$observerPath = Join-Path $repoRoot 'tools\run_tppa_ra_witness_observer.ps1'

Describe 'one-shot TPPA RA witness observer contract' {
    It 'pins request validation, immutable hashes, and one create-new attempt' {
        $source = [IO.File]::ReadAllText($observerPath)
        $source.Contains("'validate-witness-request'") | Should Be $true
        $source.Contains('requiredObserverPipelineDigest') | Should Be $true
        $source.Contains('requiredPointCapturePipelineDigest') | Should Be $true
        $source.Contains('[IO.FileMode]::CreateNew') | Should Be $true
        $source.Contains('attemptNumber = 1') | Should Be $true
        $source.Contains('maximumAttempts') | Should Be $true
    }

    It 'invokes the point capture exactly once and never owns movement' {
        $source = [IO.File]::ReadAllText($observerPath)
        ([regex]::Matches($source, '& \$CaptureScriptPath @captureParameters')).Count |
            Should Be 1
        $source.Contains('Start-Process') | Should Be $false
        $source.Contains('Stop-Process') | Should Be $false
        $source.Contains('GrantsMotionAuthority = $false') | Should Be $true
        $source.Contains('GrantsCompletionAuthority = $false') | Should Be $true
    }

    It 'delegates canonical outcome creation and validation to the headless core' {
        $source = [IO.File]::ReadAllText($observerPath)
        $source.Contains("'create-witness-outcome'") | Should Be $true
        $source.Contains("'validate-witness-outcome'") | Should Be $true
        $source.Contains('observer-pipeline-digest') | Should Be $true
    }
}
