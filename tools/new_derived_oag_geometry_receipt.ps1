param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$MainWcsPath,
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$GuideWcsPath,
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$OutputPath,
    [string]$MainSolutionSha256 = '',
    [string]$GuideSolutionSha256 = '',
    [double]$GuideLockOffsetXFromCenterPixels = 0.0,
    [double]$GuideLockOffsetYFromCenterPixels = 0.0
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
$invariant = [Globalization.CultureInfo]::InvariantCulture
$hasLockX = $PSBoundParameters.ContainsKey('GuideLockOffsetXFromCenterPixels')
$hasLockY = $PSBoundParameters.ContainsKey('GuideLockOffsetYFromCenterPixels')
if ($hasLockX -ne $hasLockY) {
    throw 'Guide lock X and Y offsets must be supplied together.'
}
foreach ($hash in @($MainSolutionSha256, $GuideSolutionSha256)) {
    if ($hash -and $hash -notmatch '^[0-9a-fA-F]{64}$') {
        throw 'Optional source SHA-256 values must contain exactly 64 hexadecimal characters.'
    }
}

function Get-RequiredString([Collections.IDictionary]$Header, [string]$Key) {
    if (-not $Header.Contains($Key) -or
            [string]::IsNullOrWhiteSpace([string]$Header[$Key])) {
        throw "ASTAP WCS is missing $Key."
    }
    [string]$Header[$Key]
}

function Get-RequiredDouble([Collections.IDictionary]$Header, [string]$Key) {
    $value = Get-RequiredString $Header $Key
    $parsed = 0.0
    if (-not [double]::TryParse($value,
            [Globalization.NumberStyles]::Float, $invariant, [ref]$parsed) -or
            [double]::IsNaN($parsed) -or [double]::IsInfinity($parsed)) {
        throw "ASTAP WCS $Key is not finite."
    }
    $parsed
}

function Get-RequiredInt([Collections.IDictionary]$Header, [string]$Key) {
    $value = Get-RequiredString $Header $Key
    $parsed = 0
    if (-not [int]::TryParse($value,
            [Globalization.NumberStyles]::Integer, $invariant, [ref]$parsed)) {
        throw "ASTAP WCS $Key is not an integer."
    }
    $parsed
}

function Get-StrictAstapWcs([byte[]]$Bytes, [string]$Label) {
    if ($null -eq $Bytes -or $Bytes.Length -eq 0) {
        throw "$Label ASTAP WCS source is empty."
    }
    $header = [Collections.Specialized.OrderedDictionary]::new(
        [StringComparer]::OrdinalIgnoreCase)
    $strictUtf8 = [Text.UTF8Encoding]::new($false, $true)
    foreach ($line in $strictUtf8.GetString($Bytes) -split '\r?\n') {
        if ([string]::IsNullOrWhiteSpace($line) -or $line.Length -lt 3) {
            continue
        }
        $equals = $line.IndexOf('=')
        if ($equals -le 0) { continue }
        $key = $line.Substring(0, [Math]::Min(8, $equals)).Trim()
        if ([string]::IsNullOrWhiteSpace($key)) { continue }
        if ($header.Contains($key)) {
            throw "ASTAP WCS contains duplicate $key."
        }
        $value = $line.Substring($equals + 1).Split('/', 2)[0].Trim()
        if ($value.Length -ge 2 -and $value[0] -eq "'" -and
                $value[$value.Length - 1] -eq "'") {
            $value = $value.Substring(1, $value.Length - 2).Replace("''", "'").Trim()
        }
        $header.Add($key, $value)
    }
    foreach ($key in $header.Keys) {
        if ($key -match '^(A|B|AP|BP)(?:_ORDER|_[0-9]+_[0-9]+)$' -or
                $key -match '^(PV1_|PV2_|PC1_|PC2_)') {
            throw "ASTAP WCS distortion or PC-matrix keyword $key is unsupported."
        }
    }
    if ((Get-RequiredInt $header 'NAXIS') -ne 2) {
        throw 'ASTAP WCS must describe a two-dimensional image.'
    }
    $width = Get-RequiredInt $header 'NAXIS1'
    $height = Get-RequiredInt $header 'NAXIS2'
    if ($width -le 0 -or $height -le 0 -or
            $width -gt 100000 -or $height -gt 100000) {
        throw 'ASTAP WCS dimensions are invalid.'
    }
    if ((Get-RequiredString $header 'CTYPE1') -cne 'RA---TAN' -or
            (Get-RequiredString $header 'CTYPE2') -cne 'DEC--TAN') {
        throw 'ASTAP WCS must use the undistorted RA---TAN/DEC--TAN projection.'
    }
    $ra = Get-RequiredDouble $header 'CRVAL1'
    $dec = Get-RequiredDouble $header 'CRVAL2'
    if ($ra -lt 0 -or $ra -ge 360 -or $dec -lt -90 -or $dec -gt 90) {
        throw 'ASTAP WCS centre is out of range.'
    }
    $crpix1 = Get-RequiredDouble $header 'CRPIX1'
    $crpix2 = Get-RequiredDouble $header 'CRPIX2'
    if ($crpix1 -lt 0.5 -or $crpix1 -gt $width + 0.5 -or
            $crpix2 -lt 0.5 -or $crpix2 -gt $height + 0.5) {
        throw 'ASTAP WCS reference pixel is outside the image.'
    }
    $observationText = Get-RequiredString $header 'DATE-OBS'
    $observationUtc = [DateTimeOffset]::MinValue
    $dateStyles = [Globalization.DateTimeStyles]::AssumeUniversal -bor
        [Globalization.DateTimeStyles]::AdjustToUniversal
    if (-not [DateTimeOffset]::TryParse(
            $observationText, $invariant, $dateStyles, [ref]$observationUtc)) {
        throw 'ASTAP WCS DATE-OBS is invalid.'
    }
    $cdKeys = @('CD1_1', 'CD1_2', 'CD2_1', 'CD2_2')
    $hasAnyCd = @($cdKeys | Where-Object { $header.Contains($_) }).Count -gt 0
    $hasAllCd = @($cdKeys | Where-Object { $header.Contains($_) }).Count -eq 4
    $hasAnyCdelt = $header.Contains('CDELT1') -or
        $header.Contains('CDELT2') -or $header.Contains('CROTA2')
    $hasAllCdelt = $header.Contains('CDELT1') -and
        $header.Contains('CDELT2') -and $header.Contains('CROTA2')
    if (($hasAnyCd -and -not $hasAllCd) -or
            ($hasAnyCdelt -and -not $hasAllCdelt) -or
            ($hasAllCd -eq $hasAllCdelt)) {
        throw 'ASTAP WCS must contain exactly one complete CD or CDELT/CROTA2 scale model.'
    }
    if ($hasAllCd) {
        $cd11 = Get-RequiredDouble $header 'CD1_1'
        $cd12 = Get-RequiredDouble $header 'CD1_2'
        $cd21 = Get-RequiredDouble $header 'CD2_1'
        $cd22 = Get-RequiredDouble $header 'CD2_2'
        $determinant = $cd11 * $cd22 - $cd12 * $cd21
        if ([double]::IsNaN($determinant) -or
                [double]::IsInfinity($determinant) -or
                [Math]::Abs($determinant) -lt 1e-18) {
            throw 'ASTAP WCS CD matrix is singular.'
        }
        $xScale = 3600.0 * [Math]::Sqrt($cd11 * $cd11 + $cd21 * $cd21)
        $yScale = 3600.0 * [Math]::Sqrt($cd12 * $cd12 + $cd22 * $cd22)
        $a = $cd11 * $cd11 + $cd21 * $cd21
        $b = $cd11 * $cd12 + $cd21 * $cd22
        $d = $cd12 * $cd12 + $cd22 * $cd22
        $discriminant = [Math]::Sqrt([Math]::Max(
            0.0, ($a - $d) * ($a - $d) + 4.0 * $b * $b))
        $largest = [Math]::Sqrt(0.5 * ($a + $d + $discriminant))
        $smallestSquared = 0.5 * ($a + $d - $discriminant)
        if ([double]::IsNaN($largest) -or [double]::IsInfinity($largest) -or
                $smallestSquared -le 0) {
            throw 'ASTAP WCS CD matrix is ill-conditioned.'
        }
        $smallest = [Math]::Sqrt($smallestSquared)
        if (($largest - $smallest) / (0.5 * ($largest + $smallest)) -gt 0.02) {
            throw 'ASTAP WCS CD matrix scale/shear condition exceeds 2 percent.'
        }
        $scaleModel = 'CD'
    } else {
        $cdelt1 = Get-RequiredDouble $header 'CDELT1'
        $cdelt2 = Get-RequiredDouble $header 'CDELT2'
        $rotation = (Get-RequiredDouble $header 'CROTA2') * [Math]::PI / 180.0
        $cd11 = $cdelt1 * [Math]::Cos($rotation)
        $cd12 = -$cdelt2 * [Math]::Sin($rotation)
        $cd21 = $cdelt1 * [Math]::Sin($rotation)
        $cd22 = $cdelt2 * [Math]::Cos($rotation)
        $xScale = 3600.0 * [Math]::Abs($cdelt1)
        $yScale = 3600.0 * [Math]::Abs($cdelt2)
        $scaleModel = 'CDELT-CROTA2'
    }
    if ($xScale -lt 0.01 -or $yScale -lt 0.01 -or
            $xScale -gt 60.0 -or $yScale -gt 60.0 -or
            [double]::IsNaN($xScale) -or [double]::IsInfinity($xScale) -or
            [double]::IsNaN($yScale) -or [double]::IsInfinity($yScale)) {
        throw 'ASTAP WCS pixel scale is invalid.'
    }
    $meanScale = 0.5 * ($xScale + $yScale)
    if ([Math]::Abs($xScale - $yScale) / $meanScale -gt 0.02) {
        throw 'ASTAP WCS pixel-scale anisotropy exceeds 2 percent.'
    }
    $factor = [Math]::PI / 180.0
    $deltaX = 0.5 * ($width + 1.0) - $crpix1
    $deltaY = 0.5 * ($height + 1.0) - $crpix2
    $xi = ($cd11 * $deltaX + $cd12 * $deltaY) * $factor
    $eta = ($cd21 * $deltaX + $cd22 * $deltaY) * $factor
    $ra0 = $ra * $factor
    $dec0 = $dec * $factor
    $denominator = [Math]::Cos($dec0) - $eta * [Math]::Sin($dec0)
    $sensorRa = $ra0 + [Math]::Atan2($xi, $denominator)
    $sensorDec = [Math]::Atan2(
        [Math]::Sin($dec0) + $eta * [Math]::Cos($dec0),
        [Math]::Sqrt($denominator * $denominator + $xi * $xi))
    $sensorRaDegrees = (($sensorRa / $factor) % 360.0 + 360.0) % 360.0
    [pscustomobject][ordered]@{
        RightAscensionDegrees = $sensorRaDegrees
        DeclinationDegrees = $sensorDec / $factor
        ObservationUtc = $observationUtc.ToUniversalTime()
        WidthPixels = $width
        HeightPixels = $height
        PixelScaleXArcseconds = $xScale
        PixelScaleYArcseconds = $yScale
        ScaleModel = $scaleModel
    }
}

function Get-GreatCircleSeparationRadians(
        [double]$Ra1, [double]$Dec1, [double]$Ra2, [double]$Dec2) {
    $factor = [Math]::PI / 180.0
    $ra1r = $Ra1 * $factor
    $ra2r = $Ra2 * $factor
    $dec1r = $Dec1 * $factor
    $dec2r = $Dec2 * $factor
    $cosine = [Math]::Sin($dec1r) * [Math]::Sin($dec2r) +
        [Math]::Cos($dec1r) * [Math]::Cos($dec2r) *
        [Math]::Cos($ra2r - $ra1r)
    [Math]::Acos([Math]::Max(-1.0, [Math]::Min(1.0, $cosine)))
}

function Get-BundleSource([string]$Source, [string]$DeclaredHash,
        [string]$Label, [string]$ReceiptDirectory) {
    $full = [IO.Path]::GetFullPath($Source)
    if (-not [IO.File]::Exists($full)) {
        throw "$Label WCS source is missing: $full"
    }
    $relative = [IO.Path]::GetRelativePath($ReceiptDirectory, $full)
    if ([IO.Path]::IsPathRooted($relative) -or $relative -eq '..' -or
            $relative.StartsWith('..' + [IO.Path]::DirectorySeparatorChar,
                [StringComparison]::Ordinal)) {
        throw "$Label WCS source must be inside the receipt bundle."
    }
    $rootLength = [IO.Path]::GetPathRoot($full).Length
    if ($full.Substring($rootLength).Contains(':') -or
            @($relative -split '[\\/]' | Where-Object {
                $_.EndsWith(' ') -or $_.EndsWith('.') }).Count -gt 0) {
        throw "$Label WCS source path is ambiguous."
    }
    $current = [IO.Path]::GetFullPath($ReceiptDirectory)
    if (([IO.File]::GetAttributes($current) -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw 'OAG geometry bundle uses a reparse point.'
    }
    foreach ($segment in $relative -split '[\\/]') {
        if ([string]::IsNullOrWhiteSpace($segment)) { continue }
        $current = [IO.Path]::Combine($current, $segment)
        if (([IO.File]::GetAttributes($current) -band
                [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "$Label WCS source uses a reparse point."
        }
    }
    $stream = [IO.File]::Open($full, [IO.FileMode]::Open,
        [IO.FileAccess]::Read, [IO.FileShare]::Read)
    try {
        if ($stream.Length -le 0 -or $stream.Length -gt 1000000) {
            throw "$Label WCS source size is invalid."
        }
        $memory = [IO.MemoryStream]::new([int]$stream.Length)
        try {
            $stream.CopyTo($memory)
            $bytes = $memory.ToArray()
        } finally { $memory.Dispose() }
    } finally { $stream.Dispose() }
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $hash = [Convert]::ToHexString($sha.ComputeHash($bytes)).ToLowerInvariant() }
    finally { $sha.Dispose() }
    if ($DeclaredHash -and $hash -ne $DeclaredHash.ToLowerInvariant()) {
        throw "$Label WCS source SHA-256 does not match the supplied bytes."
    }
    [pscustomobject]@{
        FullPath = $full
        RelativePath = $relative
        Sha256 = $hash
        Bytes = $bytes
    }
}

function Write-CreateNewUtf8([string]$Path, [string]$Content) {
    $full = [IO.Path]::GetFullPath($Path)
    $parent = [IO.Path]::GetDirectoryName($full)
    if ([string]::IsNullOrWhiteSpace($parent) -or
            -not [IO.Directory]::Exists($parent)) {
        throw "Output parent directory is missing: $parent"
    }
    if ([IO.File]::Exists($full)) { throw "Output already exists: $full" }
    $temporary = Join-Path $parent (
        ([IO.Path]::GetFileName($full)) + '.new-' + [Guid]::NewGuid().ToString('N'))
    try {
        $stream = [IO.File]::Open($temporary, [IO.FileMode]::CreateNew,
            [IO.FileAccess]::Write, [IO.FileShare]::Read)
        try {
            $writer = [IO.StreamWriter]::new(
                $stream, [Text.UTF8Encoding]::new($false), 1024, $true)
            try {
                $writer.Write($Content)
                $writer.Flush()
            } finally { $writer.Dispose() }
            $stream.Flush($true)
        } finally { $stream.Dispose() }
        [IO.File]::Move($temporary, $full)
    } finally {
        if ([IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) }
    }
}

$outputFull = [IO.Path]::GetFullPath($OutputPath)
$receiptDirectory = [IO.Path]::GetDirectoryName($outputFull)
if ([string]::IsNullOrWhiteSpace($receiptDirectory) -or
        -not [IO.Directory]::Exists($receiptDirectory)) {
    throw "Output parent directory is missing: $receiptDirectory"
}
$mainSource = Get-BundleSource $MainWcsPath $MainSolutionSha256 'Main' $receiptDirectory
$guideSource = Get-BundleSource $GuideWcsPath $GuideSolutionSha256 'Guide' $receiptDirectory
if ($mainSource.FullPath -eq $guideSource.FullPath -or
        $mainSource.Sha256 -eq $guideSource.Sha256) {
    throw 'Main and guide WCS sources must be distinct artifacts.'
}
$main = Get-StrictAstapWcs $mainSource.Bytes 'Main'
$guide = Get-StrictAstapWcs $guideSource.Bytes 'Guide'
$observationDelta = [Math]::Abs(
    ($main.ObservationUtc - $guide.ObservationUtc).TotalSeconds)
if ($observationDelta -gt 60.0) {
    throw 'Main and guide WCS observations are not time-coherent.'
}
$mainScale = [Math]::Min(
    $main.PixelScaleXArcseconds, $main.PixelScaleYArcseconds)
$guideScale = [Math]::Max(
    $guide.PixelScaleXArcseconds, $guide.PixelScaleYArcseconds)
$centerSeparationRadians = Get-GreatCircleSeparationRadians $main.RightAscensionDegrees $main.DeclinationDegrees $guide.RightAscensionDegrees $guide.DeclinationDegrees
$centerSeparationArcseconds =
    $centerSeparationRadians * 180.0 / [Math]::PI * 3600.0
$mainHalfDiagonal = 0.5 * [Math]::Sqrt(
    [double]$main.WidthPixels * $main.WidthPixels +
    [double]$main.HeightPixels * $main.HeightPixels)
if ($hasLockX) {
    if ([double]::IsNaN($GuideLockOffsetXFromCenterPixels) -or
            [double]::IsInfinity($GuideLockOffsetXFromCenterPixels) -or
            [double]::IsNaN($GuideLockOffsetYFromCenterPixels) -or
            [double]::IsInfinity($GuideLockOffsetYFromCenterPixels)) {
        throw 'Guide lock offsets must be finite.'
    }
    $guideRadial = [Math]::Sqrt(
        $GuideLockOffsetXFromCenterPixels * $GuideLockOffsetXFromCenterPixels +
        $GuideLockOffsetYFromCenterPixels * $GuideLockOffsetYFromCenterPixels)
    $evidenceKind = 'measured-lock-offset-upper-bound'
} else {
    $guideRadial = 0.5 * [Math]::Sqrt(
        [double]$guide.WidthPixels * $guide.WidthPixels +
        [double]$guide.HeightPixels * $guide.HeightPixels)
    $evidenceKind = 'full-guide-sensor-upper-bound'
}
$centerMainPixels = $centerSeparationArcseconds / $mainScale
$guideRadialArcseconds = $guideRadial * $guideScale
$guideRadialMainPixels = $guideRadialArcseconds / $mainScale

$result = [pscustomobject][ordered]@{
    SchemaVersion = 2
    Model = 'orientation-independent-spherical-triangle-upper-bound'
    FormulaVersion = 'orientation-independent-spherical-triangle-upper-bound/v2'
    GeometryProvenance = 'derived-astap-wcs'
    MainSolutionSource = $mainSource.RelativePath
    MainSolutionSha256 = $mainSource.Sha256
    GuideSolutionSource = $guideSource.RelativePath
    GuideSolutionSha256 = $guideSource.Sha256
    MainCenterRightAscensionDegrees = $main.RightAscensionDegrees
    MainCenterDeclinationDegrees = $main.DeclinationDegrees
    MainObservationUtc = $main.ObservationUtc.ToString('O', $invariant)
    MainWidthPixels = $main.WidthPixels
    MainHeightPixels = $main.HeightPixels
    MainPixelScaleXArcseconds = $main.PixelScaleXArcseconds
    MainPixelScaleYArcseconds = $main.PixelScaleYArcseconds
    MainScaleModel = $main.ScaleModel
    GuideCenterRightAscensionDegrees = $guide.RightAscensionDegrees
    GuideCenterDeclinationDegrees = $guide.DeclinationDegrees
    GuideObservationUtc = $guide.ObservationUtc.ToString('O', $invariant)
    GuideWidthPixels = $guide.WidthPixels
    GuideHeightPixels = $guide.HeightPixels
    GuidePixelScaleXArcseconds = $guide.PixelScaleXArcseconds
    GuidePixelScaleYArcseconds = $guide.PixelScaleYArcseconds
    GuideScaleModel = $guide.ScaleModel
    ObservationDeltaSeconds = $observationDelta
    MaximumObservationDeltaSeconds = 60.0
    GuideLockOffsetXFromCenterPixels = $(if ($hasLockX) {
        $GuideLockOffsetXFromCenterPixels } else { $null })
    GuideLockOffsetYFromCenterPixels = $(if ($hasLockY) {
        $GuideLockOffsetYFromCenterPixels } else { $null })
    MainPixelScaleUsedArcseconds = $mainScale
    GuidePixelScaleUsedArcseconds = $guideScale
    CenterSeparationArcseconds = $centerSeparationArcseconds
    CenterSeparationMainPixels = $centerMainPixels
    MainHalfDiagonalPixels = $mainHalfDiagonal
    GuideRadialEvidenceKind = $evidenceKind
    GuideRadialPixels = $guideRadial
    GuideRadialArcseconds = $guideRadialArcseconds
    GuideRadialMainPixels = $guideRadialMainPixels
    GuideToFarthestMainCornerUpperBoundPixels =
        $centerMainPixels + $guideRadialMainPixels + $mainHalfDiagonal
    UsesOrientationConvention = $false
    GrantsMotionAuthority = $false
    GrantsAbsolutePolarAccuracyClaim = $false
}
$json = ($result | ConvertTo-Json -Depth 4) + "`r`n"
Write-CreateNewUtf8 $outputFull $json
$result
