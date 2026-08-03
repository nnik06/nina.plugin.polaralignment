#requires -Version 7.5
param(
    [Parameter(Mandatory = $true)]
    [string[]]$TimingReceiptPath,
    [ValidateRange(59, 500)]
    [int]$MinimumDeterminations = 59,
    [ValidateRange(2, 20)]
    [int]$MinimumDubaiNights = 2,
    [ValidateRange(1, 250)]
    [int]$MinimumPerDirection = 8,
    [ValidateRange(0.5, 1.0)]
    [double]$MaximumNightFraction = 0.70,
    [ValidateRange(29.0, 31.0)]
    [double]$ReferenceSettleSeconds = 30.0,
    [ValidateRange(0.001, 1.0)]
    [double]$SettleToleranceSeconds = 0.01,
    [ValidateRange(0.0, 30.0)]
    [double]$ReserveMarginSeconds = 5.0,
    [ValidateRange(1.0, 75.0)]
    [double]$MaximumCommissionedDurationSeconds = 75.0
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

function Get-PropertyValue([object]$Object, [string]$Name) {
    if ($null -eq $Object) { throw "Cannot read '$Name' from null." }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { throw "Missing field '$Name'." }
    return $property.Value
}
function ConvertTo-FiniteDouble([object]$Value, [string]$Name) {
    if ($Value -is [string] -or $Value -is [bool] -or $Value -isnot [ValueType]) {
        throw "Field '$Name' is not a JSON number."
    }
    $result = [double]$Value
    if (-not [double]::IsFinite($result)) { throw "Field '$Name' is not finite." }
    return $result
}
function ConvertTo-StrictInt([object]$Value, [string]$Name) {
    $result = ConvertTo-FiniteDouble $Value $Name
    if ([Math]::Truncate($result) -ne $result) { throw "Field '$Name' is not an integer." }
    return [int]$result
}
function ConvertTo-StrictBoolean([object]$Value, [string]$Name) {
    if ($Value -isnot [bool]) { throw "Field '$Name' is not a boolean." }
    return [bool]$Value
}
function Require-LowerHex([object]$Value, [string]$Name, [int]$Length = 64) {
    if ($Value -isnot [string] -or $Value -notmatch "^[0-9a-f]{$Length}$") {
        throw "Field '$Name' is not canonical lowercase hexadecimal."
    }
    return [string]$Value
}
function ConvertTo-ObservedTime([object]$Value, [string]$Name) {
    if ($Value -isnot [string] -or $Value -notmatch '(Z|[+-]\d{2}:\d{2})$') {
        throw "Field '$Name' is not an offset-bearing timestamp."
    }
    return [DateTimeOffset]::Parse(
        $Value,
        [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::RoundtripKind)
}
function Get-DubaiNight([DateTimeOffset]$Timestamp) {
    return $Timestamp.ToOffset([TimeSpan]::FromHours(4)).AddHours(-12).Date.ToString('yyyy-MM-dd')
}

$receipts = [Collections.Generic.List[object]]::new()
foreach ($inputPath in $TimingReceiptPath) {
    $issues = [Collections.Generic.List[string]]::new()
    $fullPath = [IO.Path]::GetFullPath($inputPath)
    try {
        $receipt = [IO.File]::ReadAllText($fullPath) | ConvertFrom-Json -DateKind String
        if ((ConvertTo-StrictInt (Get-PropertyValue $receipt 'SchemaVersion') 'SchemaVersion') -ne 1) { $issues.Add('SchemaVersion is not 1') }
        if ([string](Get-PropertyValue $receipt 'Event') -ne 'tppa-fresh-determination-timing') { $issues.Add('Event is invalid') }
        $receiptId = [string](Get-PropertyValue $receipt 'ReceiptId')
        [Guid]$parsed = [Guid]::Empty
        if (-not [Guid]::TryParseExact($receiptId, 'D', [ref]$parsed) -or $parsed -eq [Guid]::Empty) { $issues.Add('ReceiptId is invalid') }
        $started = ConvertTo-ObservedTime (Get-PropertyValue $receipt 'StartedUtc') 'StartedUtc'
        $completed = ConvertTo-ObservedTime (Get-PropertyValue $receipt 'CompletedUtc') 'CompletedUtc'
        if ($completed -le $started) { $issues.Add('Lifecycle is not increasing') }
        $declaredNight = [string](Get-PropertyValue $receipt 'DubaiNight')
        if ($declaredNight -ne (Get-DubaiNight $started) -or $declaredNight -ne (Get-DubaiNight $completed)) { $issues.Add('DubaiNight does not match timestamps') }
        $direction = [string](Get-PropertyValue $receipt 'SlewDirection')
        if ($direction -notin @('IncreasingRA', 'DecreasingRA')) { $issues.Add('SlewDirection is invalid') }
        $settle = ConvertTo-FiniteDouble (Get-PropertyValue $receipt 'EffectiveSettleSeconds') 'EffectiveSettleSeconds'
        if ([Math]::Abs($settle - $ReferenceSettleSeconds) -gt $SettleToleranceSeconds) { $issues.Add('Timing did not use the 30-second fallback cadence') }
        $duration = ConvertTo-FiniteDouble (Get-PropertyValue $receipt 'TotalDurationSeconds') 'TotalDurationSeconds'
        $timestampDuration = ($completed - $started).TotalSeconds
        if ($duration -le 0 -or [Math]::Abs($duration - $timestampDuration) -gt 0.25) { $issues.Add('Duration contradicts lifecycle timestamps') }
        if ([string](Get-PropertyValue $receipt 'TimingPath') -ne 'fresh-three-point-plus-return-field') { $issues.Add('TimingPath is not the runtime-reserved path') }
        foreach ($field in @('MeasurementOnly', 'RefractionAdjustmentEnabled', 'CompletedSuccessfully')) {
            if (-not (ConvertTo-StrictBoolean (Get-PropertyValue $receipt $field) $field)) { $issues.Add("$field is false") }
        }
        if (ConvertTo-StrictBoolean (Get-PropertyValue $receipt 'CadenceAuthorityConsumed') 'CadenceAuthorityConsumed') { $issues.Add('Timing consumed cadence authority') }
        if ((ConvertTo-StrictInt (Get-PropertyValue $receipt 'UpasMovementCount') 'UpasMovementCount') -ne 0) { $issues.Add('UPAS moved during timing receipt') }
        $runtime = Require-LowerHex (Get-PropertyValue $receipt 'RuntimeManifestSha256') 'RuntimeManifestSha256'
        $plugin = Require-LowerHex (Get-PropertyValue $receipt 'PluginAssemblySha256') 'PluginAssemblySha256'
        $hardware = Require-LowerHex (Get-PropertyValue $receipt 'HardwareConfigurationId') 'HardwareConfigurationId'
        $mechanical = Require-LowerHex (Get-PropertyValue $receipt 'MechanicalStateId') 'MechanicalStateId'
        $load = [string](Get-PropertyValue $receipt 'LoadProfileId')
        if ([string]::IsNullOrWhiteSpace($load)) { $issues.Add('LoadProfileId is empty') }
        $temperature = ConvertTo-FiniteDouble (Get-PropertyValue $receipt 'TemperatureC') 'TemperatureC'
        $sourceLog = [IO.Path]::GetFullPath([string](Get-PropertyValue $receipt 'SourceNinaLogPath'))
        $sourceLogSha = Require-LowerHex (Get-PropertyValue $receipt 'SourceNinaLogSha256') 'SourceNinaLogSha256'
        $sourceLineNumber = ConvertTo-StrictInt (Get-PropertyValue $receipt 'SourceLineNumber') 'SourceLineNumber'
        $sourceEventSha = Require-LowerHex (Get-PropertyValue $receipt 'SourceTimingEventSha256') 'SourceTimingEventSha256'
        if (-not [IO.File]::Exists($sourceLog)) {
            $issues.Add('Source NINA log is missing')
        } elseif ((Get-FileHash -LiteralPath $sourceLog -Algorithm SHA256).Hash.ToLowerInvariant() -ne $sourceLogSha) {
            $issues.Add('Source NINA log hash does not match')
        } else {
            $sourceLines = [IO.File]::ReadAllLines($sourceLog)
            if ($sourceLineNumber -lt 1 -or $sourceLineNumber -gt $sourceLines.Length) {
                $issues.Add('Source line number is outside the NINA log')
            } else {
                $sourceLine = $sourceLines[$sourceLineNumber - 1]
                $actualEventSha = ([Convert]::ToHexString(
                    [Security.Cryptography.SHA256]::HashData(
                        [Text.Encoding]::UTF8.GetBytes($sourceLine)))).ToLowerInvariant()
                if ($actualEventSha -ne $sourceEventSha -or
                        -not $sourceLine.Contains("receiptId=$receiptId", [StringComparison]::Ordinal)) {
                    $issues.Add('Source timing event does not match the receipt')
                }
            }
        }
        $receipts.Add([pscustomobject][ordered]@{
            Path=$fullPath
            Sha256=(Get-FileHash -LiteralPath $fullPath -Algorithm SHA256).Hash.ToLowerInvariant()
            ReceiptId=$receiptId
            DubaiNight=$declaredNight
            SlewDirection=$direction
            TotalDurationSeconds=$duration
            RuntimeManifestSha256=$runtime
            PluginAssemblySha256=$plugin
            HardwareConfigurationId=$hardware
            MechanicalStateId=$mechanical
            LoadProfileId=$load
            TemperatureC=$temperature
            SourceNinaLogPath=$sourceLog
            SourceNinaLogSha256=$sourceLogSha
            SourceLineNumber=$sourceLineNumber
            SourceTimingEventSha256=$sourceEventSha
            Valid=$issues.Count -eq 0
            Issues=$issues.ToArray()
        })
    } catch {
        $issues.Add($_.Exception.Message)
        $receipts.Add([pscustomobject][ordered]@{
            Path=$fullPath; Sha256=$null; ReceiptId=$null; DubaiNight=$null
            SlewDirection=$null; TotalDurationSeconds=$null
            RuntimeManifestSha256=$null; PluginAssemblySha256=$null
            HardwareConfigurationId=$null; MechanicalStateId=$null
            LoadProfileId=$null; TemperatureC=$null; SourceNinaLogPath=$null; SourceNinaLogSha256=$null
            SourceLineNumber=$null; SourceTimingEventSha256=$null
            Valid=$false; Issues=$issues.ToArray()
        })
    }
}

$valid = @($receipts | Where-Object Valid)
$issues = [Collections.Generic.List[string]]::new()
if (@($receipts | Where-Object { -not $_.Valid }).Count -gt 0) { $issues.Add('One or more timing receipts are invalid; exclusions are forbidden') }
if ($valid.Count -lt $MinimumDeterminations) { $issues.Add("Only $($valid.Count) valid determinations; $MinimumDeterminations required") }
$ids = @($valid | ForEach-Object { $_.ReceiptId })
if (@($ids | Select-Object -Unique).Count -ne $ids.Count) { $issues.Add('ReceiptId values are not unique') }
foreach ($field in @('RuntimeManifestSha256','PluginAssemblySha256','HardwareConfigurationId','MechanicalStateId','LoadProfileId')) {
    if (@($valid | ForEach-Object { $_.$field } | Select-Object -Unique).Count -ne 1) { $issues.Add("Timing campaign mixes $field") }
}
$nights = @($valid | ForEach-Object { $_.DubaiNight } | Select-Object -Unique)
if ($nights.Count -lt $MinimumDubaiNights) { $issues.Add("Only $($nights.Count) Dubai nights; $MinimumDubaiNights required") }
foreach ($direction in @('IncreasingRA','DecreasingRA')) {
    $count = @($valid | Where-Object SlewDirection -eq $direction).Count
    if ($count -lt $MinimumPerDirection) { $issues.Add("Only $count $direction timings; $MinimumPerDirection required") }
}
$maximumObservedNightFraction = if ($valid.Count -gt 0) {
    [double](($nights | ForEach-Object { @($valid | Where-Object DubaiNight -eq $_).Count } | Measure-Object -Maximum).Maximum) / $valid.Count
} else { $null }
if ($null -ne $maximumObservedNightFraction -and $maximumObservedNightFraction -gt $MaximumNightFraction) {
    $issues.Add('One Dubai night dominates the timing campaign')
}
$durations = @($valid | ForEach-Object { $_.TotalDurationSeconds } | Sort-Object)
$observedMaximum = if ($durations.Count -gt 0) { [double]$durations[-1] } else { $null }
$conservativeUpper = if ($null -ne $observedMaximum) { $observedMaximum + $ReserveMarginSeconds } else { $null }
$coverageConfidence = if ($valid.Count -gt 0) { 1.0 - [Math]::Pow(0.95, $valid.Count) } else { 0.0 }
if ($coverageConfidence -lt 0.95) { $issues.Add('The sample maximum does not provide 95% confidence of covering the population 95th percentile') }
if ($null -ne $conservativeUpper -and $conservativeUpper -gt $MaximumCommissionedDurationSeconds) {
    $issues.Add("Observed maximum plus reserve is $($conservativeUpper.ToString('F3')) s; limit is $MaximumCommissionedDurationSeconds s")
}
$temperatureValues = @($valid | ForEach-Object { $_.TemperatureC })
$qualified = $issues.Count -eq 0
[pscustomobject][ordered]@{
    SchemaVersion=1
    Event='tppa-fresh-determination-timing-campaign'
    GeneratedUtc=[DateTimeOffset]::UtcNow.ToString('O')
    RuntimeManifestSha256=if($valid.Count -gt 0){$valid[0].RuntimeManifestSha256}else{$null}
    PluginAssemblySha256=if($valid.Count -gt 0){$valid[0].PluginAssemblySha256}else{$null}
    HardwareConfigurationId=if($valid.Count -gt 0){$valid[0].HardwareConfigurationId}else{$null}
    MechanicalStateId=if($valid.Count -gt 0){$valid[0].MechanicalStateId}else{$null}
    LoadProfileId=if($valid.Count -gt 0){$valid[0].LoadProfileId}else{$null}
    TemperatureC=[ordered]@{
        Minimum=if($temperatureValues.Count -gt 0){[double](($temperatureValues|Measure-Object -Minimum).Minimum)}else{$null}
        Maximum=if($temperatureValues.Count -gt 0){[double](($temperatureValues|Measure-Object -Maximum).Maximum)}else{$null}
    }
    ReferenceSettleSeconds=$ReferenceSettleSeconds
    MinimumDeterminations=$MinimumDeterminations
    ValidDeterminationCount=$valid.Count
    ExcludedSampleCount=@($receipts | Where-Object { -not $_.Valid }).Count
    DubaiNightCount=$nights.Count
    DubaiNights=$nights
    MaximumNightFraction=$MaximumNightFraction
    MaximumObservedNightFraction=$maximumObservedNightFraction
    MinimumPerDirection=$MinimumPerDirection
    ObservedMaximumSeconds=$observedMaximum
    ReserveMarginSeconds=$ReserveMarginSeconds
    ConservativeUpperBoundSeconds=$conservativeUpper
    P95CoverageConfidence=$coverageConfidence
    TimingCampaignQualified=$qualified
    ProductionSettleQualified=$false
    GrantsUpasMotionAuthority=$false
    ScopeNote='This qualifies only the duration reserve for the exact fresh-three-point-plus-return-field path at the unconditional 30-second cadence. It does not itself commission a shorter cadence or authorize UPAS movement.'
    Issues=$issues.ToArray()
    Receipts=$receipts.ToArray()
}