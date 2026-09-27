"""Keep the established portable verification flow with distinct campaign outputs."""
from pathlib import Path

root = Path(__file__).resolve().parents[1]
text = (root / 'tools/Publish-CompactWrap.ps1').read_text(encoding='utf-8-sig')
text = text.replace('compact-wrap', 'campaign-library').replace('CompactWrap', 'CampaignDecks')
text = text.replace("'source_manifest.json'))", "'source_manifest.json', 'fan_deck_sources.json'))")
needle = "    Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination $packageDirectory"
text = text.replace(needle, needle + "\n    Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\\benchmarks\\CAMPAIGN_DECK_RESULTS.md') -Destination $packageDirectory")
text = text.replace(needle, needle + "\n    Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\\research\\FAN_DECK_RESEARCH.md') -Destination $packageDirectory\n    Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\\benchmarks\\campaign-deck-library.json') -Destination $packageDirectory\n    Copy-Item -LiteralPath (Join-Path $projectRoot 'src\\YfmCompanion.Engine\\Research\\campaign-decks.json') -Destination $packageDirectory\n    Copy-Item -LiteralPath (Join-Path $renderDirectory 'deck-library-ui-audit.json') -Destination $packageDirectory")
start = text.index("    @'\n")
end = text.index("'@ | Set-Content", start)
text = text[:start] + """    @'
YFM Fusion Companion 2.0 - researched campaign deck library

Extract the complete folder and double-click YFM Fusion Companion.exe.
Keep Artwork, Data and ResearchData beside it. The .NET runtime is included.

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
""" + text[end:]
text = text.replace("        LiveGameplayCoexistenceRetested = $false", "        DeckLibrary = Get-Content -LiteralPath (Join-Path $renderDirectory 'deck-library-ui-audit.json') -Raw | ConvertFrom-Json\n        LiveGameplayCoexistenceRetested = $false")
(root / 'tools/Publish-CampaignLibrary.ps1').write_text(text, encoding='utf-8')
print('Prepared a separate campaign-library publisher with package extraction/hash verification.')
