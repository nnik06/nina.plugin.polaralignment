param(
    [string]$SourcePath = 'C:\dev\upas-nina-tppa-plugin\tools\tppa_phd2_supervisor.ps1',
    [string]$CloudLogPath = 'C:\tmp\nina-20260715-cloud.log'
)

$tokens = $null
$parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile(
    $SourcePath,
    [ref]$tokens,
    [ref]$parseErrors
)
if (@($parseErrors).Count -gt 0) {
    throw "Supervisor parse failed: $(@($parseErrors | ForEach-Object Message) -join '; ')"
}

$functionAst = @($ast.FindAll({
    param($node)
    $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
        $node.Name -eq 'Test-NinaExplicitCloudFailure'
}, $true))
if ($functionAst.Count -ne 1) {
    throw "Expected one Test-NinaExplicitCloudFailure function, found $($functionAst.Count)"
}

$script:FixturePath = $null
$script:Messages = [System.Collections.Generic.List[string]]::new()
$EnableExplicitCloudPreflight = $true

function Get-LatestNinaLogPath { return $script:FixturePath }
function Read-NinaLogSharedTail {
    param([string]$Path, [int]$Count = 5000)
    @([IO.File]::ReadAllLines($Path) | Select-Object -Last $Count)
}
function Log { param([string]$Text); [void]$script:Messages.Add($Text) }

Invoke-Expression $functionAst[0].Extent.Text

function Assert-Equal {
    param([object]$Expected, [object]$Actual, [string]$Name)
    if ($Expected -ne $Actual) {
        throw "$Name failed: expected '$Expected', actual '$Actual'"
    }
    Write-Output "PASS: $Name"
}

$script:FixturePath = $CloudLogPath
Assert-Equal $true (Test-NinaExplicitCloudFailure -SinceLocal ([datetime]'2026-07-15T01:02:46')) 'real cloud log detects explicit too-few-stars result'
Assert-Equal $false (Test-NinaExplicitCloudFailure -SinceLocal ([datetime]'2026-07-15T03:44:00')) 'real cloud log excludes stale cloud result'

$genericFailurePath = 'C:\tmp\tppa-generic-solve-failure.log'
[IO.File]::WriteAllLines($genericFailurePath, @(
    '2026-07-15T21:00:00.0000|ERROR|ASTAPSolver.cs|ReadResult|62|ASTAP - Plate solve failed.',
    '',
    'Search radius exhausted.',
    '2026-07-15T21:00:01.0000|INFO|ImageSolver.cs|Solve|56|Platesolve failed'
))
$script:FixturePath = $genericFailurePath
Assert-Equal $false (Test-NinaExplicitCloudFailure -SinceLocal ([datetime]'2026-07-15T20:59:59')) 'generic solve failure is not classified as cloud'

$explicitFailurePath = 'C:\tmp\tppa-explicit-cloud-failure.log'
[IO.File]::WriteAllLines($explicitFailurePath, @(
    '2026-07-15T21:00:00.0000|ERROR|ASTAPSolver.cs|ReadResult|62|ASTAP - Plate solve failed.',
    '',
    'Not enough stars.',
    '2026-07-15T21:00:01.0000|INFO|ImageSolver.cs|Solve|56|Platesolve failed'
))
$script:FixturePath = $explicitFailurePath
Assert-Equal $true (Test-NinaExplicitCloudFailure -SinceLocal ([datetime]'2026-07-15T20:59:59')) 'explicit continuation is classified as cloud'

$successBreakPath = 'C:\tmp\tppa-success-breaks-cloud-association.log'
[IO.File]::WriteAllLines($successBreakPath, @(
    '2026-07-15T21:00:00.0000|ERROR|ASTAPSolver.cs|ReadResult|62|ASTAP - Plate solve failed.',
    '2026-07-15T21:00:00.5000|INFO|ImageSolver.cs|Solve|41|Platesolve successful: RA 1 Dec 2',
    'Not enough stars.'
))
$script:FixturePath = $successBreakPath
Assert-Equal $false (Test-NinaExplicitCloudFailure -SinceLocal ([datetime]'2026-07-15T20:59:59')) 'success clears pending failure association'

$EnableExplicitCloudPreflight = $false
$script:FixturePath = $explicitFailurePath
Assert-Equal $false (Test-NinaExplicitCloudFailure -SinceLocal ([datetime]'2026-07-15T20:59:59')) 'preflight opt-out is honored'

Write-Output 'Cloud-preflight tests passed.'
