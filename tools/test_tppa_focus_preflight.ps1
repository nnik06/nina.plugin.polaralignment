param([string]$SourcePath = 'C:\dev\upas-nina-tppa-plugin\tools\tppa_phd2_supervisor.ps1')

$tokens = $null
$parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($SourcePath, [ref]$tokens, [ref]$parseErrors)
if (@($parseErrors).Count -gt 0) { throw "Supervisor parse failed: $(@($parseErrors | ForEach-Object Message) -join '; ')" }

$names = @('Get-LatestSuccessfulAutofocus', 'Test-NinaAutofocusCompletionLog', 'Assert-RecentSuccessfulAutofocus')
foreach ($name in $names) {
    $functionAst = @($ast.FindAll({
        param($node)
        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name
    }, $true))
    if ($functionAst.Count -ne 1) { throw "Expected one $name function, found $($functionAst.Count)" }
    Invoke-Expression $functionAst[0].Extent.Text
}

$fixture = Join-Path $env:TEMP ('tppa-focus-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($fixture) | Out-Null
$AutofocusLogDirectory = $fixture
$NinaLogDirectory = $fixture
$RequireRecentAutofocus = $true
$MaxAutofocusAgeMinutes = 90
$script:Messages = [System.Collections.Generic.List[string]]::new()
function Log { param([string]$Text); [void]$script:Messages.Add($Text) }
function Read-NinaLogSharedTail {
    param([string]$Path, [int]$Count = 5000)
    @([IO.File]::ReadAllLines($Path) | Select-Object -Last $Count)
}

function Write-FocusResult {
    param([string]$Name, [datetimeoffset]$Timestamp, [double]$Hfr = 1.5, [double]$Position = 6794)
    $json = [ordered]@{
        FinalHFR = $Hfr
        Filter = 'L'
        Timestamp = $Timestamp.ToString('O')
        Temperature = 28.5
        CalculatedFocusPoint = @{ Position = $Position; Value = 0.7; Error = 0 }
    } | ConvertTo-Json -Depth 5
    [IO.File]::WriteAllText((Join-Path $fixture $Name), $json)
}

try {
    $missingFailed = $false
    try { Assert-RecentSuccessfulAutofocus } catch { $missingFailed = $_.Exception.Message -match 'no valid successful' }
    if (-not $missingFailed) { throw 'missing autofocus result did not fail closed' }
    Write-Output 'PASS: missing autofocus fails closed'

    Write-FocusResult -Name 'stale.json' -Timestamp ([datetimeoffset]::Now.AddMinutes(-120))
    $staleFailed = $false
    try { Assert-RecentSuccessfulAutofocus } catch { $staleFailed = $_.Exception.Message -match '120' -and $_.Exception.Message -match 'limit 90' }
    if (-not $staleFailed) { throw 'stale autofocus result was accepted' }
    Write-Output 'PASS: stale autofocus is rejected'

    $recentTimestamp = [datetimeoffset]::Now.AddMinutes(-5)
    Start-Sleep -Milliseconds 20
    Write-FocusResult -Name 'recent.json' -Timestamp $recentTimestamp
    $unconfirmedFailed = $false
    try { Assert-RecentSuccessfulAutofocus } catch { $unconfirmedFailed = $_.Exception.Message -match 'not paired with a NINA successful-autofocus event' }
    if (-not $unconfirmedFailed) { throw 'unconfirmed autofocus report was accepted' }
    Write-Output 'PASS: report without NINA success event is rejected'

    $successTimestamp = $recentTimestamp.LocalDateTime.AddMilliseconds(150).ToString("yyyy-MM-dd'T'HH:mm:ss.ffff", [Globalization.CultureInfo]::InvariantCulture)
    [IO.File]::WriteAllLines((Join-Path $fixture 'nina.log'), @(
        "$successTimestamp|INFO|FocuserMediator.cs|BroadcastSuccessfulAutoFocusRun|46|Autofocus notification received - Temperature 28.5"
    ))
    Assert-RecentSuccessfulAutofocus
    if (-not ($script:Messages -match 'Focus preflight passed')) { throw 'recent autofocus result did not log pass details' }
    Write-Output 'PASS: recent autofocus paired with NINA success event is accepted'

    $RequireRecentAutofocus = $false
    Remove-Item -LiteralPath (Join-Path $fixture 'recent.json') -Force
    Remove-Item -LiteralPath (Join-Path $fixture 'stale.json') -Force
    Assert-RecentSuccessfulAutofocus
    if (-not ($script:Messages -match 'Focus preflight disabled')) { throw 'focus preflight opt-out was not logged' }
    Write-Output 'PASS: explicit opt-out is honored'
} finally {
    if (Test-Path -LiteralPath $fixture) { Remove-Item -LiteralPath $fixture -Recurse -Force }
}

Write-Output 'Focus-preflight tests passed.'
