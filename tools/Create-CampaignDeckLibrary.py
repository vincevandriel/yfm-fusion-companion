"""Author the curated vanilla deck library; resolve every card against read-only data."""
import json
import sqlite3
from pathlib import Path

root = Path(__file__).resolve().parents[1]
db = sqlite3.connect(f"file:{root / 'artifacts/yfm.db'}?mode=ro", uri=True)
cards = {row[1]: row for row in db.execute('SELECT card_id,card_name,droppable FROM cards')}
sources = {
    'casual': 'https://www.reddit.com/r/YugiohFMR/comments/1ffhm89/',
    'completion': 'https://www.reddit.com/r/YugiohFMR/comments/1r6anmw/',
    'equips': 'https://gamefaqs.gamespot.com/boards/561010-yu-gi-oh-forbidden-memories/70354905',
    'female': 'https://www.reddit.com/r/YugiohFMR/comments/1ma0lr3/',
    'farming': 'https://www.reddit.com/r/YugiohFMR/comments/1gpb937/',
    'starter': 'https://cyberdemon531.com/yugiohfm/guide-1/',
    'crimson': 'https://www.reddit.com/r/YugiohFMR/comments/1h1trow/',
}
builds = []


def add(key, name, hero, color, status, description, plan, cautions, groups, recipes, source_keys):
    entries = []
    for role, quantities in groups:
        for card_name, copies in quantities:
            card_id, _, droppable = cards[card_name]
            assert droppable, f'Unobtainable vanilla card: {card_name}'
            assert 1 <= copies <= 3
            entries.append({'CardId': card_id, 'Copies': copies, 'Role': role})
    assert sum(e['Copies'] for e in entries) == 40, name
    assert len({e['CardId'] for e in entries}) == len(entries), name
    routes = []
    for materials, result in recipes:
        ids = [cards[n][0] for n in materials]
        current = ids[0]
        for following in ids[1:]:
            pair = db.execute('SELECT result_card_id,is_glitch FROM fusion_pairs WHERE material_low_id=? AND material_high_id=?', sorted((current, following))).fetchone()
            assert pair and not pair[1], (materials, current, following)
            current = pair[0]
        assert current == cards[result][0]
        routes.append({'Materials': ids, 'ResultCardId': current})
    builds.append({'Id': key, 'Name': name, 'HeroCardId': cards[hero][0], 'Accent': color,
                   'Stage': status, 'Description': description, 'Plan': plan, 'Cautions': cautions,
                   'Entries': entries, 'Recipes': routes, 'Sources': [sources[s] for s in source_keys]})


add('thunder-umi', 'Thunder Tide', 'Twin-headed Thunder Dragon', '#55BDE8', 'Campaign → endgame with power-ups',
    'Repeatable Thunder fusions, a single Umi, compatible power-ups and reliable removal.',
    'Start with available Dragon/Thunder materials. Replace weak three-material chains with stronger two-card recipes as you collect them. Umi + Megamorph + Dragon Treasure takes Twin-headed Thunder Dragon to 4,800 ATK over separate plays. Preserve Raigeki for threats your current monster cannot beat.',
    'A 2,800 ATK fusion alone is insufficient late in the game. The displayed setup odds require the field and equips to be present in the same opening hand; they do not assume an active Umi or predict a win.',
    [('Fusion materials', [('Crawling Dragon', 3), ('Dragon Zombie', 3), ('Curse of Dragon', 3), ('Thunder Dragon', 3), ('Kaminari Attack', 3), ('Electric Snake', 3), ('LaLa Li-oon', 3), ('Baby Dragon', 3), ('Oscillo Hero #2', 2)]),
     ('Removal', [('Raigeki', 3), ('Widespread Ruin', 2)]), ('Field', [('Umi', 1)]),
     ('Power-ups', [('Dragon Treasure', 2), ('Invigoration', 2), ('Megamorph', 2), ('Bright Castle', 2)])],
    [(['Crawling Dragon', 'Electric Snake'], 'Twin-headed Thunder Dragon'), (['Baby Dragon', 'Thunder Dragon'], 'Twin-headed Thunder Dragon')], ['starter', 'completion', 'farming'])

add('meteor-hybrid', 'Meteor Crossfire', 'Meteor B. Dragon', '#FF9970', 'Late campaign / final gauntlet',
    'Meteor B. Dragon as both a natural draw and a fusion, with Thunder and Mercury backups.',
    'Red-eyes B. Dragon + Meteor Dragon produces a 3,500 ATK boss. Both materials also have actual Thunder fusion routes. Dragon Treasure, Bright Castle and Megamorph support the main Dragon/Thunder routes; Salamandra is for Meteor B. Dragon. A natural Meteor + Megamorph + Dragon Treasure reaches 5,000 ATK without terrain.',
    'Mountain helps opposing Dragons too, including Blue-eyes Ultimate Dragon. Treat it as a situational card. This is a farming destination rather than a starting deck; acquiring natural Meteor copies can take substantial play.',
    [('Monsters / materials', [('Meteor B. Dragon', 3), ('Red-eyes B. Dragon', 3), ('Meteor Dragon', 3), ('Curse of Dragon', 3), ('Dragon Zombie', 3), ('Thunder Dragon', 3), ('Kaminari Attack', 3), ('Skull Knight', 2), ('Zoa', 2)]),
     ('Removal', [('Raigeki', 3), ('Widespread Ruin', 2)]), ('Field', [('Mountain', 1)]),
     ('Power-ups', [('Dragon Treasure', 3), ('Salamandra', 2), ('Megamorph', 2), ('Bright Castle', 2)])],
    [(['Red-eyes B. Dragon', 'Meteor Dragon'], 'Meteor B. Dragon'), (['Meteor Dragon', 'Thunder Dragon'], 'Twin-headed Thunder Dragon')], ['completion', 'equips'])

add('mercury-control', 'Mercury Eclipse', 'Skull Knight', '#BB9AF7', 'Endgame alternative to Thunder',
    'Skull Knight and Zoa use Mercury, backed by shared dark equips and Meteor finishers.',
    'Mercury gains 500 combat points against Sun, one of Blue-eyes Ultimate Dragon’s stars. Yami supports the Fiend/Spellcaster core. Skull Knight + Yami + two Megamorphs reaches 5,150 ATK before guardian comparison; Zoa reaches 5,100. Use actual compatibility rather than assuming every dark-looking monster takes Dark Energy.',
    'Mercury is neutral against Venus; its advantage is not universal. The heavy equip package can draw without a monster. Removal is retained for campaign survival even though some S/A-POW farming lists omit it to protect rank.',
    [('Natural threats / fusion pair', [('Skull Knight', 3), ('Zoa', 3), ('Dark Magician', 3), ('Metalzoa', 3), ('Curse of Dragon', 3), ('Meteor B. Dragon', 3), ('Red-eyes B. Dragon', 3), ('Meteor Dragon', 3)]),
     ('Removal', [('Raigeki', 3), ('Widespread Ruin', 2)]), ('Field', [('Yami', 1)]),
     ('Power-ups', [('Dark Energy', 3), ('Bright Castle', 3), ('Megamorph', 3), ('Black Pendant', 1)])],
    [(['Red-eyes B. Dragon', 'Meteor Dragon'], 'Meteor B. Dragon')], ['equips', 'completion', 'casual'])

add('natural-bosses', 'Iron & Meteor', 'Metalzoa', '#A7C4CD', 'Farmed endgame / fewer materials',
    'Strong monsters drawn directly leave more opening-hand space for power-ups and removal.',
    'Natural Meteor B. Dragon and Metalzoa avoid spending two or three opening cards on a monster. Skull Knight and Zoa add a different guardian matchup. Start with the strongest owned natural bodies; fill missing slots with actual fusion partners, not unrelated ritual cards. Meteor + Megamorph + Bright Castle reaches 5,000 ATK before terrain.',
    'The reference list is expensive to farm. Metalzoa’s stars do not provide Mercury’s Sun advantage. Yami supports the Fiend/Spellcaster core without strengthening enemy Dragons, but does not boost Meteor or Metalzoa. The heavier equip package trades fusion density for finishing power.',
    [('Natural threats / backups', [('Meteor B. Dragon', 3), ('Skull Knight', 3), ('Zoa', 3), ('Metalzoa', 3), ('Dark Magician', 3), ('Red-eyes B. Dragon', 2), ('Meteor Dragon', 2)]),
     ('Removal', [('Raigeki', 3), ('Widespread Ruin', 2)]), ('Field', [('Yami', 1)]),
     ('Power-ups', [('Megamorph', 3), ('Bright Castle', 3), ('Dark Energy', 3), ('Dragon Treasure', 3), ('Salamandra', 3)])],
    [(['Red-eyes B. Dragon', 'Meteor Dragon'], 'Meteor B. Dragon')], ['equips', 'completion'])

add('sand-mercury', 'Sand & Sorcery', 'Mystical Sand', '#E8C884', 'Fan synergy → equip-heavy endgame',
    'Female/Rock fusion routes, broad Mystical Sand equip compatibility and Mercury backups.',
    'Female materials + selected Rocks make Mystical Sand, but always check the actual pair: category exceptions exist. Wasteland + three Megamorphs reaches 5,600 ATK on a naturally drawn Mystical Sand. A two-material Sand fusion + two Megamorphs + Wasteland fills all five opening slots and reaches 4,600. Skull Knight and Zoa provide stronger late-game natural draws.',
    'Sand starts at only 2,100 ATK. This route needs much heavier support than Meteor or Thunder and may need several turns. Fan reports disagree on finishing with a pure female deck, so this is a supported hybrid rather than a promised pure-theme clear.',
    [('Female / Rock core and backups', [('Mystical Elf', 3), ('Ancient Elf', 3), ('Gemini Elf', 3), ('Giant Soldier of Stone', 3), ('Morphing Jar', 3), ('Stone Armadiller', 3), ('Skull Knight', 3), ('Zoa', 2), ('Mystical Sand', 2)]),
     ('Removal', [('Raigeki', 3), ('Widespread Ruin', 2)]), ('Field', [('Wasteland', 1)]),
     ('Power-ups', [('Megamorph', 3), ('Bright Castle', 2), ('Electro-whip', 2), ('Black Pendant', 2)])],
    [(['Mystical Elf', 'Giant Soldier of Stone'], 'Mystical Sand'), (['Ancient Elf', 'Morphing Jar'], 'Mystical Sand')], ['casual', 'female', 'farming'])

add('crimson-toolbox', 'Crimson Toolbox', 'Crimson Sunbird', '#79D9AB', 'Fan fusion web → upgraded endgame',
    'Winged Beast / fiery fusion routes with Zombie/Dragon/Thunder overlap and natural boss upgrades.',
    'Faith Bird + Darkfire Dragon makes Crimson Sunbird; Dragon Zombie + The Immortal of Thunder makes Twin-headed Thunder Dragon. Several low cards serve more than one fusion route. Mountain + two Megamorphs takes Crimson Sunbird to 4,800 ATK, but uses every slot in a five-card hand if the monster needs two materials. Upgrade into natural Meteor and Skull Knight when available.',
    'Crimson starts at 2,300 ATK, so a pure early toolbox plateaus. Follow Wind supports the Winged Beast routes, while Dragon Treasure supports the Dragon/Thunder backups; neither is universal. The source player used a 15-drop mod: only catalog-verified vanilla synergies are imported, not farming rates. Mountain can boost opposing Dragons.',
    [('Overlapping materials / boss upgrades', [('Darkfire Dragon', 3), ('Dragon Zombie', 3), ('Fire Reaper', 3), ('Faith Bird', 3), ('Mavelus', 3), ('Skull Red Bird', 3), ('The Immortal of Thunder', 3), ('Meteor B. Dragon', 2), ('Skull Knight', 2)]),
     ('Removal', [('Raigeki', 3), ('Widespread Ruin', 2)]), ('Field', [('Mountain', 1)]),
     ('Power-ups', [('Megamorph', 3), ('Bright Castle', 2), ('Follow Wind', 2), ('Dragon Treasure', 2)])],
    [(['Faith Bird', 'Darkfire Dragon'], 'Crimson Sunbird'), (['Dragon Zombie', 'The Immortal of Thunder'], 'Twin-headed Thunder Dragon')], ['crimson', 'farming', 'completion'])

destination = root / 'src/YfmCompanion.Engine/Research/campaign-decks.json'
destination.parent.mkdir(parents=True, exist_ok=True)
destination.write_text(json.dumps(builds, indent=2) + '\n', encoding='utf-8')
print(f'Validated {len(builds)} vanilla 40-card reference decks and all example fusion recipes.')
