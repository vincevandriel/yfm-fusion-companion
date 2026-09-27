"""Publish the suggested-card ordering update with isolated verification evidence."""
from pathlib import Path

root = Path(__file__).resolve().parents[1]
text = (root / 'tools/Publish-CampaignLibrary.ps1').read_text(encoding='utf-8-sig')
text = text.replace('campaign-library', 'card-order').replace('CampaignDecks', 'CardOrder')
needle = "    Copy-Item -LiteralPath (Join-Path $renderDirectory 'deck-library-ui-audit.json') -Destination $packageDirectory"
assert needle in text
text = text.replace(needle, needle + "\n    Copy-Item -LiteralPath (Join-Path $renderDirectory 'suggested-deck-sorting.json') -Destination $packageDirectory")
text = text.replace('YFM Fusion Companion 2.0 - researched campaign deck library', 'YFM Fusion Companion 2.0 - suggested-card ordering and campaign deck library')
needle = 'Open Owned-Card Optimizer, then RECOMMENDED DECKS.'
assert needle in text
text = text.replace(needle, '''Open Owned-Card Optimizer and use CARD ORDER above the suggested deck.
Alphabetical (A-Z) is the default. Card number shows the lowest first;
ATK and DEF show the highest first. Your choice is remembered across
new results and restarts. This changes the display, not deck calculations.

Open Owned-Card Optimizer, then RECOMMENDED DECKS.''')
needle = '        LiveGameplayCoexistenceRetested = $false'
assert needle in text
text = text.replace(needle, "        SuggestedDeckOrdering = Get-Content -LiteralPath (Join-Path $renderDirectory 'suggested-deck-sorting.json') -Raw | ConvertFrom-Json\n" + needle)
(root / 'tools/Publish-CardOrder.ps1').write_text(text, encoding='utf-8')
print('Prepared the card-order publisher.')
