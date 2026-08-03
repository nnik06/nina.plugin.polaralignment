#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet(
        'WO-GT81-IV-0.8-OAG-L-ASI2600MM-gain100-bin1',
        'EdgeHD-9.25-0.7-OAG-L-ASI2600MM-gain100-bin1')]
    [string]$OpticalTrainId,
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()]
    [string]$PhysicalConfigurationDescription,
    [Parameter(Mandatory)][string]$OutputPath,
    [DateTimeOffset]$EstablishedUtc = [DateTimeOffset]::UtcNow
)

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($PhysicalConfigurationDescription)) {
    throw 'PhysicalConfigurationDescription cannot be blank.'
}
$output = [IO.Path]::GetFullPath($OutputPath)
if ([IO.File]::Exists($output)) {
    throw "Refusing to overwrite mechanical epoch receipt: $output"
}
$parent = [IO.Path]::GetDirectoryName($output)
if (-not [string]::IsNullOrWhiteSpace($parent)) {
    [void][IO.Directory]::CreateDirectory($parent)
}
$receipt = [ordered]@{
    schemaVersion = 2
    epochId = [Guid]::NewGuid().ToString('D')
    establishedUtc = $EstablishedUtc.ToUniversalTime().ToString('O')
    opticalTrainId = $OpticalTrainId
    physicalConfigurationDescription = $PhysicalConfigurationDescription
    epochScope = 'rigid assembly, optical train, load, balance, cable routing, and mechanical seating'
    notInvalidatedBy = @(
        'rigid-body relocation with the assembly unchanged',
        'intended UPAS azimuth or altitude travel within qualified limits',
        'manual polar-axis orientation change without assembly, seating, load, balance, cable, or fastener change'
    )
    invalidatedBy = @(
        'tripod or pier leg, foot, spreader, clamp, adapter, or plate reseating or adjustment',
        'UPAS removal, reset, or mechanical reseating',
        'mount, saddle, OTA, reducer, OAG, camera, rotator, or guider removal or reseating',
        'counterweight, balance, cable-routing, or load-profile change',
        'physical impact, fastener adjustment, or unexplained scale-position discontinuity'
    )
}
$bytes = [Text.UTF8Encoding]::new($false).GetBytes(
    ($receipt | ConvertTo-Json -Depth 4) + [Environment]::NewLine)
$stream = [IO.File]::Open(
    $output, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
try {
    $stream.Write($bytes, 0, $bytes.Length)
    $stream.Flush($true)
} finally {
    $stream.Dispose()
}
$stateId = (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash.ToLowerInvariant()
[pscustomobject]@{
    ReceiptPath = $output
    MechanicalStateId = $stateId
    SetForNextNinaLaunch = "[Environment]::SetEnvironmentVariable('TPPA_MECHANICAL_STATE_ID','$stateId','User')"
    InvalidationRule = 'Create a new receipt and restart NINA after any listed physical change.'
}
