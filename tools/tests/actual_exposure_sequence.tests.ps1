Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$toolsRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$toolPath = Join-Path $toolsRoot 'new_actual_exposure_sequence.ps1'
$edgeTrain = 'EdgeHD-9.25-0.7-OAG-L-ASI2600MM-gain100-bin1'
$gt81Train = 'WO-GT81-IV-0.8-OAG-L-ASI2600MM-gain100-bin1'

function New-TestSequence {
    param(
        [Parameter(Mandatory)][string]$Path,
        [string]$Train = $edgeTrain
    )
    & $toolPath -OutputPath $Path -OpticalTrainId $Train | Out-Null
    return ([IO.File]::ReadAllText($Path) | ConvertFrom-Json)
}

Describe 'actual 900-second NINA sequence generator' {
    It 'emits the expected NINA root and empty start/end areas' {
        $path = Join-Path $TestDrive 'witness.json'
        $root = New-TestSequence $path
        $root.'$type' | Should Be 'NINA.Sequencer.Container.SequenceRootContainer, NINA.Sequencer'
        $root.Items.'$values'.Count | Should Be 3
        $root.Items.'$values'[0].'$type' |
            Should Be 'NINA.Sequencer.Container.StartAreaContainer, NINA.Sequencer'
        $root.Items.'$values'[0].Items.'$values'.Count | Should Be 0
        $root.Items.'$values'[2].'$type' |
            Should Be 'NINA.Sequencer.Container.EndAreaContainer, NINA.Sequencer'
        $root.Items.'$values'[2].Items.'$values'.Count | Should Be 0
    }

    It 'emits exactly five controls, one 900-second frame, and five controls' {
        $path = Join-Path $TestDrive 'bracket.json'
        $root = New-TestSequence $path
        $bracket = $root.Items.'$values'[1].Items.'$values'[0]
        $bracket.Items.'$values'.Count | Should Be 4

        $pre = $bracket.Items.'$values'[1]
        $long = $bracket.Items.'$values'[2]
        $post = $bracket.Items.'$values'[3]
        $pre.Conditions.'$values'[0].Iterations | Should Be 5
        $post.Conditions.'$values'[0].Iterations | Should Be 5
        $pre.Conditions.'$values'[0].CompletedIterations | Should Be 0
        $post.Conditions.'$values'[0].CompletedIterations | Should Be 0
        $pre.Items.'$values'[0].ExposureTime | Should Be 30
        $long.ExposureTime | Should Be 900
        $post.Items.'$values'[0].ExposureTime | Should Be 30
    }

    It 'pins light-frame camera settings in every exposure node' {
        $path = Join-Path $TestDrive 'settings.json'
        $root = New-TestSequence $path $gt81Train
        $bracket = $root.Items.'$values'[1].Items.'$values'[0]
        $exposures = @(
            $bracket.Items.'$values'[1].Items.'$values'[0],
            $bracket.Items.'$values'[2],
            $bracket.Items.'$values'[3].Items.'$values'[0])
        foreach ($exposure in $exposures) {
            $exposure.'$type' |
                Should Be 'NINA.Sequencer.SequenceItem.Imaging.TakeExposure, NINA.Sequencer'
            $exposure.Gain | Should Be 100
            $exposure.Offset | Should Be 50
            $exposure.Binning.X | Should Be 1
            $exposure.Binning.Y | Should Be 1
            $exposure.ImageType | Should Be 'LIGHT'
        }
    }

    It 'emits only the exact passive NINA type allow-list' {
        $path = Join-Path $TestDrive 'passive.json'
        New-TestSequence $path | Out-Null
        $json = [IO.File]::ReadAllText($path)
        $actualTypes = @([regex]::Matches($json, '"\$type"\s*:\s*"([^"]+)"') |
            ForEach-Object { $_.Groups[1].Value } |
            Sort-Object -Unique)
        $expectedTypes = @(
            'NINA.Core.Model.Equipment.BinningMode, NINA.Core',
            'NINA.Sequencer.Conditions.LoopCondition, NINA.Sequencer',
            'NINA.Sequencer.Container.EndAreaContainer, NINA.Sequencer',
            'NINA.Sequencer.Container.ExecutionStrategy.SequentialStrategy, NINA.Sequencer',
            'NINA.Sequencer.Container.SequenceRootContainer, NINA.Sequencer',
            'NINA.Sequencer.Container.SequentialContainer, NINA.Sequencer',
            'NINA.Sequencer.Container.StartAreaContainer, NINA.Sequencer',
            'NINA.Sequencer.Container.TargetAreaContainer, NINA.Sequencer',
            'NINA.Sequencer.SequenceItem.Imaging.TakeExposure, NINA.Sequencer',
            'NINA.Sequencer.SequenceItem.Utility.Annotation, NINA.Sequencer',
            'System.Collections.ObjectModel.ObservableCollection`1[[NINA.Sequencer.Conditions.ISequenceCondition, NINA.Sequencer]], System.ObjectModel',
            'System.Collections.ObjectModel.ObservableCollection`1[[NINA.Sequencer.SequenceItem.ISequenceItem, NINA.Sequencer]], System.ObjectModel',
            'System.Collections.ObjectModel.ObservableCollection`1[[NINA.Sequencer.Trigger.ISequenceTrigger, NINA.Sequencer]], System.ObjectModel' |
            Sort-Object)
        ($actualTypes -join "`n") | Should Be ($expectedTypes -join "`n")
    }

    It 'refuses to overwrite an existing sequence' {
        $path = Join-Path $TestDrive 'existing.json'
        [IO.File]::WriteAllText($path, 'do not replace')
        $threw = $false
        try {
            & $toolPath -OutputPath $path -OpticalTrainId $edgeTrain |
                Out-Null
        } catch {
            $threw = $true
        }
        $threw | Should Be $true
        [IO.File]::ReadAllText($path) | Should Be 'do not replace'
        [IO.Directory]::GetFiles($TestDrive, '.existing.json.*.tmp').Count |
            Should Be 0
    }

    It 'rejects an unsupported optical train' {
        $path = Join-Path $TestDrive 'unsupported.json'
        $threw = $false
        try {
            & $toolPath -OutputPath $path -OpticalTrainId 'unknown-train'
        } catch {
            $threw = $true
        }
        $threw | Should Be $true
        [IO.File]::Exists($path) | Should Be $false
    }

    It 'produces deterministic bytes for the same inputs' {
        $first = Join-Path $TestDrive 'first.json'
        $second = Join-Path $TestDrive 'second.json'
        New-TestSequence $first | Out-Null
        New-TestSequence $second | Out-Null
        [Convert]::ToBase64String([IO.File]::ReadAllBytes($first)) |
            Should Be ([Convert]::ToBase64String([IO.File]::ReadAllBytes($second)))
    }

    It 'emits unique object ids and only resolvable references' {
        $path = Join-Path $TestDrive 'references.json'
        New-TestSequence $path | Out-Null
        $json = [IO.File]::ReadAllText($path)
        $ids = @([regex]::Matches($json, '"\$id"\s*:\s*"([0-9]+)"') |
            ForEach-Object { $_.Groups[1].Value })
        $references = @([regex]::Matches($json, '"\$ref"\s*:\s*"([0-9]+)"') |
            ForEach-Object { $_.Groups[1].Value })
        $ids.Count | Should BeGreaterThan 0
        (@($ids | Select-Object -Unique)).Count | Should Be $ids.Count
        foreach ($reference in $references) {
            $ids.Contains($reference) | Should Be $true
        }
    }

    It 'serializes invariantly under comma-decimal and Turkish cultures' {
        $priorCulture = [Globalization.CultureInfo]::CurrentCulture
        $priorUiCulture = [Globalization.CultureInfo]::CurrentUICulture
        try {
            foreach ($cultureName in @('de-DE', 'tr-TR')) {
                $culture = [Globalization.CultureInfo]::GetCultureInfo($cultureName)
                [Globalization.CultureInfo]::CurrentCulture = $culture
                [Globalization.CultureInfo]::CurrentUICulture = $culture
                $path = Join-Path $TestDrive "culture-$cultureName.json"
                $root = New-TestSequence $path
                $root.Items.'$values'[1].Items.'$values'[0].Items.'$values'[2].ExposureTime |
                    Should Be 900
                [IO.File]::ReadAllText($path).Contains('900,0') | Should Be $false
            }
        } finally {
            [Globalization.CultureInfo]::CurrentCulture = $priorCulture
            [Globalization.CultureInfo]::CurrentUICulture = $priorUiCulture
        }
    }
}
