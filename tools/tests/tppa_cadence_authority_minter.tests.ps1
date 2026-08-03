$minter = Join-Path $PSScriptRoot '..\new_tppa_cadence_authority.ps1'

Describe 'TPPA commissioned cadence authority minter' {
    BeforeEach {
        $root=Join-Path $TestDrive ([Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $root | Out-Null
        $script:mechanical='d'*64
        $script:rig='c'*64
        $script:plugin='b'*64
        $script:nominationPath=Join-Path $root 'nomination.json'
        $script:vectorPath=Join-Path $root 'vector.json'
        $script:nullPath=Join-Path $root 'null.json'
        $script:timingPath=Join-Path $root 'timing.json'
        $script:runtimePath=Join-Path $root 'runtime.json'
        $script:outputPath=Join-Path $root 'authority.json'

        [ordered]@{SchemaVersion=2;Event='tppa-settle-cadence-nomination';CandidateCadenceQualified=$true;ProductionSettleQualified=$false;ValidRunCount=20;DubaiNightCount=2;CandidateSettleSeconds=15.0;RigConfigurationId=$rig}|ConvertTo-Json|Set-Content $nominationPath -Encoding utf8NoBOM
        $nominationHash=(Get-FileHash $nominationPath -Algorithm SHA256).Hash
        [ordered]@{SchemaVersion=2;Event='tppa-settle-vector-pair-campaign';CandidateVectorPairQualified=$true;ProductionSettleQualified=$false;ValidPairCount=20;DubaiNightCount=2;RigConfigurationId=$rig;CandidateSettleSeconds=15.0;CandidateNominationReceiptSha256=$nominationHash;ObservedMaximumPairSeparationMinutes=0.24;ObservedP95PairSeparationMinutes=0.18;MaximumObservedNightFraction=0.5}|ConvertTo-Json|Set-Content $vectorPath -Encoding utf8NoBOM
        [ordered]@{SchemaVersion=2;Event='tppa-settle-null-dataset';NullArmDatasetQualified=$true;ProductionSettleQualified=$false;ValidNullPairCount=10;DubaiNightCount=2;RigConfigurationId=$rig;ObservedMaximumNullLegSeparationMinutes=0.16;ObservedMaximumNullMidpointSeparationMinutes=0.14;ObservedP95NullLegSeparationMinutes=0.14;ObservedP95NullMidpointSeparationMinutes=0.12;MaximumObservedNightFraction=0.5}|ConvertTo-Json|Set-Content $nullPath -Encoding utf8NoBOM
        [ordered]@{schemaVersion=1;packageId='NINA.Plugins.PolarAlignment';pluginVersion='2.2.6.104';sourceCommit=('a'*40);artifacts=@([ordered]@{name='NINA.Plugins.PolarAlignment.dll';sha256=$plugin},[ordered]@{name='NINA.Plugins.PolarAlignment.QualificationCore.dll';sha256=('e'*64)})}|ConvertTo-Json -Depth 5|Set-Content $runtimePath -Encoding utf8NoBOM
        $runtimeHash=(Get-FileHash $runtimePath -Algorithm SHA256).Hash.ToLowerInvariant()
        [ordered]@{SchemaVersion=1;Event='tppa-fresh-determination-timing-campaign';TimingCampaignQualified=$true;ProductionSettleQualified=$false;ValidDeterminationCount=59;ExcludedSampleCount=0;DubaiNightCount=2;MaximumObservedNightFraction=0.51;HardwareConfigurationId=$rig;MechanicalStateId=$mechanical;RuntimeManifestSha256=$runtimeHash;PluginAssemblySha256=$plugin;LoadProfileId='hae29c-ec-full-rig-v1';TemperatureC=[ordered]@{Minimum=32.0;Maximum=42.0};ObservedMaximumSeconds=64.0;ConservativeUpperBoundSeconds=69.0;P95CoverageConfidence=0.9515}|ConvertTo-Json -Depth 5|Set-Content $timingPath -Encoding utf8NoBOM
        $script:arguments=@{NominationReportPath=$nominationPath;VectorPairReportPath=$vectorPath;NullPairReportPath=$nullPath;TimingReportPath=$timingPath;RuntimeManifestPath=$runtimePath;MechanicalStateId=$mechanical;OutputPath=$outputPath}
    }

    It 'mints schema 3 only from all four qualified hash-linked stages' {
        $result=& $minter @arguments
        $authority=Get-Content $outputPath -Raw|ConvertFrom-Json
        $authority.schemaVersion|Should Be 3
        $authority.sourceNullPairCampaignSha256|Should Match '^[0-9a-f]{64}$'
        $authority.sourceTimingCampaignSha256|Should Match '^[0-9a-f]{64}$'
        $authority.sourceTimingDeterminationCount|Should Be 59
        $authority.maximumFreshDeterminationSeconds|Should Be 69
        $result.AuthoritySha256|Should Match '^[0-9a-f]{64}$'
    }

    It 'rejects omission of the null campaign' {
        Remove-Item $nullPath
        $threw=$false; try { & $minter @arguments | Out-Null } catch { $threw=$true }; $threw|Should Be $true
    }

    It 'rejects candidate maximum above 1.5 times null maximum' {
        $vector=Get-Content $vectorPath -Raw|ConvertFrom-Json
        $vector.ObservedMaximumPairSeparationMinutes=0.25
        $vector|ConvertTo-Json|Set-Content $vectorPath -Encoding utf8NoBOM
        $threw=$false; try { & $minter @arguments | Out-Null } catch { $threw=$true }; $threw|Should Be $true
    }

    It 'rejects unqualified timing evidence' {
        $timing=Get-Content $timingPath -Raw|ConvertFrom-Json
        $timing.TimingCampaignQualified=$false
        $timing|ConvertTo-Json -Depth 5|Set-Content $timingPath -Encoding utf8NoBOM
        $threw=$false; try { & $minter @arguments | Out-Null } catch { $threw=$true }; $threw|Should Be $true
    }

    It 'rejects an existing output instead of overwriting authority' {
        Set-Content $outputPath -Value 'sentinel'
        $threw=$false; try { & $minter @arguments | Out-Null } catch { $threw=$true }; $threw|Should Be $true
        (Get-Content $outputPath -Raw).Trim()|Should Be 'sentinel'
    }
}