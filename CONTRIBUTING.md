# Contributing

Start with the [design guide](docs/DESIGN.md) and [source index](docs/reference/SOURCE_INDEX.md). The four production projects separate catalog data, pure game/search logic, read-only emulator/save integration, and WPF presentation. Code is MIT licensed; artwork rights are separate.

## Build and test on Windows

Use Windows x64, Git, PowerShell 7, and .NET SDK **9.0.316** (pinned by `global.json`). The self-contained runtime version is pinned in the desktop project. Ordinary users need only the portable release.

```powershell
git clone https://github.com/vincevandriel/yfm-fusion-companion.git
Set-Location yfm-fusion-companion
dotnet restore YfmFusionCompanion.sln --locked-mode
dotnet build YfmFusionCompanion.sln -c Release --no-restore -m:1
dotnet test YfmFusionCompanion.sln -c Release --no-build
dotnet run --project src/YfmCompanion.Desktop -c Release --no-build
```

The canonical database and artwork are checked in. No game files, credentials or personal saves are required for tests. Tests and UI audits use synthetic data and isolated settings. Do not commit personal snapshots, memory dumps, diagnostics containing private paths, or binaries from local builds.

```powershell
dotnet format YfmFusionCompanion.sln --verify-no-changes --severity warn --no-restore
dotnet run --project tools/YfmCompanion.Phase4Audit -c Release --no-build -- . artifacts/acceptance.json
dotnet run --project tools/YfmCompanion.UiRender -c Release --no-build -- artifacts/ui
dotnet run --project tools/YfmCompanion.UiAudit -c Release --no-build -- artifacts/desktop
```

Run from the repository root. Use `--states` after the UiAudit output path for synthetic state/scale renders, `--live-interaction` for changing synthetic snapshots, and `--real-live` only for an explicitly arranged read-only gameplay observation. Synthetic renders do not establish native OS-DPI or real gameplay behavior. Benchmarks and evidence boundaries are explained in the design guide.

## Documentation and branding

With Python 3.10 or later:

```powershell
python -m pip install -r tools/requirements-docs.txt
python tools/Build-Documentation.py
```

This regenerates the Markdown table of contents and source index, validates local documentation targets, renders offline HTML under `output/docs`, and rebuilds the illustrated PDF. Review the PDF pages and any changed screenshots before release. Code links target the version in the desktop project, so they resolve publicly after that tag is created. `output/docs` is generated; Markdown is editable source.

The editable icon is `assets/branding/app-icon.svg`. PNG and nine-size Windows ICO are committed, so builds need no image tool. To regenerate, install `sharp@0.34.5` in your Node environment and run `node tools/Create-AppIcon.cjs`. Do not use a copied official game logo.

## Make a change

Keep each change coherent. Add independent tests for changed rules, boundary cases, cancellation or persistence. Search/scoring changes require serial/parallel equality and checkpoint-identity review; a new objective must not resume a proof made under another objective. Live/save changes must retain read-only access and stale-state handling. Include relevant documentation and evidence in your pull request.

## Publish a release

Update the desktop version fields and release notes together. Commit intended source changes. Run from PowerShell 7:

```powershell
pwsh -NoProfile -File tools/Publish-Release.ps1 -OutputDirectory artifacts/releases -EvidenceDirectory artifacts/release-audit
```

The publisher refuses an existing destination. It restores locked dependencies, checks formatting, builds all solution projects, runs tests and independent acceptance/UI/lifecycle/artwork checks, checks known dependency vulnerabilities, reproduces the catalog, validates research, builds documentation, publishes self-contained, exercises the actual executable, and checks every file after ZIP extraction. Failure prevents publication. It does not grant real-gameplay or native-DPI acceptance.

GitHub Actions runs this same script. A `v<version>` tag matching the project version publishes one Windows ZIP and its checksum. The package includes a file manifest, offline guides and audit summary. Non-tag CI keeps temporary packages for seven days. Inspect the successful tag workflow, independently download the public ZIP, compare hashes, extract it, and run its offline self-check before retiring previous releases:

```powershell
& '.\YFM Fusion Companion.exe' --verify-package '.\package-check.json'
```

Inspect the JSON and process exit code; a normal window surviving startup alone is not a content check. Keep compact provenance and cleanup records under `docs/audit/releases/`. Delete only identified superseded runtime packages after verification; retain source history, user settings, proof checkpoints, game/save files and unrelated projects.
