[CmdletBinding()]
param(
    [string]$OutputDirectory = 'artifacts/releases',
    [string]$EvidenceDirectory = 'artifacts/release-audit'
)

# The same fail-closed publisher runs locally and in GitHub Actions.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $projectRoot
$previousSettings = $env:YFM_COMPANION_SETTINGS_DIRECTORY
try {
    $env:MSBUILDDISABLENODEREUSE = '1'
    $outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
    $evidenceRoot = [IO.Path]::GetFullPath($EvidenceDirectory)
    $run = Join-Path $evidenceRoot (Get-Date -Format 'yyyyMMdd-HHmmss')
    [IO.Directory]::CreateDirectory($run) | Out-Null
    $env:YFM_COMPANION_SETTINGS_DIRECTORY = Join-Path $run 'isolated-settings'
    [xml]$project = Get-Content src/YfmCompanion.Desktop/YfmCompanion.Desktop.csproj -Raw
    $version = [string]$project.Project.PropertyGroup.Version
    $package = Join-Path $outputRoot "YFM-Fusion-Companion-$version"
    $archive = "$package-Windows-x64.zip"
    if ((Test-Path -LiteralPath $package) -or (Test-Path -LiteralPath $archive)) {
        throw 'Output already exists. Choose an empty output directory; verified releases are never overwritten.'
    }
    $steps = [Collections.Generic.List[string]]::new()
    function Invoke-Checked([string]$Label, [string]$Executable, [string[]]$Arguments) {
        $log = Join-Path $run "$Label.log"
        & $Executable @Arguments > $log 2>&1
        if ($LASTEXITCODE -ne 0) {
            Get-Content -LiteralPath $log -Tail 18 | Write-Host
            throw "$Label failed. Full log: $log"
        }
        $steps.Add($Label)
        Write-Host "PASS $Label"
    }
    Invoke-Checked 'restore' 'dotnet' @('restore','YfmFusionCompanion.sln','--locked-mode')
    Invoke-Checked 'format' 'dotnet' @('format','YfmFusionCompanion.sln','--verify-no-changes','--severity','warn','--no-restore')
    Invoke-Checked 'build' 'dotnet' @('build','YfmFusionCompanion.sln','-c','Release','--no-restore','-m:1','-v:q')
    Invoke-Checked 'tests' 'dotnet' @('test','YfmFusionCompanion.sln','-c','Release','--no-build','--logger','trx;LogFileName=release.trx','--results-directory',(Join-Path $run 'tests'),'-v:q')
    Invoke-Checked 'dependencies' 'dotnet' @('list','YfmFusionCompanion.sln','package','--vulnerable','--include-transitive','--format','json')
    if ((Get-Content (Join-Path $run 'dependencies.log') -Raw) -match '"vulnerabilities"\s*:') { throw 'Vulnerable dependencies were reported.' }
    Invoke-Checked 'catalog-rebuild' 'dotnet' @('run','--project','tools/YfmCompanion.DataBuilder','-c','Release','--no-build','--','database-source/YuGiOh_Forbidden_Memories_PostgreSQL.sql',(Join-Path $run 'rebuilt.db'))
    if ((Get-FileHash artifacts/yfm.db).Hash -ne (Get-FileHash (Join-Path $run 'rebuilt.db')).Hash) { throw 'Canonical database is not reproducible.' }
    & tools/Phase1Research/Verify-Phase1ResearchData.ps1 docs/research/data > (Join-Path $run 'research.log')
    if ($LASTEXITCODE -ne 0) { throw 'Research validation failed.' }
    $steps.Add('research')
    Invoke-Checked 'acceptance' 'dotnet' @('run','--project','tools/YfmCompanion.Phase4Audit','-c','Release','--no-build','--',$projectRoot,(Join-Path $run 'acceptance.json'))
    Invoke-Checked 'ui' 'dotnet' @('run','--project','tools/YfmCompanion.UiRender','-c','Release','--no-build','--',(Join-Path $run 'ui'))
    Invoke-Checked 'desktop-lifecycle' 'dotnet' @('run','--project','tools/YfmCompanion.UiAudit','-c','Release','--no-build','--',(Join-Path $run 'desktop'))
    Invoke-Checked 'artwork' 'dotnet' @('run','--project','tools/YfmCompanion.UiAudit','-c','Release','--no-build','--',(Join-Path $run 'artwork'),'--artwork-only')
    Invoke-Checked 'documentation' 'python' @('tools/Build-Documentation.py')
    $sourceHashes = @(Get-ChildItem src -Recurse -File | Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } | ForEach-Object {
        [pscustomobject]@{ Path = $_.FullName; SHA256 = (Get-FileHash -LiteralPath $_.FullName).Hash }
    })
    $publish = Join-Path $run 'publish'
    Invoke-Checked 'publish' 'dotnet' @('publish','src/YfmCompanion.Desktop','-c','Release','--no-restore','-m:1','-o',$publish,'-v:q')
    [IO.Directory]::CreateDirectory($package) | Out-Null
    Copy-Item -Path (Join-Path $publish '*') -Destination $package -Recurse
    $resources = Join-Path $package 'Resources'
    [IO.Directory]::CreateDirectory($resources) | Out-Null
    foreach ($component in @('Data','Artwork','ResearchData')) {
        $source = [IO.Path]::GetFullPath((Join-Path $package $component))
        if (-not $source.StartsWith($package + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid resource move path.' }
        Move-Item -LiteralPath $source -Destination $resources
    }
    $documentation = Join-Path $package 'Documentation'
    $licenses = Join-Path $package 'Licenses'
    [IO.Directory]::CreateDirectory($documentation) | Out-Null
    [IO.Directory]::CreateDirectory($licenses) | Out-Null
    Copy-Item output/docs/* -Destination $documentation -Recurse
    Copy-Item output/pdf/YFM-Fusion-Companion-User-Guide.pdf -Destination $documentation
    Copy-Item docs/SETUP.md,docs/LIMITATIONS.md,docs/DESIGN.md,docs/USER_GUIDE.md -Destination $documentation
    Copy-Item LICENSE,THIRD_PARTY_NOTICES.md -Destination $licenses
    Copy-Item licenses/dependencies -Destination (Join-Path $licenses 'Dependencies') -Recurse
    $notice = (Get-Content (Join-Path $licenses 'THIRD_PARTY_NOTICES.md') -Raw).Replace('](licenses/dependencies/README.md)', '](Dependencies/README.md)').Replace('](assets/card-artwork/ATTRIBUTION.md)', '](../Resources/Artwork/ATTRIBUTION.md)')
    $notice | Set-Content (Join-Path $licenses 'THIRD_PARTY_NOTICES.md') -Encoding utf8
    @'
YFM FUSION COMPANION

1. Extract this entire folder before starting.
2. Double-click YFM Fusion Companion.exe. No separate .NET install is needed.
3. Keep Resources, Documentation and Licenses beside the executable.

Documentation/USER_GUIDE.html explains everyday use.
Documentation/DESIGN.html is the searchable design and source-code guide.
Documentation/YFM-Fusion-Companion-User-Guide.pdf is the illustrated manual.

The app reads game state and saves without modifying them. Settings and proof
checkpoints live outside this package in your Windows user profile. Upgrading
does not require copying settings into this folder.

Source, release checks and license:
https://github.com/vincevandriel/yfm-fusion-companion
'@ | Set-Content -LiteralPath (Join-Path $package 'START-HERE.txt') -Encoding utf8
    $executable = Join-Path $package 'YFM Fusion Companion.exe'
    $selfCheck = Join-Path $run 'packaged-self-check.json'
    $process = Start-Process -FilePath $executable -ArgumentList @('--verify-package', ('"{0}"' -f $selfCheck)) -WorkingDirectory $package -WindowStyle Hidden -PassThru -RedirectStandardError (Join-Path $run 'packaged-self-check-stderr.log')
    $null = $process.Handle
    if (-not $process.WaitForExit(60000)) { Stop-Process -Id $process.Id -Force; throw 'Packaged self-check timed out.' }
    if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $selfCheck)) { throw 'Packaged executable self-check failed.' }
    $selfReport = Get-Content -LiteralPath $selfCheck -Raw | ConvertFrom-Json
    if (-not $selfReport.Passed -or $selfReport.ResourceLayout -ne 'organized' -or $selfReport.ArtworkVerified -ne 722) { throw 'Invalid package self-check evidence.' }
    $steps.Add('packaged-self-check')
    $process = Start-Process -FilePath $executable -WorkingDirectory $package -WindowStyle Hidden -PassThru
    try {
        Start-Sleep -Seconds 4
        $process.Refresh()
        if ($process.HasExited) { throw 'Packaged executable exited during normal startup.' }
        $steps.Add('normal-startup')
    }
    finally {
        if (-not $process.HasExited) { $null = $process.CloseMainWindow(); if (-not $process.WaitForExit(5000)) { Stop-Process -Id $process.Id -Force } }
    }
    Add-Type -AssemblyName System.Drawing
    $icon = [Drawing.Icon]::ExtractAssociatedIcon($executable)
    if ($null -eq $icon) { throw 'Executable has no Windows icon.' }
    $iconBitmap = $icon.ToBitmap()
    try { $iconBitmap.Save((Join-Path $run 'executable-icon.png'), [Drawing.Imaging.ImageFormat]::Png) }
    finally { $iconBitmap.Dispose(); $icon.Dispose() }
    [xml]$trx = Get-Content (Join-Path $run 'tests/release.trx') -Raw
    $counters = $trx.SelectSingleNode("//*[local-name()='Counters']")
    $testCount = [int]$counters.GetAttribute('passed')
    if ($testCount -lt 1 -or $testCount -ne [int]$counters.GetAttribute('total')) { throw 'Incomplete passing test evidence.' }
    foreach ($file in $sourceHashes) { if ((Get-FileHash -LiteralPath $file.Path).Hash -ne $file.SHA256) { throw 'Source changed during publishing.' } }
    $audit = [ordered]@{ Version=$version; Passed=$true; TestsPassed=$testCount; Steps=$steps; PackageSelfCheck=$selfReport; LiveGameplayRetested=$false; NativeDpiSwitchingRetested=$false }
    $audit | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $documentation 'Release-audit.json') -Encoding utf8
    $manifest = @(Get-ChildItem -LiteralPath $package -Recurse -File | Sort-Object FullName | ForEach-Object {
        [pscustomobject]@{ Path=[IO.Path]::GetRelativePath($package,$_.FullName).Replace('\','/'); SHA256=(Get-FileHash -LiteralPath $_.FullName).Hash }
    })
    $manifest | ForEach-Object { "$($_.SHA256)  $($_.Path)" } | Set-Content -LiteralPath (Join-Path $package 'CHECKSUMS.sha256') -Encoding ascii
    Compress-Archive -LiteralPath $package -DestinationPath $archive
    $extracted = Join-Path $run 'extracted'
    Expand-Archive -LiteralPath $archive -DestinationPath $extracted
    $extractedPackage = Join-Path $extracted ([IO.Path]::GetFileName($package))
    foreach ($file in $manifest) {
        if ((Get-FileHash -LiteralPath (Join-Path $extractedPackage $file.Path)).Hash -ne $file.SHA256) { throw "Extracted checksum mismatch: $($file.Path)" }
    }
    if (@(Get-ChildItem -LiteralPath $extractedPackage -Recurse -File).Count -ne $manifest.Count + 1) { throw 'Extracted package contains unexpected files.' }
    # Fail-closed probe against an independent extraction, never the deliverable.
    $probeDatabase = [IO.Path]::GetFullPath((Join-Path $extractedPackage 'Resources/Data/yfm.db'))
    if (-not $probeDatabase.StartsWith($extractedPackage + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid probe path.' }
    $probeBackup = "$probeDatabase.probe-backup"
    Move-Item -LiteralPath $probeDatabase -Destination $probeBackup
    try {
        $missingReport = Join-Path $run 'missing-resource-check.json'
        $probe = Start-Process -FilePath (Join-Path $extractedPackage 'YFM Fusion Companion.exe') -ArgumentList @('--verify-package', ('"{0}"' -f $missingReport)) -WorkingDirectory $extractedPackage -WindowStyle Hidden -PassThru
        $null = $probe.Handle
        if (-not $probe.WaitForExit(60000)) { Stop-Process -Id $probe.Id -Force; throw 'Missing-resource probe timed out.' }
        if ($probe.ExitCode -ne 1 -or (Get-Content -LiteralPath $missingReport -Raw | ConvertFrom-Json).Passed) { throw 'Missing-resource probe did not fail closed.' }
    }
    finally { Move-Item -LiteralPath $probeBackup -Destination $probeDatabase }
    $audit['MissingResourceFailsClosed'] = $true
    $archiveHash = (Get-FileHash -LiteralPath $archive).Hash
    "$archiveHash  $([IO.Path]::GetFileName($archive))" | Set-Content -LiteralPath "$archive.sha256" -Encoding ascii
    $audit['Archive'] = [IO.Path]::GetFileName($archive)
    $audit['ArchiveSHA256'] = $archiveHash
    $audit['FilesVerified'] = $manifest.Count + 1
    $audit['SourceFilesUnchanged'] = $sourceHashes.Count
    $audit | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $run 'release-audit.json') -Encoding utf8
    Write-Host "PASS release $version : $($manifest.Count + 1) files verified."
    Write-Host "Package: $package"
    Write-Host "Archive: $archive"
    Write-Host "Evidence: $run"
}
finally { $env:YFM_COMPANION_SETTINGS_DIRECTORY = $previousSettings; Pop-Location }
