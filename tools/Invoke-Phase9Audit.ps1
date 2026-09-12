[CmdletBinding()]
param(
    [Parameter()]
    [string] $SourceSqlPath,

    [Parameter()]
    [string] $SavePath,

    [Parameter()]
    [string] $RetroArchConfigPath,

    [Parameter()]
    [string] $ResultsRoot,

    [Parameter()]
    [string] $FinalDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
Add-Type -AssemblyName System.IO.Compression.FileSystem

$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$workspaceRoot = Split-Path (Split-Path $projectRoot -Parent) -Parent
if ([string]::IsNullOrWhiteSpace($SourceSqlPath)) {
    $SourceSqlPath = Join-Path $projectRoot "database-source\YuGiOh_Forbidden_Memories_PostgreSQL.sql"
}
if ([string]::IsNullOrWhiteSpace($ResultsRoot)) {
    $ResultsRoot = Join-Path $projectRoot "artifacts\phase9-runs"
}
if ([string]::IsNullOrWhiteSpace($FinalDirectory)) {
    $FinalDirectory = Join-Path $workspaceRoot "outputs\YFM-Fusion-Companion-FINAL"
}

$ResultsRoot = [System.IO.Path]::GetFullPath($ResultsRoot)
$FinalDirectory = [System.IO.Path]::GetFullPath($FinalDirectory)
$finalArchive = "$FinalDirectory.zip"
$runStamp = Get-Date -Format "yyyyMMdd-HHmmss"
$runDirectory = Join-Path $ResultsRoot $runStamp
$logsDirectory = Join-Path $runDirectory "logs"
$testDirectory = Join-Path $runDirectory "tests"
$integrationDirectory = Join-Path $runDirectory "integration"
$uiDirectory = Join-Path $runDirectory "ui"
$publishDirectory = Join-Path $runDirectory "publish"
$finalStagingDirectory = Join-Path $runDirectory "final-staging"
$rebuiltDatabasePath = Join-Path $runDirectory "rebuilt-yfm.db"
$solutionPath = Join-Path $projectRoot "YfmFusionCompanion.sln"
$canonicalDatabasePath = Join-Path $projectRoot "artifacts\yfm.db"
$desktopProjectPath = Join-Path $projectRoot "src\YfmCompanion.Desktop\YfmCompanion.Desktop.csproj"
$auditProjectPath = Join-Path $projectRoot "tools\YfmCompanion.AuditRunner\YfmCompanion.AuditRunner.csproj"
$dataBuilderProjectPath = Join-Path $projectRoot "tools\YfmCompanion.DataBuilder\YfmCompanion.DataBuilder.csproj"
$uiRenderProjectPath = Join-Path $projectRoot "tools\YfmCompanion.UiRender\YfmCompanion.UiRender.csproj"
$steps = [System.Collections.Generic.List[object]]::new()

function Assert-ExistingFile {
    param(
        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [string] $Description
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Description was not found: $Path"
    }
}

function Invoke-NativeCommand {
    param(
        [Parameter(Mandatory)]
        [string] $FilePath,

        [Parameter(Mandatory)]
        [string[]] $Arguments,

        [Parameter(Mandatory)]
        [string] $LogName
    )

    $logPath = Join-Path $logsDirectory $LogName
    & $FilePath @Arguments 2>&1 | Tee-Object -LiteralPath $logPath | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "$FilePath exited with code $LASTEXITCODE. See $logPath"
    }
}

function Invoke-AuditStep {
    param(
        [Parameter(Mandatory)]
        [string] $Phase,

        [Parameter(Mandatory)]
        [string] $Name,

        [Parameter(Mandatory)]
        [scriptblock] $Action
    )

    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        $detail = & $Action
        $stopwatch.Stop()
        $steps.Add([pscustomobject]@{
                Phase = $Phase
                Name = $Name
                Outcome = "Pass"
                DurationSeconds = [Math]::Round($stopwatch.Elapsed.TotalSeconds, 3)
                Detail = [string] $detail
            })
        Write-Host "PASS [$Phase] $Name"
    }
    catch {
        $stopwatch.Stop()
        $steps.Add([pscustomobject]@{
                Phase = $Phase
                Name = $Name
                Outcome = "Fail"
                DurationSeconds = [Math]::Round($stopwatch.Elapsed.TotalSeconds, 3)
                Detail = $_.Exception.Message
            })
        Write-AuditReports -Passed $false
        throw
    }
}

function Write-AuditReports {
    param(
        [Parameter(Mandatory)]
        [bool] $Passed
    )

    $jsonPath = Join-Path $runDirectory "Phase-9-Final-Audit.json"
    $markdownPath = Join-Path $runDirectory "Phase-9-Final-Audit.md"
    $report = [ordered]@{
        GeneratedAt = (Get-Date).ToString("o")
        Passed = $Passed
        ProjectRoot = $projectRoot
        SourceSqlPath = $SourceSqlPath
        SavePath = $SavePath
        RetroArchConfigPath = $RetroArchConfigPath
        ResultsDirectory = $runDirectory
        FinalDirectory = $FinalDirectory
        Steps = $steps
    }
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $jsonPath -Encoding utf8

    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add("# Phase 9 final audit")
    $lines.Add("")
    $lines.Add("Overall result: **$(if ($Passed) { 'PASS' } else { 'FAIL' })**")
    $lines.Add("")
    $lines.Add("Generated: $($report.GeneratedAt)")
    $lines.Add("")
    $lines.Add("This report was generated by the one-command audit. Each check below is executable and fail-closed; a failed check stops packaging.")
    $lines.Add("")
    $lines.Add("| Phase | Check | Result | Seconds | Detail |")
    $lines.Add("|---|---|---:|---:|---|")
    foreach ($step in $steps) {
        $safeDetail = ([string] $step.Detail).Replace("|", "\|").Replace("`r", " ").Replace("`n", " ")
        $lines.Add("| $($step.Phase) | $($step.Name) | $($step.Outcome) | $($step.DurationSeconds) | $safeDetail |")
    }
    $lines.Add("")
    $lines.Add("The application is read-only with respect to RetroArch, game RAM, memory-card saves, and RetroArch configuration. Live verification uses status and memory-read requests only.")
    $lines | Set-Content -LiteralPath $markdownPath -Encoding utf8
}

function Get-SafeRelativePath {
    param(
        [Parameter(Mandatory)]
        [string] $BasePath,

        [Parameter(Mandatory)]
        [string] $TargetPath
    )

    $separator = [System.IO.Path]::DirectorySeparatorChar.ToString()
    $baseFullPath = [System.IO.Path]::GetFullPath($BasePath)
    if (-not $baseFullPath.EndsWith($separator, [System.StringComparison]::Ordinal)) {
        $baseFullPath += $separator
    }
    $targetFullPath = [System.IO.Path]::GetFullPath($TargetPath)
    if (-not $targetFullPath.StartsWith($baseFullPath, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Path is outside the expected base directory: $targetFullPath"
    }
    return $targetFullPath.Substring($baseFullPath.Length)
}

function Copy-FilePreservingRelativePath {
    param(
        [Parameter(Mandatory)]
        [System.IO.FileInfo] $File,

        [Parameter(Mandatory)]
        [string] $BasePath,

        [Parameter(Mandatory)]
        [string] $DestinationPath
    )

    $relative = Get-SafeRelativePath -BasePath $BasePath -TargetPath $File.FullName
    $destinationFile = Join-Path $DestinationPath $relative
    $destinationParent = Split-Path $destinationFile -Parent
    [System.IO.Directory]::CreateDirectory($destinationParent) | Out-Null
    Copy-Item -LiteralPath $File.FullName -Destination $destinationFile
}

function Get-RelativeFileHashes {
    param(
        [Parameter(Mandatory)]
        [string] $Directory
    )

    $hashes = @{}
    foreach ($file in Get-ChildItem -LiteralPath $Directory -Recurse -File | Sort-Object FullName) {
        $relative = (Get-SafeRelativePath -BasePath $Directory -TargetPath $file.FullName).Replace('\', '/')
        $hashes[$relative] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    }
    return $hashes
}

function Assert-MatchingTrees {
    param(
        [Parameter(Mandatory)]
        [string] $ExpectedDirectory,

        [Parameter(Mandatory)]
        [string] $ActualDirectory
    )

    $expected = Get-RelativeFileHashes -Directory $ExpectedDirectory
    $actual = Get-RelativeFileHashes -Directory $ActualDirectory
    $expectedNames = @($expected.Keys | Sort-Object)
    $actualNames = @($actual.Keys | Sort-Object)
    if (Compare-Object -ReferenceObject $expectedNames -DifferenceObject $actualNames) {
        throw "Archive contents do not have the same file list as the final directory."
    }
    foreach ($name in $expectedNames) {
        if ($expected[$name] -ne $actual[$name]) {
            throw "Archive hash mismatch for $name."
        }
    }
}

Push-Location $projectRoot
try {
    [System.IO.Directory]::CreateDirectory($logsDirectory) | Out-Null
    [System.IO.Directory]::CreateDirectory($testDirectory) | Out-Null
    Assert-ExistingFile -Path $solutionPath -Description "Solution"
    Assert-ExistingFile -Path $canonicalDatabasePath -Description "Canonical SQLite database"
    Assert-ExistingFile -Path $SourceSqlPath -Description "Supplied PostgreSQL source database"
    if (Test-Path -LiteralPath $FinalDirectory) {
        throw "The final directory already exists; this fail-closed audit will not overwrite it: $FinalDirectory"
    }
    if (Test-Path -LiteralPath $finalArchive) {
        throw "The final archive already exists; this fail-closed audit will not overwrite it: $finalArchive"
    }

    Invoke-AuditStep -Phase "9A" -Name "Restore the complete solution" -Action {
        Invoke-NativeCommand -FilePath "dotnet" -Arguments @("restore", $solutionPath, "--locked-mode") -LogName "01-restore.log"
        "All nine projects restored from the checked-in dependency graph."
    }

    Invoke-AuditStep -Phase "9A" -Name "Verify formatting, analyzers, and whitespace" -Action {
        Invoke-NativeCommand -FilePath "dotnet" -Arguments @("format", $solutionPath, "--verify-no-changes", "--severity", "info", "--no-restore") -LogName "02-format.log"
        Invoke-NativeCommand -FilePath "dotnet" -Arguments @("format", $solutionPath, "analyzers", "--verify-no-changes", "--severity", "info", "--no-restore") -LogName "03-analyzers.log"
        Invoke-NativeCommand -FilePath "dotnet" -Arguments @("format", $solutionPath, "whitespace", "--verify-no-changes", "--no-restore") -LogName "04-whitespace.log"
        "Formatting, whitespace, and all configured analyzer diagnostics passed at informational severity."
    }

    Invoke-AuditStep -Phase "9A" -Name "Inspect architecture and prohibited implementation patterns" -Action {
        $codeFiles = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot "src"), (Join-Path $projectRoot "tests"), (Join-Path $projectRoot "tools") -Recurse -File |
                Where-Object { $_.FullName -notmatch "[\\/](bin|obj)[\\/]" -and $_.Name -ne "Invoke-Phase9Audit.ps1" })
        $markerMatches = @($codeFiles | Select-String -Pattern "TODO|FIXME|HACK|NotImplementedException")
        if ($markerMatches.Count -ne 0) {
            throw "Found $($markerMatches.Count) unfinished-code marker(s)."
        }

        $productionFiles = @($codeFiles | Where-Object { $_.FullName -notmatch "[\\/]tests[\\/]" })
        $sqliteMatches = @($productionFiles | Select-String -Pattern "\bSqliteConnection\b")
        $outsideDataLayer = @($sqliteMatches | Where-Object { $_.Path -notmatch "[\\/]src[\\/]YfmCompanion\.Data[\\/]" })
        if ($sqliteMatches.Count -eq 0 -or $outsideDataLayer.Count -ne 0) {
            throw "Runtime SQLite access is absent or exists outside the shared data layer."
        }

        $networkMatches = @($productionFiles | Select-String -Pattern "HttpClient|WebClient|TelemetryClient|ApplicationInsights|SentrySdk")
        if ($networkMatches.Count -ne 0) {
            throw "Found an Internet or telemetry implementation in production code."
        }

        $unsafeRetroArchMatches = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot "src\YfmCompanion.RetroArch") -Recurse -File |
                Where-Object { $_.FullName -notmatch "[\\/](bin|obj)[\\/]" } |
                Select-String -Pattern "WRITE_CORE_MEMORY|WRITE_CORE_RAM|CHEAT|RETROPAD|stdin")
        if ($unsafeRetroArchMatches.Count -ne 0) {
            throw "Found a prohibited RetroArch write, cheat, or input command surface."
        }
        "$($codeFiles.Count) source files inspected; SQLite is confined to the shared data layer; no unfinished markers, Internet client, telemetry, input, cheat, or memory-write implementation found."
    }

    Invoke-AuditStep -Phase "9A" -Name "Build Release with warnings as errors" -Action {
        Invoke-NativeCommand -FilePath "dotnet" -Arguments @("build", $solutionPath, "--configuration", "Release", "--no-restore", "-warnaserror") -LogName "05-build.log"
        "The full nine-project solution compiled cleanly with warnings promoted to errors."
    }

    Invoke-AuditStep -Phase "9A" -Name "Scan all direct and transitive packages for known vulnerabilities" -Action {
        $vulnerabilityPath = Join-Path $runDirectory "dependency-vulnerabilities.json"
        $output = (& dotnet list $solutionPath package --vulnerable --include-transitive --format json 2>&1 | Out-String)
        $output | Set-Content -LiteralPath $vulnerabilityPath -Encoding utf8
        if ($LASTEXITCODE -ne 0) {
            throw "The dependency vulnerability scanner exited with code $LASTEXITCODE."
        }
        if ($output -match '"vulnerabilities"\s*:') {
            throw "One or more vulnerable direct or transitive packages remain."
        }
        "No known vulnerable direct or transitive NuGet packages were reported."
    }

    Invoke-AuditStep -Phase "9B" -Name "Run the complete automated test suite with coverage collection" -Action {
        $previousSqlPath = $env:YFM_SQL_PATH
        try {
            $env:YFM_SQL_PATH = $SourceSqlPath
            Invoke-NativeCommand -FilePath "dotnet" -Arguments @(
                "test", $solutionPath,
                "--configuration", "Release",
                "--no-build",
                "--logger", "trx;LogFileName=phase9-tests.trx",
                "--results-directory", $testDirectory,
                "--collect", "Code Coverage"
            ) -LogName "06-tests.log"
        }
        finally {
            $env:YFM_SQL_PATH = $previousSqlPath
        }
        $trx = Get-ChildItem -LiteralPath $testDirectory -Recurse -Filter "phase9-tests.trx" | Select-Object -First 1
        $coverage = Get-ChildItem -LiteralPath $testDirectory -Recurse -Filter "*.coverage" | Select-Object -First 1
        if ($null -eq $trx -or $null -eq $coverage -or $coverage.Length -le 0) {
            throw "Test result or binary coverage evidence was not produced."
        }
        [xml] $trxDocument = Get-Content -LiteralPath $trx.FullName -Raw
        $counters = Select-Xml -Xml $trxDocument -XPath "//*[local-name()='Counters']" | Select-Object -First 1
        $passedTests = [int] $counters.Node.passed
        $failedTests = [int] $counters.Node.failed
        if ($failedTests -ne 0 -or $passedTests -lt 1) {
            throw "The test result did not contain a clean passing test count."
        }
        "$passedTests tests passed; binary code-coverage evidence was captured."
    }

    Invoke-AuditStep -Phase "9B" -Name "Rebuild the SQLite catalog from the supplied SQL and compare it" -Action {
        Invoke-NativeCommand -FilePath "dotnet" -Arguments @(
            "run", "--project", $dataBuilderProjectPath,
            "--configuration", "Release", "--no-build", "--",
            $SourceSqlPath, $rebuiltDatabasePath
        ) -LogName "07-database-rebuild.log"
        Assert-ExistingFile -Path $rebuiltDatabasePath -Description "Rebuilt SQLite database"
        $canonicalHash = (Get-FileHash -LiteralPath $canonicalDatabasePath -Algorithm SHA256).Hash
        $rebuiltHash = (Get-FileHash -LiteralPath $rebuiltDatabasePath -Algorithm SHA256).Hash
        if ($canonicalHash -ne $rebuiltHash) {
            throw "The rebuilt SQLite database is not byte-for-byte identical to the canonical runtime database."
        }
        "The supplied SQL reproduced the canonical database exactly (SHA-256 $canonicalHash)."
    }

    Invoke-AuditStep -Phase "9C" -Name "Exercise database, fusion, planner, analyzer, optimizer, save, and live integration" -Action {
        [System.IO.Directory]::CreateDirectory($integrationDirectory) | Out-Null
        $runnerArguments = @(
            "run", "--project", $auditProjectPath,
            "--configuration", "Release", "--no-build", "--",
            $canonicalDatabasePath, $integrationDirectory
        )
        if ((Test-Path -LiteralPath $SavePath -PathType Leaf) -or (Test-Path -LiteralPath $RetroArchConfigPath -PathType Leaf)) {
            $runnerArguments += $(if (Test-Path -LiteralPath $SavePath -PathType Leaf) { $SavePath } else { "" })
        }
        if (Test-Path -LiteralPath $RetroArchConfigPath -PathType Leaf) {
            $runnerArguments += $RetroArchConfigPath
        }
        Invoke-NativeCommand -FilePath "dotnet" -Arguments $runnerArguments -LogName "08-integration.log"
        $integrationReportPath = Join-Path $integrationDirectory "integration-audit.json"
        Assert-ExistingFile -Path $integrationReportPath -Description "Integration audit report"
        $integrationReport = Get-Content -LiteralPath $integrationReportPath -Raw | ConvertFrom-Json
        if (-not $integrationReport.passed) {
            throw "The end-to-end integration runner reported failure."
        }
        $failedChecks = @($integrationReport.checks | Where-Object { $_.outcome -eq "Fail" })
        if ($failedChecks.Count -ne 0) {
            throw "The integration report contains $($failedChecks.Count) failed check(s)."
        }
        $passedIntegrationChecks = @($integrationReport.checks | Where-Object { $_.outcome -eq "Pass" }).Count
        $skippedIntegrationChecks = @($integrationReport.checks | Where-Object { $_.outcome -eq "Skip" }).Count
        "$passedIntegrationChecks integration checks passed; $skippedIntegrationChecks environment-dependent checks skipped."
    }

    Invoke-AuditStep -Phase "9C" -Name "Initialize and render the full and compact desktop interfaces" -Action {
        [System.IO.Directory]::CreateDirectory($uiDirectory) | Out-Null
        Invoke-NativeCommand -FilePath "dotnet" -Arguments @(
            "run", "--project", $uiRenderProjectPath,
            "--configuration", "Release", "--no-build", "--",
            $uiDirectory
        ) -LogName "09-ui-render.log"
        $uiReportPath = Join-Path $uiDirectory "ui-render-audit.json"
        Assert-ExistingFile -Path $uiReportPath -Description "UI render audit report"
        $renderReport = @(Get-Content -LiteralPath $uiReportPath -Raw | ConvertFrom-Json)
        $requiredRenders = @("normal.png", "deck-analyzer.png", "live-duel.png", "live-duel-inspector.png", "save-snapshot.png", "owned-card-optimizer.png", "compact.png")
        $renderedFiles = @($renderReport | ForEach-Object { $_.File })
        $missingRenders = @($requiredRenders | Where-Object { $_ -notin $renderedFiles })
        $invalidRenders = @($renderReport | Where-Object {
                $_.VisiblePixels -lt 1 -or $_.BrightPixels -lt 500 -or $_.DistinctColors -lt 32
            })
        if ($renderedFiles.Count -ne $requiredRenders.Count -or
            $missingRenders.Count -ne 0 -or
            $invalidRenders.Count -ne 0) {
            $missingDetail = if ($missingRenders.Count -eq 0) { "none" } else { $missingRenders -join ", " }
            $invalidDetail = if ($invalidRenders.Count -eq 0) { "none" } else { @($invalidRenders | ForEach-Object { $_.File }) -join ", " }
            throw "UI render verification failed: expected $($requiredRenders.Count), found $($renderedFiles.Count), missing [$missingDetail], invalid [$invalidDetail]."
        }
        "All five full-workspace tabs, both closed and open Live Duel inspector states, and the compact live view initialized and rendered with visible content and the enforced blue/white control palette."
    }

    Invoke-AuditStep -Phase "9D" -Name "Publish and smoke-start the self-contained Windows application" -Action {
        Invoke-NativeCommand -FilePath "dotnet" -Arguments @(
            "publish", $desktopProjectPath,
            "--configuration", "Release", "--no-build",
            "--runtime", "win-x64", "--self-contained", "true",
            "--output", $publishDirectory
        ) -LogName "10-publish.log"
        $publishedExecutable = Join-Path $publishDirectory "YFM Fusion Companion.exe"
        $publishedDatabase = Join-Path $publishDirectory "Data\yfm.db"
        Assert-ExistingFile -Path $publishedExecutable -Description "Published application"
        Assert-ExistingFile -Path $publishedDatabase -Description "Published runtime database"
        if ((Get-FileHash -LiteralPath $publishedDatabase -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $canonicalDatabasePath -Algorithm SHA256).Hash) {
            throw "The published runtime database differs from the audited canonical database."
        }
        $unexpectedCultureDirectories = @(Get-ChildItem -LiteralPath $publishDirectory -Directory | Where-Object { $_.Name -ne "Data" })
        if ($unexpectedCultureDirectories.Count -ne 0) {
            throw "The English-only publish unexpectedly contains satellite/culture directories."
        }

        $smokeLocalData = Join-Path $runDirectory "smoke-local-app-data"
        [System.IO.Directory]::CreateDirectory($smokeLocalData) | Out-Null
        $previousLocalData = $env:LOCALAPPDATA
        $process = $null
        try {
            $env:LOCALAPPDATA = $smokeLocalData
            $process = Start-Process -FilePath $publishedExecutable -WorkingDirectory $publishDirectory -WindowStyle Hidden -PassThru
            Start-Sleep -Seconds 4
            if ($process.HasExited) {
                throw "The published application exited during its four-second startup smoke test with code $($process.ExitCode)."
            }
        }
        finally {
            $env:LOCALAPPDATA = $previousLocalData
            if ($null -ne $process -and -not $process.HasExited) {
                $null = $process.CloseMainWindow()
                if (-not $process.WaitForExit(3000)) {
                    Stop-Process -Id $process.Id
                }
            }
        }
        "The exact published executable stayed healthy through a four-second isolated startup smoke test; publish contains only the application and its Data folder."
    }

    Invoke-AuditStep -Phase "9D" -Name "Assemble the canonical minimal final directory" -Action {
        $runOutput = Join-Path $finalStagingDirectory "Run"
        $documentationOutput = Join-Path $runOutput "Documentation"
        $sourceOutput = Join-Path $finalStagingDirectory "Source"
        $auditOutput = Join-Path $finalStagingDirectory "Audit"
        [System.IO.Directory]::CreateDirectory($runOutput) | Out-Null
        [System.IO.Directory]::CreateDirectory($documentationOutput) | Out-Null
        [System.IO.Directory]::CreateDirectory($sourceOutput) | Out-Null
        [System.IO.Directory]::CreateDirectory($auditOutput) | Out-Null
        foreach ($publishedItem in Get-ChildItem -LiteralPath $publishDirectory -Force) {
            Copy-Item -LiteralPath $publishedItem.FullName -Destination $runOutput -Recurse
        }
        Copy-Item -LiteralPath (Join-Path $projectRoot "README.md") -Destination $documentationOutput
        Copy-Item -LiteralPath (Join-Path $projectRoot "docs\SETUP.md") -Destination $documentationOutput
        Copy-Item -LiteralPath (Join-Path $projectRoot "docs\LIMITATIONS.md") -Destination $documentationOutput
        $strategyResearch = Join-Path $workspaceRoot "outputs\YFM-Forbidden-Memories-Deck-Strategy-Research.md"
        if (Test-Path -LiteralPath $strategyResearch -PathType Leaf) {
            Copy-Item -LiteralPath $strategyResearch -Destination $documentationOutput
        }

        $sourceStaging = Join-Path $runDirectory "source-staging"
        [System.IO.Directory]::CreateDirectory($sourceStaging) | Out-Null
        foreach ($rootFileName in @(".gitignore", "Directory.Build.props", "NuGet.config", "README.md", "YfmFusionCompanion.sln")) {
            Copy-Item -LiteralPath (Join-Path $projectRoot $rootFileName) -Destination $sourceStaging
        }
        foreach ($sourceFolderName in @("docs", "src", "tests", "tools")) {
            $sourceFolder = Join-Path $projectRoot $sourceFolderName
            foreach ($file in Get-ChildItem -LiteralPath $sourceFolder -Recurse -File | Where-Object { $_.FullName -notmatch "[\\/](bin|obj)[\\/]" }) {
                Copy-FilePreservingRelativePath -File $file -BasePath $projectRoot -DestinationPath $sourceStaging
            }
        }
        $sourceArtifacts = Join-Path $sourceStaging "artifacts"
        [System.IO.Directory]::CreateDirectory($sourceArtifacts) | Out-Null
        Copy-Item -LiteralPath $canonicalDatabasePath -Destination (Join-Path $sourceArtifacts "yfm.db")
        $databaseSourceDirectory = Join-Path $sourceStaging "database-source"
        [System.IO.Directory]::CreateDirectory($databaseSourceDirectory) | Out-Null
        Copy-Item -LiteralPath $SourceSqlPath -Destination (Join-Path $databaseSourceDirectory (Split-Path $SourceSqlPath -Leaf))
        $sourceArchive = Join-Path $sourceOutput "YFM-Fusion-Companion-Source.zip"
        [System.IO.Compression.ZipFile]::CreateFromDirectory($sourceStaging, $sourceArchive, [System.IO.Compression.CompressionLevel]::Optimal, $false)

        foreach ($integrationItem in Get-ChildItem -LiteralPath $integrationDirectory -Force) {
            Copy-Item -LiteralPath $integrationItem.FullName -Destination $auditOutput -Recurse
        }
        foreach ($uiItem in Get-ChildItem -LiteralPath $uiDirectory -Force) {
            Copy-Item -LiteralPath $uiItem.FullName -Destination $auditOutput -Recurse
        }
        Copy-Item -LiteralPath (Join-Path $runDirectory "dependency-vulnerabilities.json") -Destination $auditOutput
        $trxEvidence = Get-ChildItem -LiteralPath $testDirectory -Recurse -Filter "phase9-tests.trx" | Select-Object -First 1
        $coverageEvidence = Get-ChildItem -LiteralPath $testDirectory -Recurse -Filter "*.coverage" | Select-Object -First 1
        Copy-Item -LiteralPath $trxEvidence.FullName -Destination (Join-Path $auditOutput "phase9-tests.trx")
        Copy-Item -LiteralPath $coverageEvidence.FullName -Destination (Join-Path $auditOutput "phase9-code-coverage.coverage")

        $keepGuide = @'
# Keep this folder

This is the canonical final YFM Fusion Companion release.

- Start the program with `Run\YFM Fusion Companion.exe`.
- Keep the `Run\Data` folder beside the executable.
- `Run\Documentation` contains setup, limitations, and strategy guidance.
- `Source` contains one reproducible source archive, including the supplied SQL and audited SQLite database.
- `Audit` contains machine-readable and human-readable Phase 9 evidence.

## What may be deleted after a successful launch

In the sibling `outputs` folder, every older `YFM-Fusion-Companion-Phase-*` file or folder, every loose Phase validation/checkpoint document, and the loose strategy-research and equip-rule documents are superseded. The final folder already contains the current application, documentation, research, source, SQL data, runtime database, and audit evidence.

The development folder at `work\yfm-fusion-companion` is also optional after delivery. Keep it only if you plan to edit or rebuild the program; the final `Source` archive is the reproducible backup.

For normal use, keep this extracted final folder. The sibling `YFM-Fusion-Companion-FINAL.zip` is an optional portable backup, and its `.verification.txt` sidecar records the independently checked archive hash.

The audit deliberately did not delete any older copy automatically.
'@
        $keepGuide | Set-Content -LiteralPath (Join-Path $finalStagingDirectory "KEEP-THIS-FOLDER.md") -Encoding utf8
        "Canonical Run, Source, and Audit folders assembled without copying build caches, user saves, or RetroArch configuration."
    }

    Write-AuditReports -Passed $true
    Copy-Item -LiteralPath (Join-Path $runDirectory "Phase-9-Final-Audit.json") -Destination (Join-Path $finalStagingDirectory "Audit")
    Copy-Item -LiteralPath (Join-Path $runDirectory "Phase-9-Final-Audit.md") -Destination (Join-Path $finalStagingDirectory "Audit")

    Invoke-AuditStep -Phase "9D" -Name "Generate hashes and preflight every packaged file" -Action {
        $manifestPath = Join-Path $finalStagingDirectory "SHA256SUMS.txt"
        $manifestLines = foreach ($file in Get-ChildItem -LiteralPath $finalStagingDirectory -Recurse -File | Where-Object { $_.FullName -ne $manifestPath } | Sort-Object FullName) {
            $relative = (Get-SafeRelativePath -BasePath $finalStagingDirectory -TargetPath $file.FullName).Replace('\', '/')
            "$((Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash)  $relative"
        }
        $manifestLines | Set-Content -LiteralPath $manifestPath -Encoding ascii
        $preflightArchive = Join-Path $runDirectory "preflight-final.zip"
        [System.IO.Compression.ZipFile]::CreateFromDirectory($finalStagingDirectory, $preflightArchive, [System.IO.Compression.CompressionLevel]::Optimal, $false)
        $preflightVerificationDirectory = Join-Path $runDirectory "preflight-archive-verification"
        [System.IO.Compression.ZipFile]::ExtractToDirectory($preflightArchive, $preflightVerificationDirectory)
        Assert-MatchingTrees -ExpectedDirectory $finalStagingDirectory -ActualDirectory $preflightVerificationDirectory
        "All files passed SHA-256 generation and an independently extracted preflight archive matched the staged release byte-for-byte."
    }

    Write-AuditReports -Passed $true
    Copy-Item -LiteralPath (Join-Path $runDirectory "Phase-9-Final-Audit.json") -Destination (Join-Path $finalStagingDirectory "Audit\Phase-9-Final-Audit.json") -Force
    Copy-Item -LiteralPath (Join-Path $runDirectory "Phase-9-Final-Audit.md") -Destination (Join-Path $finalStagingDirectory "Audit\Phase-9-Final-Audit.md") -Force

    $manifestPath = Join-Path $finalStagingDirectory "SHA256SUMS.txt"
    $manifestLines = foreach ($file in Get-ChildItem -LiteralPath $finalStagingDirectory -Recurse -File | Where-Object { $_.FullName -ne $manifestPath } | Sort-Object FullName) {
        $relative = (Get-SafeRelativePath -BasePath $finalStagingDirectory -TargetPath $file.FullName).Replace('\', '/')
        "$((Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash)  $relative"
    }
    $manifestLines | Set-Content -LiteralPath $manifestPath -Encoding ascii
    $readyArchive = Join-Path $runDirectory "verified-final.zip"
    [System.IO.Compression.ZipFile]::CreateFromDirectory($finalStagingDirectory, $readyArchive, [System.IO.Compression.CompressionLevel]::Optimal, $false)
    $finalVerificationDirectory = Join-Path $runDirectory "final-archive-verification"
    [System.IO.Compression.ZipFile]::ExtractToDirectory($readyArchive, $finalVerificationDirectory)
    Assert-MatchingTrees -ExpectedDirectory $finalStagingDirectory -ActualDirectory $finalVerificationDirectory
    Move-Item -LiteralPath $finalStagingDirectory -Destination $FinalDirectory
    Move-Item -LiteralPath $readyArchive -Destination $finalArchive
    $archiveHash = (Get-FileHash -LiteralPath $finalArchive -Algorithm SHA256).Hash
    @(
        "PASS",
        "Verified: $((Get-Date).ToString('o'))",
        "Archive SHA-256: $archiveHash",
        "Method: independently extracted every file and compared the complete relative file list and each SHA-256 hash to the canonical final directory."
    ) | Set-Content -LiteralPath "$finalArchive.verification.txt" -Encoding utf8

    $finalAuditHash = (Get-FileHash -LiteralPath (Join-Path $FinalDirectory "Audit\Phase-9-Final-Audit.json") -Algorithm SHA256).Hash
    Write-Host ""
    Write-Host "PHASE 9 FINAL AUDIT: PASS"
    Write-Host "Final directory: $FinalDirectory"
    Write-Host "Final archive:   $finalArchive"
    Write-Host "Archive hash:    $archiveHash"
    Write-Host "Audit JSON hash: $finalAuditHash"
}
finally {
    Pop-Location
}
