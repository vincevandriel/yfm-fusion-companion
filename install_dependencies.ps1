[CmdletBinding()]
param([switch] $InstallBuildTools)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT -or
    -not [Environment]::Is64BitOperatingSystem)
{
    throw "YFM Fusion Companion requires 64-bit Windows 10 or 11."
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$releaseReady =
    ((Test-Path -LiteralPath (Join-Path $scriptRoot "YFM Fusion Companion.exe")) -and
     (Test-Path -LiteralPath (Join-Path $scriptRoot "Data\yfm.db"))) -or
    ((Test-Path -LiteralPath (Join-Path $scriptRoot "Run\YFM Fusion Companion.exe")) -and
     (Test-Path -LiteralPath (Join-Path $scriptRoot "Run\Data\yfm.db")))

Write-Host "YFM Fusion Companion dependency check" -ForegroundColor Cyan
Write-Host "Windows x64: PASS" -ForegroundColor Green
if ($releaseReady)
{
    Write-Host "Application and Data\yfm.db: PASS" -ForegroundColor Green
    Write-Host "This release is self-contained; no separate .NET runtime is required." -ForegroundColor Green
}
else
{
    Write-Warning "The executable and Data\yfm.db were not found together. Extract the complete release ZIP first."
}

if ($InstallBuildTools)
{
    $hasSdk = (Get-Command dotnet -ErrorAction SilentlyContinue) -and
        (@(dotnet --list-sdks 2>$null | Where-Object { $_ -match '^9\.' }).Count -gt 0)
    if (-not $hasSdk)
    {
        if (-not (Get-Command winget -ErrorAction SilentlyContinue))
        {
            throw "Install the .NET 9 SDK from https://dotnet.microsoft.com/download/dotnet/9.0."
        }
        winget install --id Microsoft.DotNet.SDK.9 --exact --accept-package-agreements --accept-source-agreements
        if ($LASTEXITCODE -ne 0) { throw "The .NET 9 SDK installation failed." }
    }
    Write-Host ".NET 9 SDK build dependency: PASS" -ForegroundColor Green
}

Write-Host ""
Write-Host "RetroArch/SwanStation is optional and only needed for save and Live Duel automation."
Write-Host "Manual Turn Adviser, Deck Analyzer, and Owned-card Optimizer require no emulator."
if (-not $releaseReady -and -not $InstallBuildTools) { exit 1 }
