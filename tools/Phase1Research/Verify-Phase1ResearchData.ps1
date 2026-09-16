[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Container })]
    [string]$DataRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Read-RequiredJson([string]$Name) {
    $path = Join-Path $DataRoot $Name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Missing required research data file: $Name"
    }
    return Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
}

$manifest = Read-RequiredJson 'source_manifest.json'
$opponents = Read-RequiredJson 'opponent_reference.json'
$guardianStars = Read-RequiredJson 'guardian_star_rules.json'
$policy = Read-RequiredJson 'optimizer_policy.json'
$summary = Read-RequiredJson 'generation_summary.json'

if ([int]$manifest.schema_version -ne 1 -or [int]$opponents.schema_version -ne 1) {
    throw 'Unsupported Phase 1 research schema version.'
}
if (@($manifest.sources).Count -lt 5) {
    throw 'Source manifest does not contain the required provenance records.'
}
if ((($manifest | ConvertTo-Json -Depth 16) -match '([A-Za-z]:\\|Emulator Games|\.bin|\.cue|\.srm)')) {
    throw 'Research data contains a private local path or prohibited game/save reference.'
}
if (@($opponents.opponents).Count -ne 39 -or [int]$opponents.opponent_count -ne 39) {
    throw 'Expected exactly 39 opponents.'
}
$ids = @($opponents.opponents | ForEach-Object { [int]$_.duelist_id })
if (@($ids | Sort-Object -Unique).Count -ne 39 -or @(Compare-Object $ids (1..39)).Count -ne 0) {
    throw 'Opponent IDs must be the complete unique range 1 through 39.'
}
foreach ($opponent in $opponents.opponents) {
    if ([string]::IsNullOrWhiteSpace($opponent.name) -or [int]$opponent.opening_hand_size -le 0) {
        throw "Opponent $($opponent.duelist_id) is missing name or opening hand size."
    }
    if (@($opponent.deck_pool).Count -eq 0) {
        throw "Opponent $($opponent.duelist_id) has no Deck pool."
    }
    foreach ($entry in $opponent.deck_pool) {
        if ([int]$entry.card_id -lt 1 -or [int]$entry.card_id -gt 722 -or [int]$entry.weight -le 0) {
            throw "Opponent $($opponent.duelist_id) contains an invalid Deck pool entry."
        }
    }
}
if (@($policy.general_safety_duelist_ids).Count -ne 33 -or @($policy.final_gauntlet_duelist_ids).Count -ne 6) {
    throw 'Optimizer scope does not partition general safety and final gauntlet as specified.'
}
if (-not (($policy.general_safety_duelist_ids -contains 39) -and -not ($policy.final_gauntlet_duelist_ids -contains 39))) {
    throw 'Duel Master K must be included in the general-safety scope.'
}
if (@($guardianStars.cycles).Count -ne 2 -or [int]$guardianStars.battle_modifier.amount -ne 500) {
    throw 'Guardian-star reference is incomplete.'
}
if (@($guardianStars.symbols.PSObject.Properties).Count -ne 10) {
    throw 'Guardian-star symbol map is incomplete.'
}
if ([int]$summary.card_count -ne 722 -or [int]$summary.opponent_count -ne 39 -or [int]$summary.deck_pool_entry_count -le 0) {
    throw 'Generation summary counts are invalid.'
}

Write-Output "Phase 1 research data: PASS (39 opponents, $($summary.deck_pool_entry_count) Deck-pool entries, $($manifest.sources.Count) provenance records)."
