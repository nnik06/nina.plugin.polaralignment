$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$servicePath = Join-Path $repoRoot 'tools\run_tppa_ra_witness_observer_service.ps1'

Describe 'persistent TPPA RA witness observer service contract' {
    It 'accepts only atomically published ready requests under one global lease' {
        $source = [IO.File]::ReadAllText($servicePath)
        $source.Contains('.witness-request.ready.json') | Should Be $true
        $source.Contains('Global\TppaRaWitnessObserverService') | Should Be $true
        $source.Contains('WaitOne(0)') | Should Be $true
        $source.Contains('[IO.FileMode]::CreateNew') | Should Be $true
        $source.Contains("'validate-witness-request'") | Should Be $true
        $source.Contains('Get-FileHash -LiteralPath $requestFile.FullName') |
            Should Be $true
    }

    It 'claims each request before invoking the one-shot observer' {
        $source = [IO.File]::ReadAllText($servicePath)
        $claimIndex = $source.IndexOf('Write-CreateNewUtf8 $claimPath')
        $invokeIndex = $source.IndexOf('& $ObserverScriptPath @observerParameters')
        ($claimIndex -ge 0) | Should Be $true
        ($invokeIndex -gt $claimIndex) | Should Be $true
        ([regex]::Matches($source,
            '& \$ObserverScriptPath @observerParameters')).Count | Should Be 1
    }

    It 'has no movement, UPAS, retry, or completion authority' {
        $source = [IO.File]::ReadAllText($servicePath)
        $source.Contains('SlewTo') | Should Be $false
        $source.Contains('UPAS') | Should Be $false
        $source.Contains('Start-Process') | Should Be $false
        $source.Contains('Stop-Process') | Should Be $false
        $source.Contains('GrantsMotionAuthority = $false') | Should Be $true
        $source.Contains('GrantsCompletionAuthority = $false') | Should Be $true
    }

    It 'processes one atomic ready request once and requires an outcome file' {
        $requests = Join-Path $TestDrive 'requests'
        $outcomes = Join-Path $TestDrive 'outcomes'
        $evidence = Join-Path $TestDrive 'evidence'
        foreach ($directory in @($requests, $outcomes, $evidence)) {
            [IO.Directory]::CreateDirectory($directory) | Out-Null
        }
        $runId = 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee'
        $requestPath = Join-Path $requests 'point-A.witness-request.ready.json'
        [IO.File]::WriteAllText($requestPath, (@{
            runId = $runId
            positionId = 'A'
            sequenceIndex = 0
            requestDigest = ('a' * 64)
        } | ConvertTo-Json -Compress))

        $fakeCli = Join-Path $TestDrive 'fake-cli.cmd'
        [IO.File]::WriteAllText($fakeCli, "@echo {`"valid`":true}`r`n@exit /b 0`r`n")
        $fakeCapture = Join-Path $TestDrive 'fake-capture.ps1'
        [IO.File]::WriteAllText($fakeCapture, "param()`r`n")
        $fakeObserver = Join-Path $TestDrive 'fake-observer.ps1'
        [IO.File]::WriteAllText($fakeObserver, @'
param($RequestPath,$OutcomePath,$EvidenceDirectory,$CaptureScriptPath,$QualificationCliPath)
[IO.File]::WriteAllText($OutcomePath, '{"status":"captured"}')
[IO.File]::AppendAllText((Join-Path (Split-Path -Parent $OutcomePath) 'invocations.txt'), "1`n")
'{"status":"captured"}'
'@)

        $result = & $servicePath `
            -RequestDirectory $requests `
            -OutcomeDirectory $outcomes `
            -EvidenceRoot $evidence `
            -ObserverScriptPath $fakeObserver `
            -CaptureScriptPath $fakeCapture `
            -QualificationCliPath $fakeCli `
            -PollMilliseconds 50 `
            -MaximumRequests 1 `
            -MaximumRuntimeMinutes 1 | ConvertFrom-Json

        $result.Status | Should Be 'request-limit-reached'
        $result.ProcessedRequests | Should Be 1
        ([IO.File]::ReadAllLines((Join-Path $outcomes 'invocations.txt'))).Count |
            Should Be 1
        @(Get-ChildItem -LiteralPath (Join-Path $evidence 'service-ledger') `
            -Filter '*.claimed.json').Count | Should Be 1
        $serviceResult = [IO.File]::ReadAllText((Join-Path `
            (Join-Path $evidence $runId) "$runId-0-A-service-result.json")) |
            ConvertFrom-Json
        $serviceResult.status | Should Be 'captured'
    }
}
