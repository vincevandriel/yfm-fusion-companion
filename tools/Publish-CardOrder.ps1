param([Parameter(Mandatory = $true)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$publishDirectory = Join-Path $projectRoot 'artifacts\yfm-companion-v2-card-order'
$renderDirectory = Join-Path $projectRoot 'artifacts\ui-render-card-order'
$settingsDirectory = Join-Path $projectRoot 'artifacts\card-order-ui-settings'
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
    foreach ($fileName in @('guardian_star_rules.json', 'opponent_reference.json', 'optimizer_policy.json', 'source_manifest.json', 'fan_deck_sources.json')) {
        $file = Get-Item -LiteralPath (Join-Path $projectRoot "docs\research\data\$fileName")
        $published = Join-Path $publishDirectory "ResearchData\$($file.Name)"
        if ((Get-FileHash -LiteralPath $published).Hash -ne (Get-FileHash -LiteralPath $file.FullName).Hash) {
            throw "Published research data mismatch: $($file.Name)"
        }
    }
    $artworkManifest = Get-Content -LiteralPath (Join-Path $projectRoot 'assets\card-artwork\manifest.json') -Raw | ConvertFrom-Json
    foreach ($card in $artworkManifest.Cards) {
        $image = Join-Path $publishDirectory "Artwork\$($card.Path)"
        if ((Get-FileHash -LiteralPath $image).Hash -ne $card.SHA256) { throw "Published artwork mismatch: $($card.CardId)" }
    }
    if ($artworkManifest.Cards.Count -ne 722) { throw 'Incomplete bundled artwork.' }
    $startupSettings = Join-Path $projectRoot 'artifacts\card-order-startup-settings'
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
    $name = 'YFM-Fusion-Companion-2.0-CardOrder-' + (Get-Date -Format 'yyyyMMdd-HHmmss')
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
    Copy-Item -LiteralPath (Join-Path $projectRoot 'THIRD_PARTY_NOTICES.md') -Destination $packageDirectory
    Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination $packageDirectory
    Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\research\FAN_DECK_RESEARCH.md') -Destination $packageDirectory
    Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\benchmarks\campaign-deck-library.json') -Destination $packageDirectory
    Copy-Item -LiteralPath (Join-Path $projectRoot 'src\YfmCompanion.Engine\Research\campaign-decks.json') -Destination $packageDirectory
    Copy-Item -LiteralPath (Join-Path $renderDirectory 'deck-library-ui-audit.json') -Destination $packageDirectory
    Copy-Item -LiteralPath (Join-Path $renderDirectory 'suggested-deck-sorting.json') -Destination $packageDirectory
    Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\benchmarks\CAMPAIGN_DECK_RESULTS.md') -Destination $packageDirectory
    @'
YFM Fusion Companion 2.0 - suggested-card ordering and campaign deck library

Extract the complete folder and double-click YFM Fusion Companion.exe.
Keep Artwork, Data and ResearchData beside it. The .NET runtime is included.

Open Owned-Card Optimizer and use CARD ORDER above the suggested deck.
Alphabetical (A-Z) is the default. Card number shows the lowest first;
ATK and DEF show the highest first. Your choice is remembered across
new results and restarts. This changes the display, not deck calculations.

Open Owned-Card Optimizer, then RECOMMENDED DECKS. Six distinct card-artwork
icons show the required copies already owned out of 40 below each button.
Select an icon for missing cards, progression advice, tradeoffs and sources.
CHECK ALL OPENING HANDS analyzes the full reference deck, including missing
cards, over 658,008 physical five-card hands. It can be cancelled.
ADAPT THIS STRATEGY TO MY COLLECTION selects an owned-card search start.
Then BUILD DECK. Missing cards are replaced; other strategies are still
compared, so the final best-found result can differ from the reference list.

Balanced/control scoring includes strong natural bodies, strictly >4,500 ATK
setups, board-clear coverage and support-only draw risk. Setup availability
may require separate turns and is not a duel win probability. See
FAN_DECK_RESEARCH.md for research, weights and limitations.

All 722 card images work offline. Compact Live retains two-line names,
no horizontal scrolling and 20px guardian symbols. Auto uses up to four
search workers and eight analysis workers, reduced on smaller CPUs.
Old running copies keep their old code; restart using this executable.

New scoring uses a new proof identity. Keep previous proof checkpoints and
use the existing New proof checkpoint option to begin a fresh proof.
Game and save access remains read-only.
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
    $trx = [xml](Get-Content -LiteralPath (Join-Path $projectRoot 'artifacts\card-order-tests\card-order.trx') -Raw)
    $counters = $trx.SelectSingleNode("//*[local-name()='ResultSummary']/*[local-name()='Counters']")
    $tests = [pscustomobject]@{ Total = [int]$counters.GetAttribute('total'); Passed = [int]$counters.GetAttribute('passed'); Failed = [int]$counters.GetAttribute('failed') }
    if ($tests.Total -lt 1 -or $tests.Passed -ne $tests.Total -or $tests.Failed -ne 0) { throw 'Test evidence does not show a complete passing suite.' }
    [pscustomobject]@{
        Package = $packageDirectory; Archive = $archive; ArchiveSHA256 = (Get-FileHash -LiteralPath $archive).Hash
        Tests = $tests; ExactPublishedStartupSeconds = 4; ExtractedFilesVerified = $files.Count
        AutomaticArtworkCardsVerified = $artworkManifest.Cards.Count
        DeckLibrary = Get-Content -LiteralPath (Join-Path $renderDirectory 'deck-library-ui-audit.json') -Raw | ConvertFrom-Json
        SuggestedDeckOrdering = Get-Content -LiteralPath (Join-Path $renderDirectory 'suggested-deck-sorting.json') -Raw | ConvertFrom-Json
        LiveGameplayCoexistenceRetested = $false
    } | ConvertTo-Json -Depth 5 | Tee-Object -FilePath (Join-Path $projectRoot 'artifacts\card-order-package-verification.json')
}
finally { $env:YFM_COMPANION_SETTINGS_DIRECTORY = $previousSettings; Pop-Location }
