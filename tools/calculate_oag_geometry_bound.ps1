param(
    [Parameter(Mandatory = $true)]
    [ValidateRange(0.0, 360.0)]
    [double]$MainCenterRightAscensionDegrees,
    [Parameter(Mandatory = $true)]
    [ValidateRange(-90.0, 90.0)]
    [double]$MainCenterDeclinationDegrees,
    [Parameter(Mandatory = $true)]
    [ValidateRange(0.0, 360.0)]
    [double]$GuideCenterRightAscensionDegrees,
    [Parameter(Mandatory = $true)]
    [ValidateRange(-90.0, 90.0)]
    [double]$GuideCenterDeclinationDegrees,
    [Parameter(Mandatory = $true)]
    [ValidateRange(0.01, 10.0)]
    [double]$MainPixelScaleArcseconds,
    [Parameter(Mandatory = $true)]
    [ValidateRange(0.01, 20.0)]
    [double]$GuidePixelScaleArcseconds,
    [ValidateRange(1, 20000)]
    [int]$MainWidthPixels = 6248,
    [ValidateRange(1, 20000)]
    [int]$MainHeightPixels = 4176,
    [ValidateRange(1, 20000)]
    [int]$GuideWidthPixels = 1920,
    [ValidateRange(1, 20000)]
    [int]$GuideHeightPixels = 1080,
    [double]$GuideLockOffsetXFromCenterPixels = 0.0,
    [double]$GuideLockOffsetYFromCenterPixels = 0.0,
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$MainSolutionSource,
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$GuideSolutionSource,
    [string]$MainSolutionSha256 = '',
    [string]$GuideSolutionSha256 = '',
    [ValidateSet('Object', 'Json')]
    [string]$OutputFormat = 'Object',
    [string]$OutputPath = ''
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$hasLockX = $PSBoundParameters.ContainsKey(
    'GuideLockOffsetXFromCenterPixels')
$hasLockY = $PSBoundParameters.ContainsKey(
    'GuideLockOffsetYFromCenterPixels')
if ($hasLockX -ne $hasLockY) {
    throw 'Guide lock X and Y offsets must be supplied together.'
}
foreach ($hash in @($MainSolutionSha256, $GuideSolutionSha256)) {
    if ($hash -and $hash -notmatch '^[0-9a-fA-F]{64}$') {
        throw 'Optional source SHA-256 values must contain exactly 64 hexadecimal characters.'
    }
}

function Get-Sha256([string]$Path) {
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-SealedSource([string]$Source, [string]$DeclaredHash,
        [string]$Label, [string]$ReceiptDirectory) {
    if (-not (Test-Path -LiteralPath $Source -PathType Leaf)) {
        throw "$Label solution source is missing: $Source"
    }
    $path = (Resolve-Path -LiteralPath $Source).Path
    $relative = [IO.Path]::GetRelativePath($ReceiptDirectory, $path)
    if ([IO.Path]::IsPathRooted($relative) -or $relative -eq '..' -or
            $relative.StartsWith('..' + [IO.Path]::DirectorySeparatorChar,
                [StringComparison]::Ordinal)) {
        throw "$Label solution source must be inside the diagnostic receipt bundle."
    }
    $actual = Get-Sha256 $path
    if ($DeclaredHash -and $actual -ne $DeclaredHash.ToLowerInvariant()) {
        throw "$Label solution source SHA-256 does not match the supplied bytes."
    }
    [pscustomobject]@{ Path = $path; RelativePath = $relative; Sha256 = $actual }
}

function Write-CreateNewUtf8([string]$Path, [string]$Content) {
    $fullPath = [IO.Path]::GetFullPath($Path)
    $parent = [IO.Path]::GetDirectoryName($fullPath)
    if (-not [string]::IsNullOrWhiteSpace($parent) -and
            -not [IO.Directory]::Exists($parent)) {
        throw "Output parent directory is missing: $parent"
    }
    if ([IO.File]::Exists($fullPath)) {
        throw "Output already exists: $fullPath"
    }
    $temporary = Join-Path $parent (
        ([IO.Path]::GetFileName($fullPath)) + '.new-' + [Guid]::NewGuid().ToString('N'))
    try {
        $stream = [IO.File]::Open($temporary, [IO.FileMode]::CreateNew,
            [IO.FileAccess]::Write, [IO.FileShare]::Read)
        try {
            $writer = [IO.StreamWriter]::new($stream,
                [Text.UTF8Encoding]::new($false))
            try { $writer.Write($Content) } finally { $writer.Dispose() }
        } finally { $stream.Dispose() }
        [IO.File]::Move($temporary, $fullPath)
    } finally {
        if ([IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) }
    }
}

if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
    $outputFull = [IO.Path]::GetFullPath($OutputPath)
    $receiptDirectory = [IO.Path]::GetDirectoryName($outputFull)
    if ([string]::IsNullOrWhiteSpace($receiptDirectory) -or
            -not [IO.Directory]::Exists($receiptDirectory)) {
        throw "Output parent directory is missing: $receiptDirectory"
    }
    $mainSealed = Get-SealedSource $MainSolutionSource $MainSolutionSha256 'Main' $receiptDirectory
    $guideSealed = Get-SealedSource $GuideSolutionSource $GuideSolutionSha256 'Guide' $receiptDirectory
    $MainSolutionSource = $mainSealed.RelativePath
    $MainSolutionSha256 = $mainSealed.Sha256
    $GuideSolutionSource = $guideSealed.RelativePath
    $GuideSolutionSha256 = $guideSealed.Sha256
}

function Convert-DegreesToRadians([double]$Value) {
    $Value * [Math]::PI / 180.0
}

function Get-GreatCircleSeparationRadians(
        [double]$RightAscension1Degrees,
        [double]$Declination1Degrees,
        [double]$RightAscension2Degrees,
        [double]$Declination2Degrees) {
    $ra1 = Convert-DegreesToRadians $RightAscension1Degrees
    $ra2 = Convert-DegreesToRadians $RightAscension2Degrees
    $dec1 = Convert-DegreesToRadians $Declination1Degrees
    $dec2 = Convert-DegreesToRadians $Declination2Degrees
    $cosine =
        [Math]::Sin($dec1) * [Math]::Sin($dec2) +
        [Math]::Cos($dec1) * [Math]::Cos($dec2) * [Math]::Cos($ra2 - $ra1)
    [Math]::Acos([Math]::Max(-1.0, [Math]::Min(1.0, $cosine)))
}

$centerSeparationRadians = Get-GreatCircleSeparationRadians `
    $MainCenterRightAscensionDegrees `
    $MainCenterDeclinationDegrees `
    $GuideCenterRightAscensionDegrees `
    $GuideCenterDeclinationDegrees
$centerSeparationArcseconds =
    $centerSeparationRadians * 180.0 / [Math]::PI * 3600.0
$mainHalfDiagonalPixels =
    0.5 * [Math]::Sqrt(
        $MainWidthPixels * $MainWidthPixels +
        $MainHeightPixels * $MainHeightPixels)

if ($hasLockX) {
    $guideRadialPixels = [Math]::Sqrt(
        $GuideLockOffsetXFromCenterPixels * $GuideLockOffsetXFromCenterPixels +
        $GuideLockOffsetYFromCenterPixels * $GuideLockOffsetYFromCenterPixels)
    $guideEvidenceKind = 'measured-lock-offset-upper-bound'
} else {
    $guideRadialPixels =
        0.5 * [Math]::Sqrt(
            $GuideWidthPixels * $GuideWidthPixels +
            $GuideHeightPixels * $GuideHeightPixels)
    $guideEvidenceKind = 'full-guide-sensor-upper-bound'
}
$guideRadialArcseconds = $guideRadialPixels * $GuidePixelScaleArcseconds
$centerSeparationMainPixels =
    $centerSeparationArcseconds / $MainPixelScaleArcseconds
$guideRadialMainPixels =
    $guideRadialArcseconds / $MainPixelScaleArcseconds
$farthestCornerBoundPixels =
    $centerSeparationMainPixels +
    $guideRadialMainPixels +
    $mainHalfDiagonalPixels

$result = [pscustomobject][ordered]@{
    SchemaVersion = 1
    Model = 'orientation-independent-spherical-triangle-upper-bound'
    FormulaVersion = 'orientation-independent-spherical-triangle-upper-bound/v1-diagnostic'
    GeometryProvenance = 'operator-attested-diagnostic'
    MainSolutionSource = $MainSolutionSource
    MainSolutionSha256 = $MainSolutionSha256.ToLowerInvariant()
    GuideSolutionSource = $GuideSolutionSource
    GuideSolutionSha256 = $GuideSolutionSha256.ToLowerInvariant()
    CenterSeparationArcseconds = $centerSeparationArcseconds
    CenterSeparationMainPixels = $centerSeparationMainPixels
    MainHalfDiagonalPixels = $mainHalfDiagonalPixels
    GuideRadialEvidenceKind = $guideEvidenceKind
    GuideRadialPixels = $guideRadialPixels
    GuideRadialArcseconds = $guideRadialArcseconds
    GuideRadialMainPixels = $guideRadialMainPixels
    GuideToFarthestMainCornerUpperBoundPixels = $farthestCornerBoundPixels
    UsesOrientationConvention = $false
    GrantsMotionAuthority = $false
    GrantsAbsolutePolarAccuracyClaim = $false
}

if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
    $json = ($result | ConvertTo-Json -Depth 4) + "`r`n"
    Write-CreateNewUtf8 $OutputPath $json
    $result
} elseif ($OutputFormat -eq 'Json') {
    $result | ConvertTo-Json -Depth 4
} else {
    $result
}
