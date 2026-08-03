Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$toolsRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$readinessTool = Join-Path $toolsRoot 'test_guided_900s_readiness.ps1'
$sequenceTool = Join-Path $toolsRoot 'new_actual_exposure_sequence.ps1'
$geometryTool = Join-Path $toolsRoot 'new_derived_oag_geometry_receipt.ps1'
$manifestTool = Join-Path $toolsRoot 'new_tppa_runtime_manifest.ps1'
$repo = Split-Path -Parent $toolsRoot
$train = 'EdgeHD-9.25-0.7-OAG-L-ASI2600MM-gain100-bin1'
$filter = 'OIII 3nm'
$scoutTool = Join-Path $toolsRoot 'new_actual_exposure_saturation_scout.ps1'

function Write-ScoutFits([string]$Path, [DateTimeOffset]$ObservedUtc,
        [double]$ExposureSeconds, [double]$BackgroundAdu, [string]$FilterName = $filter) {
    $width = 64; $height = 64
    function Card([string]$Key,[string]$Value) { return ($Key.PadRight(8) + '= ' + $Value).PadRight(80) }
    $cards = @(
        (Card 'SIMPLE' 'T'), (Card 'BITPIX' '-32'), (Card 'NAXIS' '2'),
        (Card 'NAXIS1' "$width"), (Card 'NAXIS2' "$height"),
        (Card 'EXPTIME' $ExposureSeconds.ToString('0.###',[Globalization.CultureInfo]::InvariantCulture)),
        (Card 'DATE-OBS' ("'" + $ObservedUtc.UtcDateTime.ToString('yyyy-MM-ddTHH:mm:ss.fffZ') + "'")),
        (Card 'INSTRUME' "'ASI2600MM Pro'"), (Card 'FILTER' ("'" + $FilterName + "'")),
        (Card 'XBINNING' '1'), (Card 'YBINNING' '1'), (Card 'GAIN' '100'),
        (Card 'OFFSET' '50'), ('END'.PadRight(80)))
    $header = [Text.Encoding]::ASCII.GetBytes(($cards -join ''))
    $headerLength = [int]([Math]::Ceiling($header.Length / 2880.0) * 2880)
    $dataLength = $width * $height * 4
    $paddedDataLength = [int]([Math]::Ceiling($dataLength / 2880.0) * 2880)
    $bytes = [byte[]]::new($headerLength + $paddedDataLength)
    [Array]::Fill[byte]($bytes, [byte][char]' ', 0, $headerLength)
    [Array]::Copy($header, 0, $bytes, 0, $header.Length)
    $pixel = [BitConverter]::GetBytes([single]$BackgroundAdu)
    if ([BitConverter]::IsLittleEndian) { [Array]::Reverse($pixel) }
    for ($index = 0; $index -lt $width * $height; $index++) {
        [Array]::Copy($pixel, 0, $bytes, $headerLength + 4 * $index, 4)
    }
    [IO.File]::WriteAllBytes($Path, $bytes)
}

function Write-Wcs([string]$Path, [double]$Ra, [double]$Dec,
        [string]$Observed = ([DateTimeOffset]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ss.fffZ'))) {
    $text = @"
NAXIS   = 2
NAXIS1  = 6248
NAXIS2  = 4176
CTYPE1  = 'RA---TAN'
CTYPE2  = 'DEC--TAN'
CRVAL1  = $($Ra.ToString('R', [Globalization.CultureInfo]::InvariantCulture))
CRVAL2  = $($Dec.ToString('R', [Globalization.CultureInfo]::InvariantCulture))
CRPIX1  = 3124.5
CRPIX2  = 2088.5
CD1_1   = -0.000130972222222222
CD1_2   = 0
CD2_1   = 0
CD2_2   = 0.000130972222222222
DATE-OBS= '$Observed'
"@
    [IO.File]::WriteAllText($Path, $text, [Text.UTF8Encoding]::new($false))
}

function New-Fixture([string]$Root) {
    [void][IO.Directory]::CreateDirectory($Root)
    $plugin = Join-Path $Root 'plugin'
    [void][IO.Directory]::CreateDirectory($plugin)
    [IO.File]::Copy(
        (Join-Path $repo 'PolarAlignment\bin\Release\net8.0-windows7.0\NINA.Plugins.PolarAlignment.dll'),
        (Join-Path $plugin 'NINA.Plugins.PolarAlignment.dll'))
    [IO.File]::Copy(
        (Join-Path $repo 'QualificationCore\bin\Release\net8.0\NINA.Plugins.PolarAlignment.QualificationCore.dll'),
        (Join-Path $plugin 'NINA.Plugins.PolarAlignment.QualificationCore.dll'))
    $runtime = & $manifestTool -PluginDirectory $plugin `
        -SourceCommit ('a' * 40) -PluginVersion '2.2.6.93'

    $sequence = Join-Path $Root 'sequence.json'
    & $sequenceTool -OutputPath $sequence -OpticalTrainId $train `
        -RequiredFilterName $filter | Out-Null

    $policy = Join-Path $Root 'edgehd-calibrated.json'
    $policyObject = [IO.File]::ReadAllText(
        (Join-Path $toolsRoot 'actual-exposure-policies\edgehd925-07-asi2600mm-gain100.template.json')) |
        ConvertFrom-Json
    $policyObject.RequiredFilterName = $filter
    $policyObject.AllowedFilterNames = @($filter)
    [IO.File]::WriteAllText($policy,
        ($policyObject | ConvertTo-Json -Depth 8) + "`r`n", [Text.UTF8Encoding]::new($false))
    $shortScout = Join-Path $Root 'scout-10s.fits'
    $longScout = Join-Path $Root 'scout-60s.fits'
    Write-ScoutFits $shortScout ([DateTimeOffset]::UtcNow.AddMinutes(-2)) 10 600
    Write-ScoutFits $longScout ([DateTimeOffset]::UtcNow.AddMinutes(-1)) 60 1100
    $scoutReceipt = Join-Path $Root 'scout-receipt.json'
    & $scoutTool -ShortScoutFits $shortScout -LongScoutFits $longScout `
        -PolicyPath $policy -OutputPath $scoutReceipt -NowUtc ([DateTimeOffset]::UtcNow) | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Synthetic scout fixture did not pass.' }

    $main = Join-Path $Root 'main.wcs'
    $guide = Join-Path $Root 'guide.wcs'
    Write-Wcs $main 120.0 30.0
    Write-Wcs $guide 120.0002 30.0001
    $geometry = Join-Path $Root 'geometry.json'
    & $geometryTool -MainWcsPath $main -GuideWcsPath $guide -OutputPath $geometry |
        Out-Null

    $report = Join-Path $Root 'operational.json'
    $runs = 1..3 | ForEach-Object {
        [ordered]@{ Passed = $true; ReportedTotalsArcmin = @(1.1, 1.2, 1.15) }
    }
    $reportObject = [ordered]@{
        SchemaVersion = 1
        GeneratedUtc = [DateTimeOffset]::UtcNow.ToString('O')
        RequiredConsecutiveRuns = 3
        MaximumReportedTotalMinutes = 3.0
        SpeedAndInternalConsistencyQualified = $true
        PassingConsecutivePrefix = 3
        Runs = $runs
    }
    [IO.File]::WriteAllText($report,
        ($reportObject | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))

    return [ordered]@{
        OpticalTrainId = $train
        PluginDirectory = $plugin
        RuntimeManifestSha256 = $runtime.RuntimeManifestSha256
        SequencePath = $sequence
        SequenceSha256 = (Get-FileHash $sequence -Algorithm SHA256).Hash
        PolicyPath = $policy
        PolicySha256 = (Get-FileHash $policy -Algorithm SHA256).Hash
        RequiredFilterName = $filter
        ShortScoutFitsPath = $shortScout
        ShortScoutFitsSha256 = (Get-FileHash $shortScout -Algorithm SHA256).Hash
        LongScoutFitsPath = $longScout
        LongScoutFitsSha256 = (Get-FileHash $longScout -Algorithm SHA256).Hash
        SaturationScoutReceiptPath = $scoutReceipt
        SaturationScoutReceiptSha256 = (Get-FileHash $scoutReceipt -Algorithm SHA256).Hash
        MeasuredPixelScaleArcsecondsPerPixel = 0.4715
        OagGeometryReceiptPath = $geometry
        OagGeometryReceiptSha256 = (Get-FileHash $geometry -Algorithm SHA256).Hash
        OperationalQualificationReportPath = $report
        OperationalQualificationReportSha256 = (Get-FileHash $report -Algorithm SHA256).Hash
        OutputPath = (Join-Path $Root 'ready.json')
    }
}

function Test-Rejected([Collections.IDictionary]$Arguments) {
    $rejected = $false
    try { & $readinessTool @Arguments | Out-Null } catch { $rejected = $true }
    return $rejected
}

Describe 'guided 900-second readiness gate' {
    It 'accepts one complete, fresh, hash-bound field fixture' {
        $arguments = New-Fixture (Join-Path $TestDrive 'pass')
        $result = & $readinessTool @arguments
        $result.Ready | Should Be $true
        [IO.File]::Exists($arguments.OutputPath) | Should Be $true
        $receipt = [IO.File]::ReadAllText($arguments.OutputPath) | ConvertFrom-Json
        $receipt.ReadyForGuided900SecondAcquisition | Should Be $true
        $receipt.GrantsSequenceStartAuthority | Should Be $false
        $receipt.GrantsAbsolutePolarAccuracyClaim | Should Be $false
    }

    It 'rejects a calibrated policy that omits an explicit scout field' {
        $arguments = New-Fixture (Join-Path $TestDrive 'missing-scout-field')
        $policyObject = [IO.File]::ReadAllText($arguments.PolicyPath) | ConvertFrom-Json
        $policyObject.PSObject.Properties.Remove('MaximumProjectedBackgroundAdu')
        [IO.File]::WriteAllText($arguments.PolicyPath,
            ($policyObject | ConvertTo-Json -Depth 8) + "`r`n", [Text.UTF8Encoding]::new($false))
        $arguments['PolicySha256'] = (Get-FileHash $arguments.PolicyPath -Algorithm SHA256).Hash
        (Test-Rejected $arguments) | Should Be $true
    }
    It 'rejects a required filter that differs from sequence and policy' {
        $arguments = New-Fixture (Join-Path $TestDrive 'wrong-filter')
        $arguments['RequiredFilterName'] = 'Ha 3nm'
        (Test-Rejected $arguments) | Should Be $true
    }

    It 'rejects a scout FITS changed after its declared hash' {
        $arguments = New-Fixture (Join-Path $TestDrive 'scout-tamper')
        [IO.File]::AppendAllText($arguments.ShortScoutFitsPath, 'x')
        (Test-Rejected $arguments) | Should Be $true
    }

    It 'rejects a rehashed but non-reproducible scout receipt' {
        $arguments = New-Fixture (Join-Path $TestDrive 'scout-receipt-tamper')
        $receipt = [IO.File]::ReadAllText($arguments.SaturationScoutReceiptPath) | ConvertFrom-Json
        $receipt.MedianProjectedBackgroundAdu += 1
        [IO.File]::WriteAllText($arguments.SaturationScoutReceiptPath,
            ($receipt | ConvertTo-Json -Depth 8) + "`r`n", [Text.UTF8Encoding]::new($false))
        $arguments['SaturationScoutReceiptSha256'] =
            (Get-FileHash $arguments.SaturationScoutReceiptPath -Algorithm SHA256).Hash
        (Test-Rejected $arguments) | Should Be $true
    }

    It 'rejects a nominal policy template by filename' {
        $arguments = New-Fixture (Join-Path $TestDrive 'template')
        $template = Join-Path (Split-Path -Parent $arguments.PolicyPath) 'policy.template.json'
        [IO.File]::Move($arguments.PolicyPath, $template)
        $arguments['PolicyPath'] = $template
        $arguments['PolicySha256'] = (Get-FileHash $template -Algorithm SHA256).Hash
        (Test-Rejected $arguments) | Should Be $true
        [IO.File]::Exists($arguments.OutputPath) | Should Be $false
    }

    It 'rejects a sequence changed after its declared hash' {
        $arguments = New-Fixture (Join-Path $TestDrive 'sequence-tamper')
        [IO.File]::AppendAllText($arguments.SequencePath, ' ')
        (Test-Rejected $arguments) | Should Be $true
        [IO.File]::Exists($arguments.OutputPath) | Should Be $false
    }

    It 'rejects a re-hashed sequence with an extra allowed exposure node' {
        $arguments = New-Fixture (Join-Path $TestDrive 'extra-exposure')
        $sequence = [IO.File]::ReadAllText($arguments.SequencePath) | ConvertFrom-Json
        $bracket = $sequence.Items.'$values'[1].Items.'$values'[0]
        $bracket.Items.'$values' += $bracket.Items.'$values'[2]
        [IO.File]::WriteAllText($arguments.SequencePath,
            ($sequence | ConvertTo-Json -Depth 32), [Text.UTF8Encoding]::new($false))
        $arguments['SequenceSha256'] =
            (Get-FileHash $arguments.SequencePath -Algorithm SHA256).Hash
        (Test-Rejected $arguments) | Should Be $true
        [IO.File]::Exists($arguments.OutputPath) | Should Be $false
    }

    It 'rejects a geometry receipt that no longer reproduces its sources' {
        $arguments = New-Fixture (Join-Path $TestDrive 'geometry-tamper')
        $geometry = [IO.File]::ReadAllText($arguments.OagGeometryReceiptPath) |
            ConvertFrom-Json
        $geometry.GuideToFarthestMainCornerUpperBoundPixels += 10.0
        [IO.File]::WriteAllText($arguments.OagGeometryReceiptPath,
            ($geometry | ConvertTo-Json -Depth 6) + "`r`n",
            [Text.UTF8Encoding]::new($false))
        $arguments['OagGeometryReceiptSha256'] =
            (Get-FileHash $arguments.OagGeometryReceiptPath -Algorithm SHA256).Hash
        (Test-Rejected $arguments) | Should Be $true
        [IO.File]::Exists($arguments.OutputPath) | Should Be $false
    }

    It 'rejects a stale TPPA operational report' {
        $arguments = New-Fixture (Join-Path $TestDrive 'stale')
        $report = [IO.File]::ReadAllText($arguments.OperationalQualificationReportPath) |
            ConvertFrom-Json
        $report.GeneratedUtc = [DateTimeOffset]::UtcNow.AddHours(-2).ToString('O')
        [IO.File]::WriteAllText($arguments.OperationalQualificationReportPath,
            ($report | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
        $arguments['OperationalQualificationReportSha256'] =
            (Get-FileHash $arguments.OperationalQualificationReportPath -Algorithm SHA256).Hash
        (Test-Rejected $arguments) | Should Be $true
        [IO.File]::Exists($arguments.OutputPath) | Should Be $false
    }

    It 'refuses to overwrite a prior readiness receipt' {
        $arguments = New-Fixture (Join-Path $TestDrive 'overwrite')
        [IO.File]::WriteAllText($arguments.OutputPath, 'preserve')
        (Test-Rejected $arguments) | Should Be $true
        [IO.File]::ReadAllText($arguments.OutputPath) | Should Be 'preserve'
    }
}
