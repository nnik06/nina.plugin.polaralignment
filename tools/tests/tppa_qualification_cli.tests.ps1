Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$toolsRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$repoRoot = Split-Path -Parent $toolsRoot
$projectPath = Join-Path $toolsRoot 'TppaQualificationCli\TppaQualificationCli.csproj'
$coreProjectPath = Join-Path $repoRoot 'QualificationCore\NINA.Plugins.PolarAlignment.QualificationCore.csproj'
$programPath = Join-Path $toolsRoot 'TppaQualificationCli\Program.cs'
$binderPath = Join-Path $repoRoot 'QualificationCore\TppaAbsoluteEvidenceBinder.cs'
$witnessProducerPath = Join-Path $repoRoot 'QualificationCore\TppaRaRotationWitnessEvidenceProducer.cs'

$project = [IO.File]::ReadAllText($projectPath)
$coreProject = [IO.File]::ReadAllText($coreProjectPath)
$program = [IO.File]::ReadAllText($programPath)
$binder = [IO.File]::ReadAllText($binderPath)
$witnessProducer = [IO.File]::ReadAllText($witnessProducerPath)

Describe 'headless TPPA qualification CLI contract' {
    It 'references a real headless qualification project without source globs' {
        $project.Contains('QualificationCore\*.cs') | Should Be $false
        $project.Contains('NINA.Plugins.PolarAlignment.QualificationCore.csproj') |
            Should Be $true
        $coreProject.Contains('TppaFastQualificationPolicySource.cs') |
            Should Be $true
        $coreProject.Contains('<TargetFramework>net8.0</TargetFramework>') |
            Should Be $true
        $coreProject.Contains('UseWPF') | Should Be $false
        $coreProject.Contains('net8.0-windows') | Should Be $false
        $program.Contains('typeof(TppaFastQualification).Assembly') |
            Should Be $true
    }

    It 'uses distinct exits for qualified, not-qualified, invalid, and verify failure' {
        $program.Contains('return binding.IsQualified == true ? 0 : 1;') | Should Be $true
        $program.Contains('return 2;') | Should Be $true
        $program.Contains('return 3;') | Should Be $true
        $program.Contains('return valid ? 0 : 4;') | Should Be $true
    }

    It 'writes no receipt before structural evidence validity is established' {
        $validityGate = $program.IndexOf(
            'if (!binding.EvidenceValid || binding.ReceiptJson == null)')
        $receiptCreate = $program.IndexOf('FileMode.CreateNew')
        $validityGate | Should BeGreaterThan -1
        $receiptCreate | Should BeGreaterThan $validityGate
    }

    It 'never grants motion authority' {
        $program.Contains('grantsMotionAuthority = false') | Should Be $true
        $binder.Contains('never grants motion authority') | Should Be $true
        $binder.ToLowerInvariant().Contains('upasbridge') | Should Be $false
    }

    It 'strictly produces witness evidence in the headless core' {
        $program.Contains('"produce-witness" => ProduceWitness(options)') | Should Be $true
        $program.Contains('MissingMemberHandling = MissingMemberHandling.Error') |
            Should Be $true
        $program.Contains('TppaRaRotationWitnessEvidenceProducer.Produce') |
            Should Be $true
        $witnessProducer.Contains('NINA.Core') | Should Be $false
        $witnessProducer.Contains('PierSide SideOfPier') | Should Be $false
        $witnessProducer.Contains('GrantsMotionAuthority => false') | Should Be $true
    }

    It 'validates witness handshakes through the headless authority-free core' {
        $program.Contains('"create-witness-request" => CreateWitnessRequest(options)') |
            Should Be $true
        $program.Contains('"validate-witness-request" => ValidateWitnessRequest(options)') |
            Should Be $true
        $program.Contains('"create-witness-outcome" => CreateWitnessOutcome(options)') |
            Should Be $true
        $program.Contains('"validate-witness-outcome" => ValidateWitnessOutcome(options)') |
            Should Be $true
        $program.Contains('TppaRaRotationWitnessHandshake.ValidateRequest') |
            Should Be $true
        $program.Contains('TppaRaRotationWitnessHandshake.ValidateOutcome') |
            Should Be $true
        $program.Contains('FileMode.CreateNew') | Should Be $true
        $program.Contains('.witness-request.ready.json') | Should Be $true
        $program.Contains('File.Move(temporaryPath, requestPath)') | Should Be $true
    }

    It 'has no forbidden NINA or desktop assemblies in a built output' {
        $output = Join-Path $toolsRoot 'TppaQualificationCli\bin\Release\net8.0'
        if (-not (Test-Path -LiteralPath $output -PathType Container)) {
            Set-ItResult -Skipped -Because 'Release CLI output has not been built.'
            return
        }
        foreach ($name in @(
            'NINA.Plugins.PolarAlignment.dll',
            'NINA.Core.dll',
            'PresentationFramework.dll',
            'PresentationCore.dll',
            'WindowsBase.dll')) {
            (Test-Path -LiteralPath (Join-Path $output $name)) | Should Be $false
        }
    }
}
