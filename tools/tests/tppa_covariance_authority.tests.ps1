$scriptPath = Join-Path $PSScriptRoot '..\new_tppa_covariance_authority.ps1'

Describe 'TPPA commissioned covariance authority generator' {
    BeforeEach {
        $caseRoot = Join-Path $TestDrive ([Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $caseRoot | Out-Null
        $repo = Join-Path $caseRoot 'repo'
        New-Item -ItemType Directory -Path $repo | Out-Null
        & git -C $repo init -q
        & git -C $repo config user.email test@example.invalid
        & git -C $repo config user.name test
        Set-Content -LiteralPath (Join-Path $repo seed.txt) -Value seed
        & git -C $repo add seed.txt
        & git -C $repo commit -q -m seed
        $head = (& git -C $repo rev-parse HEAD).Trim()
        $plugin = 'a' * 64
        $hardware = 'c' * 64
        $evidence = Join-Path $caseRoot 'evidence'
        New-Item -ItemType Directory -Path $evidence | Out-Null
        $entries = @()
        for ($runIndex=0; $runIndex -lt 20; $runIndex++) {
            $determinations = @()
            for ($determinationIndex=0; $determinationIndex -lt 3; $determinationIndex++) {
                $angle = (($runIndex - 9.5) * 0.00002) + ($determinationIndex * 0.000001)
                $digests = 0..2 | ForEach-Object {
                    $raw = "run-$runIndex-det-$determinationIndex-solve-$_"
                    $bytes = [Text.Encoding]::UTF8.GetBytes($raw)
                    [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
                }
                $determinations += [ordered]@{
                    correctionSequenceNumber=0; freshSolvesUncached=$true
                    geometryQualified=$true; minimumArcSpanQualified=$true; closureQualified=$true
                    sourceVectorDigests=$digests
                    mountAxisVector=[ordered]@{x=[math]::Sin($angle); y=0.0; z=[math]::Cos($angle)}
                }
            }
            $run = [ordered]@{
                schemaVersion=6; runId=[Guid]::NewGuid().ToString('D')
                pipelineDigest=$plugin; hardwareConfigurationId=$hardware
                mechanicalStateId=('b'*64)
                refractionAdjustmentEnabled=$true; poleTarget='trueCelestialPole'
                atmosphereTemperatureCelsius=35.0; solverIdentity='astap-1'
                pluginAssembly=[ordered]@{sha256=$plugin}; determinations=$determinations
            }
            $path = Join-Path $evidence ("run-{0:D2}.json" -f $runIndex)
            [IO.File]::WriteAllText($path,($run|ConvertTo-Json -Depth 12 -Compress),[Text.UTF8Encoding]::new($false))
            $entries += [ordered]@{path=$path;sha256=(Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant()}
        }
        $manifest = [ordered]@{
            schemaVersion=1; repositoryHead=$head; pluginAssemblySha256=$plugin
            hardwareConfigurationId=$hardware; mechanicalStateId=('b'*64)
            loadProfileId='full-rig-v1'; catalogIdentity='d50-v17'; targetSkyArcId='safe-arc-a'
            temperatureC=[ordered]@{minimum=30.0;maximum=45.0}; evidenceFiles=$entries
        }
        $manifestPath = Join-Path $caseRoot manifest.json
        [IO.File]::WriteAllText($manifestPath,($manifest|ConvertTo-Json -Depth 12 -Compress),[Text.UTF8Encoding]::new($false))
        $output = Join-Path $caseRoot authority.json
    }

    It 'produces an exact-build, no-systematic authority from twenty unique no-motion runs' {
        $summary = (& $scriptPath -ManifestPath $manifestPath -RepositoryRoot $repo -OutputPath $output `
            -CommissionedUtc '2026-08-03T00:00:00Z' | ConvertFrom-Json)
        $authority = Get-Content $output -Raw | ConvertFrom-Json
        $summary.SourceAttemptCount | Should Be 20
        $authority.sourceAttemptCount | Should Be 20
        $authority.sourcePassCount | Should Be 20
        $authority.sharedSystematicIncluded | Should Be $false
        $authority.confidenceLevel | Should Be 0.5
        $authority.repositoryHead | Should Be $head
        $authority.pluginAssemblySha256 | Should Be $plugin
        [double]$authority.covarianceFloorSquareDegrees[0][0] | Should BeGreaterThan 0
        [double]$authority.covarianceFloorSquareDegrees[0][1] | Should Be 0
        [double]$authority.covarianceFloorSquareDegrees[1][1] | Should Be ([double]$authority.covarianceFloorSquareDegrees[0][0])
    }

    It 'rejects fewer than twenty attempts' {
        $manifest.evidenceFiles = @($manifest.evidenceFiles | Select-Object -First 19)
        [IO.File]::WriteAllText($manifestPath,($manifest|ConvertTo-Json -Depth 12 -Compress),[Text.UTF8Encoding]::new($false))
        $message = try { & $scriptPath -ManifestPath $manifestPath -RepositoryRoot $repo -OutputPath $output; '' } catch { $_.Exception.Message }
        $message | Should Match 'At least 20'
    }

    It 'rejects source evidence from a different mechanical epoch' {
        $first = $manifest.evidenceFiles[0].path
        $run = Get-Content $first -Raw | ConvertFrom-Json
        $run.mechanicalStateId = 'd' * 64
        [IO.File]::WriteAllText($first,($run|ConvertTo-Json -Depth 12 -Compress),[Text.UTF8Encoding]::new($false))
        $manifest.evidenceFiles[0].sha256 = (Get-FileHash $first -Algorithm SHA256).Hash.ToLowerInvariant()
        [IO.File]::WriteAllText($manifestPath,($manifest|ConvertTo-Json -Depth 12 -Compress),[Text.UTF8Encoding]::new($false))
        $message = try { & $scriptPath -ManifestPath $manifestPath -RepositoryRoot $repo -OutputPath $output; '' } catch { $_.Exception.Message }
        $message | Should Match 'mechanical state'
    }
    It 'rejects a source run without true-pole refraction' {
        $first = $manifest.evidenceFiles[0].path
        $run = Get-Content $first -Raw | ConvertFrom-Json
        $run.refractionAdjustmentEnabled = $false
        [IO.File]::WriteAllText($first,($run|ConvertTo-Json -Depth 12 -Compress),[Text.UTF8Encoding]::new($false))
        $manifest.evidenceFiles[0].sha256 = (Get-FileHash $first -Algorithm SHA256).Hash.ToLowerInvariant()
        [IO.File]::WriteAllText($manifestPath,($manifest|ConvertTo-Json -Depth 12 -Compress),[Text.UTF8Encoding]::new($false))
        $message = try { & $scriptPath -ManifestPath $manifestPath -RepositoryRoot $repo -OutputPath $output; '' } catch { $_.Exception.Message }
        $message | Should Match 'true-pole refraction'
    }

    It 'rejects a duplicate evidence file' {
        $manifest.evidenceFiles[19] = $manifest.evidenceFiles[0]
        [IO.File]::WriteAllText($manifestPath,($manifest|ConvertTo-Json -Depth 12 -Compress),[Text.UTF8Encoding]::new($false))
        $message = try { & $scriptPath -ManifestPath $manifestPath -RepositoryRoot $repo -OutputPath $output; '' } catch { $_.Exception.Message }
        $message | Should Match 'Duplicate evidence'
    }
}
