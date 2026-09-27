param([Parameter(Mandatory = $true)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$publishDirectory = Join-Path $projectRoot 'artifacts\yfm-companion-v2-guide-scoring'
$renderDirectory = Join-Path $projectRoot 'artifacts\ui-render-guide-scoring'
$settingsDirectory = Join-Path $projectRoot 'artifacts\guide-scoring-ui-settings'
$previousSettings = $env:YFM_COMPANION_SETTINGS_DIRECTORY
$env:MSBUILDDISABLENODEREUSE = '1'
Push-Location $projectRoot
try {
    $env:YFM_COMPANION_SETTINGS_DIRECTORY = $settingsDirectory
    dotnet build tools\YfmCompanion.UiRender -c Release --no-restore -m:1 -v:q
    if ($LASTEXITCODE -ne 0) { throw 'UI renderer build failed.' }
    dotnet run --project tools\YfmCompanion.UiRender -c Release --no-build -- $renderDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Production WPF rendering failed.' }
    dotnet publish src\YfmCompanion.Desktop\YfmCompanion.Desktop.csproj -c Release --no-restore -m:1 -o $publishDirectory -v:minimal
    if ($LASTEXITCODE -ne 0) { throw 'Windows publish failed.' }
    $publishedDatabase = Join-Path $publishDirectory 'Data\yfm.db'
    if ((Get-FileHash -LiteralPath $publishedDatabase).Hash -ne (Get-FileHash -LiteralPath (Join-Path $projectRoot 'artifacts\yfm.db')).Hash) {
        throw 'Published database differs from canonical database.'
    }
    foreach ($fileName in @('guardian_star_rules.json', 'opponent_reference.json', 'optimizer_policy.json', 'source_manifest.json')) {
        $file = Get-Item -LiteralPath (Join-Path $projectRoot "docs\research\data\$fileName")
        $published = Join-Path $publishDirectory "ResearchData\$($file.Name)"
        if ((Get-FileHash -LiteralPath $published).Hash -ne (Get-FileHash -LiteralPath $file.FullName).Hash) {
            throw "Published research data mismatch: $($file.Name)"
        }
    }
    $startupSettings = Join-Path $projectRoot 'artifacts\guide-scoring-startup-settings'
    [IO.Directory]::CreateDirectory($startupSettings) | Out-Null
    $env:YFM_COMPANION_SETTINGS_DIRECTORY = $startupSettings
    $process = $null
    try {
        $process = Start-Process -FilePath (Join-Path $publishDirectory 'YFM Fusion Companion.exe') -WorkingDirectory $publishDirectory -WindowStyle Hidden -PassThru
        Start-Sleep -Seconds 4
        $process.Refresh()
        if ($process.HasExited) { throw "Published executable exited during startup: $($process.ExitCode)" }
    }
    finally {
        if ($null -ne $process -and -not $process.HasExited) {
            $null = $process.CloseMainWindow()
            if (-not $process.WaitForExit(3000)) { Stop-Process -Id $process.Id -Force }
        }
    }
    $name = 'YFM-Fusion-Companion-2.0-GUIDE-CPU4-8-' + (Get-Date -Format 'yyyyMMdd-HHmmss')
    $packageDirectory = Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) $name
    [IO.Directory]::CreateDirectory($packageDirectory) | Out-Null
    Copy-Item -Path (Join-Path $publishDirectory '*') -Destination $packageDirectory -Recurse
    Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\CPU_PARALLELISM.md') -Destination (Join-Path $packageDirectory 'CPU-PARALLELISM.md')
    Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\benchmarks\cpu-parallelism-final.json') -Destination $packageDirectory
    foreach ($evidence in @('CPU_HEAVY_FUSION_RESULTS.md', 'cpu-heavy-fusions.json', 'CPU_SIX_EIGHT_RESULTS.md')) {
        Copy-Item -LiteralPath (Join-Path $projectRoot "docs\benchmarks\$evidence") -Destination $packageDirectory
    }
    Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\research\GUIDE_SCORING.md') -Destination $packageDirectory
    Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\benchmarks\guide-scoring.json') -Destination $packageDirectory
    Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination $packageDirectory
    @'
YFM Fusion Companion 2.0 - guide-informed deck scoring update

Extract the complete folder and double-click YFM Fusion Companion.exe.
Keep Data and ResearchData next to the executable. This Windows x64 build
includes its .NET runtime; no separate .NET installation is needed.

In Owned-Card Optimizer, expand Advanced Strategy Settings and choose CPU
Workers. Select Auto for four search workers and eight exact-analysis workers.
Smaller CPUs use reduced counts. Existing manual worker settings override Auto.
One worker restores sequential execution. Restart the app using this executable
to use the update; a previously running copy continues using its old code.

GUIDE_SCORING.md explains the revised model, sources, and limitations.
Setup availability may require separate turns; it is not a win probability.
Old proof checkpoints use an incompatible scoring identity; preserve them and
use the existing New proof checkpoint workflow for a fresh proof.
CPU-PARALLELISM.md describes worker settings and timing boundaries.
'@ | Set-Content -LiteralPath (Join-Path $packageDirectory 'START-HERE.txt') -Encoding utf8
    $files = @(Get-ChildItem -LiteralPath $packageDirectory -File -Recurse | ForEach-Object {
        [pscustomobject]@{ Path = [IO.Path]::GetRelativePath($packageDirectory, $_.FullName); SHA256 = (Get-FileHash -LiteralPath $_.FullName).Hash }
    })
    $files | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $packageDirectory 'SHA256.json') -Encoding utf8
    $archive = "$packageDirectory.zip"
    Compress-Archive -LiteralPath $packageDirectory -DestinationPath $archive
    $verificationDirectory = Join-Path $projectRoot "artifacts\cpu-package-verification\$name"
    Expand-Archive -LiteralPath $archive -DestinationPath $verificationDirectory
    foreach ($file in $files) {
        $extracted = Join-Path (Join-Path $verificationDirectory $name) $file.Path
        if ((Get-FileHash -LiteralPath $extracted).Hash -ne $file.SHA256) { throw "Extracted file mismatch: $($file.Path)" }
    }
    $trx = [xml](Get-Content -LiteralPath (Join-Path $projectRoot 'artifacts\guide-scoring-tests\guide-scoring.trx') -Raw)
    $counters = $trx.SelectSingleNode("//*[local-name()='ResultSummary']/*[local-name()='Counters']")
    $tests = [pscustomobject]@{ Total = [int]$counters.GetAttribute('total'); Passed = [int]$counters.GetAttribute('passed'); Failed = [int]$counters.GetAttribute('failed') }
    if ($tests.Total -lt 1 -or $tests.Passed -ne $tests.Total -or $tests.Failed -ne 0) { throw 'Test evidence does not show a complete passing suite.' }
    [pscustomobject]@{
        Package = $packageDirectory; Archive = $archive; ArchiveSHA256 = (Get-FileHash -LiteralPath $archive).Hash
        Tests = $tests; ExactPublishedStartupSeconds = 4; ExtractedFilesVerified = $files.Count
        LiveGameplayCoexistenceRetested = $false
    } | ConvertTo-Json -Depth 5 | Tee-Object -FilePath (Join-Path $projectRoot 'artifacts\guide-scoring-package-verification.json')
}
finally { $env:YFM_COMPANION_SETTINGS_DIRECTORY = $previousSettings; Pop-Location }
