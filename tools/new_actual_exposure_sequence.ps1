[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$OutputPath,

    [Parameter(Mandatory)]
    [ValidateSet(
        'EdgeHD-9.25-0.7-OAG-L-ASI2600MM-gain100-bin1',
        'WO-GT81-IV-0.8-OAG-L-ASI2600MM-gain100-bin1')]
    [string]$OpticalTrainId,

    [Parameter(Mandatory)]
    [ValidateScript({
        -not [string]::IsNullOrWhiteSpace($_) -and
        $_ -notmatch '^(?i:none|unknown|--)$'
    })]
    [string]$RequiredFilterName,

    [ValidateRange(10, 30)]
    [int]$ControlExposureSeconds = 30,

    [ValidateRange(0, 1000)]
    [int]$Gain = 100,

    [ValidateRange(0, 255)]
    [int]$Offset = 50,

    [ValidateSet(1, 2, 3, 4)]
    [int]$Binning = 1
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$conditionCollectionType =
    'System.Collections.ObjectModel.ObservableCollection`1[[NINA.Sequencer.Conditions.ISequenceCondition, NINA.Sequencer]], System.ObjectModel'
$itemCollectionType =
    'System.Collections.ObjectModel.ObservableCollection`1[[NINA.Sequencer.SequenceItem.ISequenceItem, NINA.Sequencer]], System.ObjectModel'
$triggerCollectionType =
    'System.Collections.ObjectModel.ObservableCollection`1[[NINA.Sequencer.Trigger.ISequenceTrigger, NINA.Sequencer]], System.ObjectModel'
$sequentialStrategyType =
    'NINA.Sequencer.Container.ExecutionStrategy.SequentialStrategy, NINA.Sequencer'

function New-Reference {
    param([Parameter(Mandatory)][string]$Id)
    return [ordered]@{ '$ref' = $Id }
}

function New-Collection {
    param(
        [Parameter(Mandatory)][string]$Id,
        [Parameter(Mandatory)][string]$Type,
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Values
    )
    return [ordered]@{
        '$id' = $Id
        '$type' = $Type
        '$values' = $Values
    }
}

function New-Strategy {
    return [ordered]@{ '$type' = $sequentialStrategyType }
}

function New-TakeExposure {
    param(
        [Parameter(Mandatory)][string]$Id,
        [Parameter(Mandatory)][string]$BinningId,
        [Parameter(Mandatory)][double]$ExposureSeconds,
        [Parameter(Mandatory)][string]$ParentId
    )
    return [ordered]@{
        '$id' = $Id
        '$type' = 'NINA.Sequencer.SequenceItem.Imaging.TakeExposure, NINA.Sequencer'
        ExposureTime = $ExposureSeconds
        Gain = $Gain
        Offset = $Offset
        Binning = [ordered]@{
            '$id' = $BinningId
            '$type' = 'NINA.Core.Model.Equipment.BinningMode, NINA.Core'
            X = $Binning
            Y = $Binning
        }
        ImageType = 'LIGHT'
        ExposureCount = 0
        Parent = New-Reference $ParentId
        ErrorBehavior = 0
        Attempts = 1
    }
}

function New-LoopedExposureContainer {
    param(
        [Parameter(Mandatory)][string]$Id,
        [Parameter(Mandatory)][string]$ConditionsId,
        [Parameter(Mandatory)][string]$LoopId,
        [Parameter(Mandatory)][string]$ItemsId,
        [Parameter(Mandatory)][string]$ExposureId,
        [Parameter(Mandatory)][string]$BinningId,
        [Parameter(Mandatory)][string]$TriggersId,
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][double]$ExposureSeconds,
        [Parameter(Mandatory)][int]$Iterations,
        [Parameter(Mandatory)][string]$ParentId
    )

    $loop = [ordered]@{
        '$id' = $LoopId
        '$type' = 'NINA.Sequencer.Conditions.LoopCondition, NINA.Sequencer'
        CompletedIterations = 0
        Iterations = $Iterations
        Parent = New-Reference $Id
    }
    $exposure = New-TakeExposure `
        -Id $ExposureId `
        -BinningId $BinningId `
        -ExposureSeconds $ExposureSeconds `
        -ParentId $Id

    return [ordered]@{
        '$id' = $Id
        '$type' = 'NINA.Sequencer.Container.SequentialContainer, NINA.Sequencer'
        Strategy = New-Strategy
        Name = $Name
        Conditions = New-Collection $ConditionsId $conditionCollectionType @($loop)
        IsExpanded = $true
        Items = New-Collection $ItemsId $itemCollectionType @($exposure)
        Triggers = New-Collection $TriggersId $triggerCollectionType @()
        Parent = New-Reference $ParentId
        ErrorBehavior = 0
        Attempts = 1
    }
}

$fullOutputPath = [IO.Path]::GetFullPath($OutputPath)
if ([IO.File]::Exists($fullOutputPath)) {
    throw "Refusing to overwrite existing sequence: $fullOutputPath"
}

$outputDirectory = [IO.Path]::GetDirectoryName($fullOutputPath)
if ([string]::IsNullOrWhiteSpace($outputDirectory)) {
    throw "Output path has no parent directory: $fullOutputPath"
}
[void][IO.Directory]::CreateDirectory($outputDirectory)

$preControls = New-LoopedExposureContainer `
    -Id '15' `
    -ConditionsId '16' `
    -LoopId '17' `
    -ItemsId '18' `
    -ExposureId '19' `
    -BinningId '20' `
    -TriggersId '21' `
    -Name "PRE CONTROLS - 5x${ControlExposureSeconds}s" `
    -ExposureSeconds $ControlExposureSeconds `
    -Iterations 5 `
    -ParentId '11'

$longExposure = New-TakeExposure `
    -Id '22' `
    -BinningId '23' `
    -ExposureSeconds 900 `
    -ParentId '11'

$postControls = New-LoopedExposureContainer `
    -Id '24' `
    -ConditionsId '25' `
    -LoopId '26' `
    -ItemsId '27' `
    -ExposureId '28' `
    -BinningId '29' `
    -TriggersId '30' `
    -Name "POST CONTROLS - 5x${ControlExposureSeconds}s" `
    -ExposureSeconds $ControlExposureSeconds `
    -Iterations 5 `
    -ParentId '11'

$annotation = [ordered]@{
    '$id' = '14'
    '$type' = 'NINA.Sequencer.SequenceItem.Utility.Annotation, NINA.Sequencer'
    Text = "Actual-exposure witness for $OpticalTrainId. Required filter: '$RequiredFilterName'. Preflight-fixed equipment, field, focus, cooling, tracking, and guiding are required. This sequence only acquires image frames."
    Parent = New-Reference '11'
    ErrorBehavior = 0
    Attempts = 1
}

$bracket = [ordered]@{
    '$id' = '11'
    '$type' = 'NINA.Sequencer.Container.SequentialContainer, NINA.Sequencer'
    Strategy = New-Strategy
    Name = "ACTUAL 900S WITNESS - $OpticalTrainId - 5x${ControlExposureSeconds}s + 900s + 5x${ControlExposureSeconds}s"
    Conditions = New-Collection '12' $conditionCollectionType @()
    IsExpanded = $true
    Items = New-Collection '13' $itemCollectionType @(
        $annotation,
        $preControls,
        $longExposure,
        $postControls)
    Triggers = New-Collection '31' $triggerCollectionType @()
    Parent = New-Reference '8'
    ErrorBehavior = 0
    Attempts = 1
}

$start = [ordered]@{
    '$id' = '4'
    '$type' = 'NINA.Sequencer.Container.StartAreaContainer, NINA.Sequencer'
    Strategy = New-Strategy
    Name = 'Start'
    Conditions = New-Collection '5' $conditionCollectionType @()
    IsExpanded = $true
    Items = New-Collection '6' $itemCollectionType @()
    Triggers = New-Collection '7' $triggerCollectionType @()
    Parent = New-Reference '1'
    ErrorBehavior = 0
    Attempts = 1
}

$target = [ordered]@{
    '$id' = '8'
    '$type' = 'NINA.Sequencer.Container.TargetAreaContainer, NINA.Sequencer'
    Strategy = New-Strategy
    Name = 'Targets'
    Conditions = New-Collection '9' $conditionCollectionType @()
    IsExpanded = $true
    Items = New-Collection '10' $itemCollectionType @($bracket)
    Triggers = New-Collection '32' $triggerCollectionType @()
    Parent = New-Reference '1'
    ErrorBehavior = 0
    Attempts = 1
}

$end = [ordered]@{
    '$id' = '33'
    '$type' = 'NINA.Sequencer.Container.EndAreaContainer, NINA.Sequencer'
    Strategy = New-Strategy
    Name = 'End'
    Conditions = New-Collection '34' $conditionCollectionType @()
    IsExpanded = $true
    Items = New-Collection '35' $itemCollectionType @()
    Triggers = New-Collection '36' $triggerCollectionType @()
    Parent = New-Reference '1'
    ErrorBehavior = 0
    Attempts = 1
}

$root = [ordered]@{
    '$id' = '1'
    '$type' = 'NINA.Sequencer.Container.SequenceRootContainer, NINA.Sequencer'
    Strategy = New-Strategy
    Name = "Actual 900s Witness - $OpticalTrainId"
    Conditions = New-Collection '2' $conditionCollectionType @()
    IsExpanded = $true
    Items = New-Collection '3' $itemCollectionType @($start, $target, $end)
    Triggers = New-Collection '37' $triggerCollectionType @()
    Parent = $null
    ErrorBehavior = 0
    Attempts = 1
}

$json = $root | ConvertTo-Json -Depth 32
$json = $json -replace "`r?`n", "`r`n"
$utf8NoBom = [Text.UTF8Encoding]::new($false)
$temporaryPath = Join-Path $outputDirectory (
    '.' + [IO.Path]::GetFileName($fullOutputPath) + '.' +
    [Guid]::NewGuid().ToString('N') + '.tmp')
try {
    [IO.File]::WriteAllText($temporaryPath, $json + "`r`n", $utf8NoBom)
    [IO.File]::Move($temporaryPath, $fullOutputPath, $false)
} catch [IO.IOException] {
    if ([IO.File]::Exists($fullOutputPath)) {
        throw "Refusing to overwrite existing sequence: $fullOutputPath"
    }
    throw
} finally {
    if ([IO.File]::Exists($temporaryPath)) {
        [IO.File]::Delete($temporaryPath)
    }
}

[pscustomobject]@{
    OutputPath = $fullOutputPath
    OpticalTrainId = $OpticalTrainId
    RequiredFilterName = $RequiredFilterName
    ControlFrameCountBefore = 5
    LongExposureSeconds = 900
    ControlFrameCountAfter = 5
    ControlExposureSeconds = $ControlExposureSeconds
    Gain = $Gain
    Offset = $Offset
    Binning = "${Binning}x${Binning}"
    Sha256 = (Get-FileHash -LiteralPath $fullOutputPath -Algorithm SHA256).Hash
}
