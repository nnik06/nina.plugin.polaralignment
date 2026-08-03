#requires -Version 7.5
param(
    [Parameter(Mandatory = $true)]
    [string]$NinaLogPath,
    [Parameter(Mandatory = $true)]
    [string]$RuntimeManifestPath,
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9a-f]{64}$')]
    [string]$HardwareConfigurationId,
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9a-f]{64}$')]
    [string]$MechanicalStateId,
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$LoadProfileId,
    [Parameter(Mandatory = $true)]
    [ValidateRange(-20.0, 70.0)]
    [double]$TemperatureC,
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,
    [ValidateRange(1, 500)]
    [int]$ExpectedReceiptCount = 1
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
$marker = 'TPPA_COMPLETION_VERIFICATION_TIMING '
if ($HardwareConfigurationId -cne $HardwareConfigurationId.ToLowerInvariant() -or
        $MechanicalStateId -cne $MechanicalStateId.ToLowerInvariant()) {
    throw 'Hardware and mechanical identities must be canonical lowercase hexadecimal.'
}
$log = [IO.Path]::GetFullPath($NinaLogPath)
$manifest = [IO.Path]::GetFullPath($RuntimeManifestPath)
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (-not [IO.File]::Exists($log)) { throw "NINA log is missing: $log" }
if (-not [IO.File]::Exists($manifest)) { throw "Runtime manifest is missing: $manifest" }

function Get-LowerSha256([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}
function Get-DubaiNight([DateTimeOffset]$Timestamp) {
    return $Timestamp.ToOffset([TimeSpan]::FromHours(4)).AddHours(-12).Date.ToString('yyyy-MM-dd')
}
function Parse-Fields([string]$Text, [int]$LineNumber) {
    $result = [ordered]@{}
    foreach ($fragment in $Text.Split(';', [StringSplitOptions]::RemoveEmptyEntries)) {
        $part = $fragment.Trim()
        $separator = $part.IndexOf('=')
        if ($separator -le 0 -or $separator -eq $part.Length - 1) {
            throw "Timing event at line $LineNumber has malformed field '$part'."
        }
        $name = $part.Substring(0, $separator)
        if ($result.Contains($name)) { throw "Timing event at line $LineNumber repeats '$name'." }
        $result[$name] = $part.Substring($separator + 1)
    }
    return $result
}
function Require-Field([Collections.IDictionary]$Fields, [string]$Name, [int]$LineNumber) {
    if (-not $Fields.Contains($Name)) { throw "Timing event at line $LineNumber is missing '$Name'." }
    return [string]$Fields[$Name]
}
function Parse-Boolean([string]$Value, [string]$Name, [int]$LineNumber) {
    if ($Value -notin @('true','false')) { throw "Timing event at line $LineNumber has invalid boolean '$Name'." }
    return $Value -eq 'true'
}
function Parse-Finite([string]$Value, [string]$Name, [int]$LineNumber) {
    [double]$number = 0
    if (-not [double]::TryParse($Value, [Globalization.NumberStyles]::Float,
            [Globalization.CultureInfo]::InvariantCulture, [ref]$number) -or
            -not [double]::IsFinite($number)) {
        throw "Timing event at line $LineNumber has invalid number '$Name'."
    }
    return $number
}

$manifestObject = [IO.File]::ReadAllText($manifest) | ConvertFrom-Json -DateKind String
if ([int]$manifestObject.schemaVersion -ne 1 -or
        [string]$manifestObject.packageId -ne 'NINA.Plugins.PolarAlignment') {
    throw 'Runtime manifest schema/package is invalid.'
}
$pluginArtifact = @($manifestObject.artifacts | Where-Object name -eq 'NINA.Plugins.PolarAlignment.dll')
if ($pluginArtifact.Count -ne 1 -or [string]$pluginArtifact[0].sha256 -notmatch '^[0-9a-f]{64}$') {
    throw 'Runtime manifest has no unique canonical TPPA plugin artifact.'
}
$pluginPath = Join-Path ([IO.Path]::GetDirectoryName($manifest)) 'NINA.Plugins.PolarAlignment.dll'
if (-not [IO.File]::Exists($pluginPath)) { throw "Installed TPPA plugin is missing beside the runtime manifest: $pluginPath" }
$pluginSha = Get-LowerSha256 $pluginPath
if ($pluginSha -ne [string]$pluginArtifact[0].sha256) {
    throw 'Installed TPPA plugin hash does not match the runtime manifest.'
}
$runtimeSha = Get-LowerSha256 $manifest
$logSha = Get-LowerSha256 $log
$lines = [IO.File]::ReadAllLines($log)
$events = [Collections.Generic.List[object]]::new()
for ($index = 0; $index -lt $lines.Length; $index++) {
    $line = $lines[$index]
    $offset = $line.IndexOf($marker, [StringComparison]::Ordinal)
    if ($offset -lt 0) { continue }
    $lineNumber = $index + 1
    $fields = Parse-Fields $line.Substring($offset + $marker.Length) $lineNumber
    if ((Require-Field $fields 'schemaVersion' $lineNumber) -ne '2' -or
            (Require-Field $fields 'phase' $lineNumber) -ne 'return-field') {
        throw "Timing event at line $lineNumber is not the schema-2 return-field event."
    }
    $receiptIdText = Require-Field $fields 'receiptId' $lineNumber
    [Guid]$receiptId = [Guid]::Empty
    if (-not [Guid]::TryParseExact($receiptIdText, 'D', [ref]$receiptId) -or
            $receiptId -eq [Guid]::Empty) {
        throw "Timing event at line $lineNumber has invalid receiptId."
    }
    $started = [DateTimeOffset]::Parse(
        (Require-Field $fields 'startedUtc' $lineNumber),
        [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::RoundtripKind)
    $completed = [DateTimeOffset]::Parse(
        (Require-Field $fields 'completedUtc' $lineNumber),
        [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::RoundtripKind)
    $direction = Require-Field $fields 'slewDirection' $lineNumber
    if ($direction -notin @('IncreasingRA','DecreasingRA')) {
        throw "Timing event at line $lineNumber has invalid slewDirection."
    }
    $settle = Parse-Finite (Require-Field $fields 'effectiveSettleSeconds' $lineNumber) 'effectiveSettleSeconds' $lineNumber
    $totalMilliseconds = Parse-Finite (Require-Field $fields 'totalMilliseconds' $lineNumber) 'totalMilliseconds' $lineNumber
    if ($completed -le $started -or $totalMilliseconds -le 0 -or
            [Math]::Abs(($completed - $started).TotalMilliseconds - $totalMilliseconds) -gt 250.0) {
        throw "Timing event at line $lineNumber has contradictory lifecycle/duration."
    }
    $movementText = Require-Field $fields 'upasMovementCount' $lineNumber
    [int]$movementCount = -1
    if (-not [int]::TryParse(
            $movementText,
            [Globalization.NumberStyles]::Integer,
            [Globalization.CultureInfo]::InvariantCulture,
            [ref]$movementCount)) {
        throw "Timing event at line $lineNumber has invalid integer 'upasMovementCount'."
    }
    if ((Require-Field $fields 'timingPath' $lineNumber) -ne 'fresh-three-point-plus-return-field' -or
            -not (Parse-Boolean (Require-Field $fields 'measurementOnly' $lineNumber) 'measurementOnly' $lineNumber) -or
            -not (Parse-Boolean (Require-Field $fields 'refractionAdjustmentEnabled' $lineNumber) 'refractionAdjustmentEnabled' $lineNumber) -or
            (Parse-Boolean (Require-Field $fields 'cadenceAuthorityConsumed' $lineNumber) 'cadenceAuthorityConsumed' $lineNumber) -or
            $movementCount -ne 0) {
        throw "Timing event at line $lineNumber is not an eligible no-motion fallback-cadence observation."
    }
    $events.Add([pscustomobject]@{
        ReceiptId=$receiptId.ToString('D')
        StartedUtc=$started
        CompletedUtc=$completed
        DubaiNight=Get-DubaiNight $started
        SlewDirection=$direction
        EffectiveSettleSeconds=$settle
        TotalDurationSeconds=$totalMilliseconds / 1000.0
        SourceLineNumber=$lineNumber
        SourceTimingEventSha256=([Convert]::ToHexString(
            [Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($line)))).ToLowerInvariant()
    })
}
if ($events.Count -ne $ExpectedReceiptCount) {
    throw "NINA log contains $($events.Count) eligible timing events; expected exactly $ExpectedReceiptCount."
}
if (@($events.ReceiptId | Select-Object -Unique).Count -ne $events.Count) {
    throw 'NINA log repeats a timing receipt ID.'
}
[IO.Directory]::CreateDirectory($output) | Out-Null
$created = [Collections.Generic.List[string]]::new()
foreach ($event in $events) {
    $path = Join-Path $output ("tppa-fresh-determination-timing-{0}.json" -f $event.ReceiptId)
    if ([IO.File]::Exists($path)) { throw "Refusing to overwrite timing receipt: $path" }
    $receipt = [ordered]@{
        SchemaVersion=1
        Event='tppa-fresh-determination-timing'
        ReceiptId=$event.ReceiptId
        StartedUtc=$event.StartedUtc.ToString('O')
        CompletedUtc=$event.CompletedUtc.ToString('O')
        DubaiNight=$event.DubaiNight
        SlewDirection=$event.SlewDirection
        EffectiveSettleSeconds=$event.EffectiveSettleSeconds
        TotalDurationSeconds=$event.TotalDurationSeconds
        TimingPath='fresh-three-point-plus-return-field'
        MeasurementOnly=$true
        RefractionAdjustmentEnabled=$true
        CompletedSuccessfully=$true
        CadenceAuthorityConsumed=$false
        UpasMovementCount=0
        RuntimeManifestSha256=$runtimeSha
        PluginAssemblySha256=$pluginSha
        HardwareConfigurationId=$HardwareConfigurationId
        MechanicalStateId=$MechanicalStateId
        LoadProfileId=$LoadProfileId
        TemperatureC=$TemperatureC
        SourceNinaLogPath=$log
        SourceNinaLogSha256=$logSha
        SourceLineNumber=$event.SourceLineNumber
        SourceTimingEventSha256=$event.SourceTimingEventSha256
    }
    [IO.File]::WriteAllText(
        $path,
        ($receipt | ConvertTo-Json -Depth 5),
        [Text.UTF8Encoding]::new($false))
    $created.Add($path)
}
[pscustomobject][ordered]@{
    SchemaVersion=1
    Event='tppa-fresh-determination-timing-receipt-production'
    NinaLogPath=$log
    NinaLogSha256=$logSha
    RuntimeManifestPath=$manifest
    RuntimeManifestSha256=$runtimeSha
    PluginAssemblySha256=$pluginSha
    ReceiptCount=$created.Count
    ReceiptPaths=$created.ToArray()
}
