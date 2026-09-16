[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Container })]
    [string]$GameDataRoot,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$OutputRoot,

    [Parameter(Mandatory = $true)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string]$DiscAuditEvidence,

    [Parameter()]
    [datetime]$GeneratedAtUtc = [datetime]::MinValue
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[System.IO.Directory]::CreateDirectory($OutputRoot) | Out-Null

function Get-UtcTimestamp([datetime]$Value) {
    $utc = if ($Value -eq [datetime]::MinValue) { [DateTime]::UtcNow } else { $Value.ToUniversalTime() }
    return $utc.ToString('yyyy-MM-ddTHH:mm:ssZ')
}

function Get-JsonFile([string]$Path) {
    return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
}

function Write-JsonFile([string]$Path, [object]$Value) {
    $directory = Split-Path -Path $Path -Parent
    [System.IO.Directory]::CreateDirectory($directory) | Out-Null
    $json = $Value | ConvertTo-Json -Depth 16
    [System.IO.File]::WriteAllText($Path, $json + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))
}

function Get-SourceHash([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

$jsonRoot = Join-Path $GameDataRoot 'sqlite\json'
$duelistInfoPath = Join-Path $jsonRoot 'duelistinfo.json'
$dropPoolPath = Join-Path $jsonRoot 'droppool.json'
$cardInfoPath = Join-Path $jsonRoot 'cardinfo.json'
$licensePath = Join-Path $GameDataRoot 'LICENSE'
foreach ($requiredPath in @($duelistInfoPath, $dropPoolPath, $cardInfoPath, $licensePath, $DiscAuditEvidence)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Required Phase 1 source is missing: $requiredPath"
    }
}

$duelists = @(Get-JsonFile $duelistInfoPath | Sort-Object duelistId)
$pools = @(Get-JsonFile $dropPoolPath)
$cards = @(Get-JsonFile $cardInfoPath)
if ($duelists.Count -ne 39 -or $cards.Count -ne 722) {
    throw "Unexpected source counts: duelists=$($duelists.Count), cards=$($cards.Count)."
}

$lowMageIds = @(21, 23, 25, 27, 29, 31)
$highMageIds = @(22, 24, 26, 28, 30)
$gauntletIds = @(33, 34, 35, 36, 37, 38)
$generalSafetyIds = @(1..32 + 39)

$opponents = foreach ($duelist in $duelists) {
    $id = [int]$duelist.duelistId
    $isLowMage = $id -in $lowMageIds
    $isHighMage = $id -in $highMageIds
    $isMageAggressive = ($isLowMage -and $id -ne 31) -or $id -eq 34
    $deckEntries = @(
        $pools |
            Where-Object { [int]$_.duelist -eq $id -and $_.poolType -eq 'Deck' } |
            Sort-Object cardId |
            ForEach-Object {
                [ordered]@{
                    card_id = [int]$_.cardId
                    weight = [int]$_.cardProbability
                }
            }
    )
    if ($deckEntries.Count -eq 0) {
        throw "Duelist $id has no Deck pool entries."
    }
    [ordered]@{
        duelist_id = $id
        name = [string]$duelist.duelist
        opening_hand_size = [int]$duelist.handSize
        optimizer_scope = if ($id -in $gauntletIds) { 'final_gauntlet_only' } else { 'general_safety_candidate' }
        ai_flags = [ordered]@{
            low_mage = $isLowMage
            high_mage = $isHighMage
            aggressive_field_spell_behavior = $isMageAggressive
            confidence = 'source_code_reimplementation'
            implementation_note = 'This flag is a pinned reimplementation claim. Phase 2 must independently verify any behavior used in scoring.'
        }
        deck_pool = $deckEntries
        deck_generation = [ordered]@{
            method = 'weighted repeated draws from Deck pool; reject a fourth copy; continue until 40 cards'
            confidence = 'source_code_reimplementation'
            source_file = 'Deck.java:createDuelistDeck'
        }
    }
}

$sourceRevision = (& git -C $GameDataRoot rev-parse HEAD).Trim()
$sourceRemote = (& git -C $GameDataRoot remote get-url origin).Trim()
$timestamp = Get-UtcTimestamp $GeneratedAtUtc

$sourceManifest = [ordered]@{
    schema_version = 1
    generated_at_utc = $timestamp
    scope = 'Phase 1 research inputs only. This manifest does not turn a community claim into a game-mechanics fact.'
    sources = @(
        [ordered]@{
            id = 'supplied_sql_catalog'
            role = 'companion card catalog and resolved pair baseline'
            provenance = 'user-supplied local SQL source, previously deterministically converted to artifacts/yfm.db'
            confidence = 'disc_verified_for_fusion_pairs'
            license = 'user-provided; do not redistribute the source SQL without authorization'
        },
        [ordered]@{
            id = 'ntsc_u_disc_fusion_audit'
            role = 'concrete pair-fusion priority and equip fallback order'
            evidence_path = 'private definitive-audit evidence; hash retained, source file is not distributed'
            sha256 = Get-SourceHash $DiscAuditEvidence
            confidence = 'disc_verified'
            license = 'derived audit facts only; do not include ROM or executable material in release'
        },
        [ordered]@{
            id = 'ygofm_gamedata'
            role = 'duelist names, opening hand sizes, Deck pools, card records, drops, and RNG/deck-generation reference'
            url = $sourceRemote
            revision = $sourceRevision
            files = @(
                [ordered]@{ path = 'sqlite/json/duelistinfo.json'; sha256 = Get-SourceHash $duelistInfoPath },
                [ordered]@{ path = 'sqlite/json/droppool.json'; sha256 = Get-SourceHash $dropPoolPath },
                [ordered]@{ path = 'sqlite/json/cardinfo.json'; sha256 = Get-SourceHash $cardInfoPath }
            )
            confidence = 'source_code_reimplementation'
            license = 'MIT; attribution retained in THIRD_PARTY_NOTICES before shipping derived data'
        },
        [ordered]@{
            id = 'speedrun_guides'
            role = 'practical deck archetypes and farming heuristics'
            urls = @(
                'https://www.speedrun.com/yugiohfm/guides/o9dax',
                'https://www.speedrun.com/yugiohfm/guides/x53ec'
            )
            confidence = 'community_consensus'
            license = 'reference only; no guide text copied into machine-readable data'
        },
        [ordered]@{
            id = 'gamefaqs_archival_guides'
            role = 'guardian-star and field-card cross-checks'
            urls = @(
                'https://gamefaqs.gamespot.com/ps/561010-yu-gi-oh-forbidden-memories/faqs/18573',
                'https://gamefaqs.gamespot.com/ps/561010-yu-gi-oh-forbidden-memories/faqs/27035',
                'https://gamefaqs.gamespot.com/ps/561010-yu-gi-oh-forbidden-memories/faqs/78677'
            )
            confidence = 'community_consensus'
            license = 'reference only; no guide text copied into machine-readable data'
        }
    )
}

$guardianStars = [ordered]@{
    schema_version = 1
    generated_at_utc = $timestamp
    confidence = 'community_consensus_pending_phase_2_game_verification'
    battle_modifier = [ordered]@{
        amount = 500
        applies_to = @('attack', 'defense')
        note = 'The UI must show ? rather than infer a result if either selected guardian star is unknown.'
    }
    cycles = @(
        [ordered]@{
            id = 'solar'
            order = @('Sun', 'Moon', 'Venus', 'Mercury')
            beats_next = $true
        },
        [ordered]@{
            id = 'planetary'
            order = @('Mars', 'Jupiter', 'Saturn', 'Uranus', 'Pluto', 'Neptune')
            beats_next = $true
        }
    )
    symbols = [ordered]@{
        Mars = '♂'; Jupiter = '♃'; Saturn = '♄'; Uranus = '⚲'; Pluto = '♇'; Neptune = '♆'
        Mercury = '☿'; Sun = '☉'; Moon = '☾'; Venus = '♀'
    }
}

$optimizerPolicy = [ordered]@{
    schema_version = 1
    generated_at_utc = $timestamp
    user_selected_objective = 'maximum_safety'
    general_safety_duelist_ids = $generalSafetyIds
    final_gauntlet_duelist_ids = $gauntletIds
    star_chip_mode = [ordered]@{
        default_enabled = $false
        behavior_when_enabled = 'Return an ordered purchase plan and a virtually-owned legal 40-card deck. Never write to the save file or game.'
    }
    required_output_guards = @(
        'Never report an estimated threat score as a proven win probability unless a validated AI simulation supports that claim.',
        'Label heuristic optimization results as best found unless optimality is proven.',
        'Treat an equip as terminal in a fusion chain because its bonus does not persist through a later monster fusion.',
        'Use unknown status rather than guessed live-state data.'
    )
    near_equal_tie_break = 'No reduced safe general matchup and no more than 0.5 percentage-point loss in exact answer-hand coverage; then prefer final-gauntlet viability and lower purchase cost.'
}

Write-JsonFile (Join-Path $OutputRoot 'source_manifest.json') $sourceManifest
Write-JsonFile (Join-Path $OutputRoot 'opponent_reference.json') ([ordered]@{
        schema_version = 1
        generated_at_utc = $timestamp
        source = 'ygofm_gamedata'
        source_revision = $sourceRevision
        opponent_count = $opponents.Count
        opponents = $opponents
    })
Write-JsonFile (Join-Path $OutputRoot 'guardian_star_rules.json') $guardianStars
Write-JsonFile (Join-Path $OutputRoot 'optimizer_policy.json') $optimizerPolicy

$summary = [ordered]@{
    schema_version = 1
    generated_at_utc = $timestamp
    card_count = $cards.Count
    opponent_count = $opponents.Count
    deck_pool_entry_count = @($opponents | ForEach-Object { $_.deck_pool.Count } | Measure-Object -Sum).Sum
    general_safety_opponent_count = $generalSafetyIds.Count
    final_gauntlet_opponent_count = $gauntletIds.Count
    output_files = @('source_manifest.json', 'opponent_reference.json', 'guardian_star_rules.json', 'optimizer_policy.json')
}
Write-JsonFile (Join-Path $OutputRoot 'generation_summary.json') $summary
Write-Output ($summary | ConvertTo-Json -Depth 8)
