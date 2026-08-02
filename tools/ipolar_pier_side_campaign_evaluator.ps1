param(
    [Parameter(Mandatory = $true)]
    [string]$ManifestPath,
    [ValidateRange(0.1, 300.0)]
    [double]$MaximumAxisCenterSpreadArcsec = 30.0,
    [string]$OutputPath = ''
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$manifestFullPath = [IO.Path]::GetFullPath($ManifestPath)
if (-not (Test-Path -LiteralPath $manifestFullPath -PathType Leaf)) { throw "Campaign manifest not found: $manifestFullPath" }
$manifest = [IO.File]::ReadAllText($manifestFullPath) | ConvertFrom-Json
$campaignId = [string]$manifest.CampaignId
if ([string]::IsNullOrWhiteSpace($campaignId)) { throw 'Campaign manifest requires CampaignId.' }
$manifestLegs = @($manifest.Legs)
if ($manifestLegs.Count -ne 4) { throw "Campaign manifest requires exactly four legs; found $($manifestLegs.Count)." }
$requiredRoles = @('west-outbound', 'west-return', 'east-outbound', 'east-return')
$roles = @($manifestLegs | ForEach-Object { [string]$_.Role })
foreach ($requiredRole in $requiredRoles) {
    if (@($roles | Where-Object { $_ -eq $requiredRole }).Count -ne 1) {
        throw "Campaign manifest requires exactly one '$requiredRole' leg."
    }
}

$issues = New-Object System.Collections.Generic.List[string]
$legs = New-Object System.Collections.Generic.List[object]
$seenPaths = @{}
$seenRunIds = @{}
foreach ($manifestLeg in $manifestLegs) {
    $role = [string]$manifestLeg.Role
    $path = [string]$manifestLeg.ResultPath
    $fullPath = [IO.Path]::GetFullPath($path)
    if ($seenPaths.ContainsKey($fullPath)) { $issues.Add("Duplicate leg result path: $fullPath"); continue }
    $seenPaths[$fullPath] = $true
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) { $issues.Add("Missing leg result: $fullPath"); continue }
    $sha256 = (Get-FileHash -LiteralPath $fullPath -Algorithm SHA256).Hash
    $expectedSha256 = [string]$manifestLeg.ResultSha256
    if ([string]::IsNullOrWhiteSpace($expectedSha256)) {
        $issues.Add("Manifest leg '$role' has no sealed ResultSha256.")
    } elseif ($sha256 -ne $expectedSha256) {
        $issues.Add("Manifest leg '$role' result hash mismatch: expected $expectedSha256, actual $sha256.")
    }
    $leg = [IO.File]::ReadAllText($fullPath) | ConvertFrom-Json
    if ([int]$leg.SchemaVersion -ne 1) { $issues.Add("Unsupported leg schema in $fullPath."); continue }
    if (-not [bool]$leg.DifferentialAxisStabilityQualified) { $issues.Add("Leg is not qualified: $fullPath") }
    if ($null -eq $leg.AxisCenterX -or $null -eq $leg.AxisCenterY) { $issues.Add("Leg has no fitted axis center: $fullPath") }
    $runId = [string]$leg.RunId
    if ([string]::IsNullOrWhiteSpace($runId)) { $issues.Add("Leg has no run ID: $fullPath") }
    elseif ($seenRunIds.ContainsKey($runId)) { $issues.Add("Duplicate leg run ID: $runId") }
    else { $seenRunIds[$runId] = $true }
    $expectedPier = if ($role.StartsWith('west-')) { 'pierWest' } else { 'pierEast' }
    if ([string]$leg.SideOfPier -ne $expectedPier) {
        $issues.Add("Role '$role' requires $expectedPier but leg reports '$($leg.SideOfPier)'.")
    }
    [void]$legs.Add([pscustomobject]@{
        Role = $role
        Path = $fullPath
        Sha256 = $sha256
        RunId = $runId
        SideOfPier = [string]$leg.SideOfPier
        Result = $leg
    })
}

$arcsecPerPixel = $null
if ($legs.Count -gt 0) {
    $scales = @($legs | ForEach-Object { [double]$_.Result.ArcsecPerPixel } | Select-Object -Unique)
    if ($scales.Count -ne 1) { $issues.Add('Legs do not share one arcsec-per-pixel scale.') }
    else { $arcsecPerPixel = [double]$scales[0] }
}

$maximumCenterDistancePixels = 0.0
$maximumPair = $null
if ($legs.Count -eq 4 -and $null -ne $arcsecPerPixel) {
    for ($left = 0; $left -lt $legs.Count; $left++) {
        for ($right = $left + 1; $right -lt $legs.Count; $right++) {
            $dx = [double]$legs[$left].Result.AxisCenterX - [double]$legs[$right].Result.AxisCenterX
            $dy = [double]$legs[$left].Result.AxisCenterY - [double]$legs[$right].Result.AxisCenterY
            $distance = [Math]::Sqrt($dx * $dx + $dy * $dy)
            if ($distance -gt $maximumCenterDistancePixels) {
                $maximumCenterDistancePixels = $distance
                $maximumPair = @($legs[$left].Role, $legs[$right].Role)
            }
        }
    }
    if ($maximumCenterDistancePixels * $arcsecPerPixel -gt $MaximumAxisCenterSpreadArcsec) {
        $issues.Add(("Maximum fitted-axis spread {0:F2} arcsec exceeds {1:F2} arcsec." -f ($maximumCenterDistancePixels * $arcsecPerPixel), $MaximumAxisCenterSpreadArcsec))
    }
}

$result = [ordered]@{
    SchemaVersion = 2
    CampaignId = $campaignId
    EvaluatedUtc = [DateTime]::UtcNow.ToString('o')
    ManifestPath = $manifestFullPath
    ManifestSha256 = (Get-FileHash -LiteralPath $manifestFullPath -Algorithm SHA256).Hash
    LegCount = $legs.Count
    Legs = @($legs | ForEach-Object {
        [ordered]@{ Role = $_.Role; ResultPath = $_.Path; ResultSha256 = $_.Sha256; RunId = $_.RunId; SideOfPier = $_.SideOfPier }
    })
    MaximumAxisCenterSpreadPixels = if ($null -eq $arcsecPerPixel) { $null } else { $maximumCenterDistancePixels }
    MaximumAxisCenterSpreadArcsec = if ($null -eq $arcsecPerPixel) { $null } else { $maximumCenterDistancePixels * $arcsecPerPixel }
    MaximumSpreadPair = $maximumPair
    DifferentialPierSideStabilityQualified = ($issues.Count -eq 0)
    Issues = $issues.ToArray()
    GrantsUpasAuthority = $false
    GrantsAbsoluteAccuracyClaim = $false
}
$json = $result | ConvertTo-Json -Depth 7
if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
    [IO.File]::WriteAllText([IO.Path]::GetFullPath($OutputPath), $json, [Text.UTF8Encoding]::new($false))
}
$result
if (-not $result.DifferentialPierSideStabilityQualified) { exit 2 }
