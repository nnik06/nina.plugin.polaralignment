#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ShortScoutFits,
    [Parameter(Mandatory)][string]$LongScoutFits,
    [Parameter(Mandatory)][string]$PolicyPath,
    [Parameter(Mandatory)][string]$OutputPath,
    [DateTimeOffset]$NowUtc = [DateTimeOffset]::UtcNow
)

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'
foreach ($item in @(
        @{ Path = $ShortScoutFits; Label = 'Short scout FITS' },
        @{ Path = $LongScoutFits; Label = 'Long scout FITS' },
        @{ Path = $PolicyPath; Label = 'Scout policy' })) {
    if (-not [IO.File]::Exists([IO.Path]::GetFullPath($item.Path))) {
        throw "$($item.Label) is missing: $($item.Path)"
    }
}
$outputFull = [IO.Path]::GetFullPath($OutputPath)
if ([IO.File]::Exists($outputFull)) {
    throw "Refusing to overwrite scout receipt: $outputFull"
}
[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($outputFull))
$cliProject = Join-Path $PSScriptRoot 'TppaQualificationCli\TppaQualificationCli.csproj'
& dotnet run --project $cliProject -c Release --no-build -- `
    analyze-saturation-scout `
    --short-fits ([IO.Path]::GetFullPath($ShortScoutFits)) `
    --long-fits ([IO.Path]::GetFullPath($LongScoutFits)) `
    --policy ([IO.Path]::GetFullPath($PolicyPath)) `
    --receipt-out $outputFull `
    --now-utc $NowUtc.ToUniversalTime().ToString('O')
exit $LASTEXITCODE
