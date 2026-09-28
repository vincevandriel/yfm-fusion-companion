[CmdletBinding()]
param(
    [Parameter()]
    [string]$OutputRoot = (Join-Path $PSScriptRoot '..\..\docs\research\data'),

    [Parameter()]
    [string]$PortraitRoot = (Join-Path $PSScriptRoot '..\..\assets\duelist-portraits')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$revision = 'bd91b5b0568059ec38555a3994c30a5068dc3dda'
$base = "https://raw.githubusercontent.com/sg4e/YGOFM-gamedata/$revision/sqlite/json"
$dropUrl = "$base/droppool.json"
$duelistUrl = "$base/duelistinfo.json"
$expectedDropHash = '2a1c432728e05b1f61db53524d6efa8f22d5851e724f50e3acff66a80a4c0053'
$expectedDuelistHash = '045b6607960cab01da22ff99370936b31b1438e9405bd5daf8492aaaaefc2b5d'
$portraitBase = 'https://yugioh-fm-db.pages.dev/assets/images/duelists'

function Write-JsonFile([string]$Path, [object]$Value) {
    [System.IO.Directory]::CreateDirectory((Split-Path -Parent $Path)) | Out-Null
    $json = $Value | ConvertTo-Json -Depth 12
    [System.IO.File]::WriteAllText($Path, $json + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))
}

function Get-VerifiedJson([string]$Url, [string]$ExpectedHash) {
    $temporary = [System.IO.Path]::GetTempFileName()
    try {
        Invoke-WebRequest -Uri $Url -OutFile $temporary
        $actual = (Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actual -ne $ExpectedHash) { throw "Pinned source hash mismatch for $Url. Expected $ExpectedHash, got $actual." }
        return Get-Content -LiteralPath $temporary -Raw | ConvertFrom-Json
    }
    finally { Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue }
}

$duelists = @(Get-VerifiedJson $duelistUrl $expectedDuelistHash | Sort-Object duelistId)
$pools = @(Get-VerifiedJson $dropUrl $expectedDropHash)
if ($duelists.Count -ne 39) { throw "Expected 39 duelists, got $($duelists.Count)." }

$poolLabels = [ordered]@{ SAPow = 'S/A POW'; SATec = 'S/A TEC'; BCD = 'B/C/D' }
$items = foreach ($duelist in $duelists) {
    $id = [int]$duelist.duelistId
    $tables = foreach ($poolType in $poolLabels.Keys) {
        $entries = @(
            $pools |
                Where-Object { [int]$_.duelist -eq $id -and $_.poolType -eq $poolType } |
                Sort-Object @{ Expression = { -[int]$_.cardProbability } }, cardId |
                ForEach-Object { [ordered]@{ card_id = [int]$_.cardId; weight = [int]$_.cardProbability } }
        )
        $sum = ($entries.weight | Measure-Object -Sum).Sum
        if ($sum -ne 2048) { throw "Duelist $id $poolType reward table totals $sum instead of 2048." }
        [ordered]@{ id = $poolType; label = $poolLabels[$poolType]; denominator = 2048; entries = $entries }
    }
    [ordered]@{
        duelist_id = $id
        name = [string]$duelist.duelist
        portrait = ('{0:D2}.png' -f $id)
        reward_tables = @($tables)
    }
}

Write-JsonFile (Join-Path $OutputRoot 'free_duel_reference.json') ([ordered]@{
    schema_version = 1
    generated_at_utc = '2026-09-28T00:00:00Z'
    source = 'ygofm_gamedata'
    source_revision = $revision
    probability_note = 'Each percentage is conditional on earning the named result-rank group. Weight divided by 2048 is the exact reward probability for that table.'
    duelists = @($items)
})

[System.IO.Directory]::CreateDirectory($PortraitRoot) | Out-Null
$portraitItems = foreach ($id in 1..39) {
    $name = '{0:D2}.png' -f $id
    $path = Join-Path $PortraitRoot $name
    Invoke-WebRequest -Uri "$portraitBase/$id.png" -OutFile $path
    $bytes = [System.IO.File]::ReadAllBytes($path)
    if ($bytes.Length -lt 100 -or $bytes[0] -ne 137 -or $bytes[1] -ne 80 -or $bytes[2] -ne 78 -or $bytes[3] -ne 71) {
        throw "Portrait $id was not a valid PNG download."
    }
    [ordered]@{
        duelist_id = $id
        path = $name
        sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}

Write-JsonFile (Join-Path $PortraitRoot 'manifest.json') ([ordered]@{
    schema_version = 1
    source_url = 'https://www.spriters-resource.com/playstation/ygofm/asset/51205/'
    numbered_crop_source = 'https://yugioh-fm-db.pages.dev/duelists'
    attribution = 'Original in-game mugshots by Konami; sheet archived by The Spriters Resource user Phongpon; numbered crops cross-checked against the YFM Database.'
    source_sheet_sha256 = 'daabafecad56e29b1268d4f435b97749735c43825804dd332fbae8835ff66932'
    portraits = @($portraitItems)
})

[ordered]@{
    duelists = $items.Count
    reward_tables = @($items | ForEach-Object { $_.reward_tables.Count } | Measure-Object -Sum).Sum
    reward_entries = @($items | ForEach-Object { $_.reward_tables | ForEach-Object { $_.entries.Count } } | Measure-Object -Sum).Sum
    portraits = $portraitItems.Count
} | ConvertTo-Json
