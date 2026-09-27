# Advanced campaign deck building: community research and implementation

Reviewed 27 September 2026. Scope: the original PlayStation Forbidden Memories rules and this companion's resolved card catalog. This review combines firsthand fan deck discussions, a reported vanilla completion, and established routing guidance. It does not import modern TCG rules, fan-remake balance, modified fusion tables, or boosted-drop farming times.

## Findings that change deck construction

**Build a connected set of useful routes, not an arbitrary monster/spell/trap ratio.** A card earns its slot by helping form a worthwhile body, supporting actual outcomes, or answering a threat. Shared materials can provide a fallback when a hand fails to contain the preferred pair. The [Crimson Sunbird toolbox discussion](https://www.reddit.com/r/YugiohFMR/comments/1h1trow/) is particularly useful: its player deliberately connects Winged Beast, fire, Zombie, Dragon and Thunder lines instead of using two disconnected material piles. Replies also identify the ceiling of a 2,300 ATK boss. The player later disclosed a 15-drop mod, so the implementation accepts only fusion routes independently resolved by the local vanilla catalog and rejects the thread's farming-duration implications.

**Count the physical cards spent on the body.** A strong two-material route leaves three opening-hand slots for support. A three-material route leaves two. A naturally drawn boss leaves four. The analyzer explores actual ordered chains and removes consumed materials before counting equips or a field. It never counts a material twice. This makes material efficiency emerge from the available hand rather than from a guessed flat bonus. A [vanilla completion account](https://www.reddit.com/r/YugiohFMR/comments/1r6anmw/) describes moving from early fallback fusions to efficient Thunder routes and then to Meteor monsters and better equips. Individual successful games do not establish a reliable completion probability.

**An endgame body is not necessarily a fusion.** The [Seto 3 fan deck and explanation](https://gamefaqs.gamespot.com/boards/561010-yu-gi-oh-forbidden-memories/70354905) combines Meteor, Metalzoa, Skull Knight and Zoa with overlapping equips. It illustrates direct draws preserving resources and guardian choices changing combat. Its S/A-POW farming goal differs from campaign survival: omitting Raigeki for rank reasons is not adopted. The app's lists retain removal and explicitly measure support-only hands. Its reported success rate is anecdotal, not a calibration target.

**Use both a campaign floor and an endgame power test.** The previous 3,500 setup test remains useful, but the new endgame metric requires strictly more than 4,500 raw ATK. Exactly 4,500 does not pass; equal attack is not a clean combat win. This threshold is a reference against an unboosted Blue-eyes Ultimate Dragon, not a promise to defeat an equipped or terrain-boosted one. Guardian matchups remain in the existing opponent assessment. For example, Mercury is advantageous against Sun but neutral against Venus; a monster offering Mercury does not get an automatic 500 points against every enemy. [Fan suggestions about Zoa](https://www.reddit.com/r/YugiohFMR/comments/1r6anmw/) motivated examining this distinction, and the app's guardian cycles validate the actual matchups.

**Equip compatibility matters more than visual appearance or primary type.** Broad equips reduce the risk of drawing an unusable support card. Narrow equips can still be good when the deck repeatedly produces their target. Each physical compatible equip contributes 500 points, or 1,000 for Megamorph, after the final monster is formed. A field contributes at most its single applicable modifier. The local compatibility table, rather than a general “all Dragons” or “all female cards” shortcut, decides whether an equip works. One beneficial field and two compatible equips remain a soft structure preference; they are not a universal ratio or a hard legality rule.

**Fields affect the matchup, not just your attack number.** Mountain can help opposing Dragons as well as your own. Yami supports the Mercury Fiend/Spellcaster package while avoiding a Dragon terrain bonus for the enemy. The library explains these tradeoffs. Reference setup metrics describe your cards in hand; they do not simulate the enemy's resulting terrain modifier or assume your preferred field is already active. Real duel recommendations and the existing threat assessment remain separate from this opening-hand calculation.

**Keep recovery and defensive utility distinct.** Raigeki and Dark Hole are counted as board clears in hand, with the existing preference for Raigeki because Dark Hole also destroys your board. Widespread Ruin is a broad attack-triggered answer, not an instant wipe. Threshold-limited traps, type removal and Crush Card retain their existing conditional threat scoring rather than being treated as universal clears. More removal displaces monsters or equips; neither a spell quota nor a fan farming-rank recipe settles that tradeoff.

**Fan alternatives need progression plans.** [Casual non-Thunder alternatives](https://www.reddit.com/r/YugiohFMR/comments/1ffhm89/) discuss female/Rock and other early fusion packages. [Female-deck experiences](https://www.reddit.com/r/YugiohFMR/comments/1ma0lr3/) disagree about whether the theme can finish without changing course. The [farming discussion](https://www.reddit.com/r/YugiohFMR/comments/1gpb937/) also shows players relying on Sand, Pumpking and Crimson before hitting progression walls. The app therefore offers equip-heavy hybrids with clear upgrade notes, rather than claiming every pure theme reliably clears the final gauntlet. The [starter guide](https://cyberdemon531.com/yugiohfm/guide-1/) remains a useful baseline, while the library extends beyond its main Thunder route.

## Six app-authored reference lists

These are synthesis builds, not copied community lists or empirically proven optimal decks. Each contains exactly 40 physical cards, uses at most three copies per card, includes a beneficial field and compatible equips, and excludes cards marked unobtainable from drops in the catalog. Natural fused-monster cards in a list must actually be owned; being able to fuse the hero does not count as owning a copy. No rare drop rate or time-to-farm is promised.

| Build | Main synergy | Progression / tradeoff |
| --- | --- | --- |
| Thunder Tide | Repeated actual two-card Thunder routes; Umi and shared equips | Accessible route that still needs power-ups and removal for the ending |
| Meteor Crossfire | Red-eyes + Meteor; materials also support Thunder routes | Farmed bosses and Mercury backups; Mountain is situational against enemy Dragons |
| Mercury Eclipse | Skull Knight / Zoa / Dark Magician with shared dark equips | Guardian-aware alternative; Mercury is not an advantage against every star |
| Iron & Meteor | Natural bosses, dense equips and Yami | Fewer material slots, more finishing power, higher acquisition cost and support-only draw risk |
| Sand & Sorcery | Female/Rock routes, wide Sand compatibility, Mercury backups | Equip-heavy fan hybrid; stronger late-game draws replace weak pure-theme hands |
| Crimson Toolbox | Winged/fire routes overlapping Zombie/Dragon/Thunder routes | Early fan fusion web with natural boss upgrades; narrow equips must match the route |

Examples independently checked against the resolved catalog:

- Crawling Dragon + Electric Snake → Twin-headed Thunder Dragon.
- Red-eyes B. Dragon + Meteor Dragon → Meteor B. Dragon.
- Mystical Elf + Giant Soldier of Stone → Mystical Sand.
- Ancient Elf + Morphing Jar → Mystical Sand.
- Faith Bird + Darkfire Dragon → Crimson Sunbird.
- Dragon Zombie + The Immortal of Thunder → Twin-headed Thunder Dragon.

The full lists, source links, copy quantities, example recipes and cautions are in the embedded `src/YfmCompanion.Engine/Research/campaign-decks.json`. `tools/Create-CampaignDeckLibrary.py` resolves and validates the authored lists against the read-only database. This does not infer missing ownership from fusion materials or edit a game save.

## Changes to the optimizer

Balanced, Control and Safety, and Field and Type use the revised support objective. It combines the following per-hand counts, normalized by the actual number of evaluated hands:

| Feature | Heuristic weight |
| --- | ---: |
| 3,500+ potential setup OR a board clear in hand | 35 |
| 2,800+ natural or fused body, before field/equips | 20 |
| Strictly >4,500 potential setup OR a board clear in hand | 15 |
| Broad removal in hand | 10 |
| 3,500+ potential setup | 10 |
| Strictly >4,500 potential setup | 5 |
| Board clear in hand | 5 |
| No natural or creatable monster in hand | -15 |

The existing compatible-support structure contributes up to ten additional points. The weights are app design choices informed by the tradeoffs above, not values estimated from community win rates. The unions count each hand once. A zero-ATK defensive monster is still a monster, not a support-only hand. Legacy fusion metrics remain unchanged and separately labelled. Specialized Fusion Consistency, Maximum Power and Ritual profiles retain their specialized objective; the fan library does not silently override their purpose.

Timed search now evaluates independently adapted reference starts before its normal mutations. Missing reference cards are replaced using a legal fallback from the frozen inventory. Each challenger must satisfy the same ownership, copy and purchase-budget checks and compete under the same sample seed and objective. Selecting a build gives it the first starting position; it is not a hard theme lock, and another strategy may win. Pause retains which starts have completed. Automatic search still uses up to four workers; exact analysis uses up to eight. Each worker retains independent analyzer state, and integer counts merge deterministically.

The objective and analysis rules have new version identities. Earlier proof checkpoints are preserved but cannot be reused as proofs under the new scoring; the existing new-checkpoint workflow creates a fresh search. Timed results remain “best found,” never global optimality claims.

## Deck library interaction

Open **Owned-Card Optimizer → Recommended Decks**. Six distinct card-artwork icons work offline. Below each button, ownership is `sum(min(required copies, owned copies)) / 40`; deck and chest copies come from the selected save source, or quantities come from manual mode. Counts refresh when that collection changes, and source text preserves stale/missing/manual status.

Selecting an icon shows the full list, required/owned/missing copies, the route and upgrade plan, limitations and source links. **Check all opening hands** evaluates the complete reference list over all 658,008 physical five-card hands, including missing cards, and labels that scope. It can be cancelled; selecting another build or closing the window cancels it. **Adapt this strategy to my collection** selects an owned-card search start. It neither fills missing ownership quantities nor installs cards into the game, and changing the start is blocked while a job is active or paused.

## Validation and limits

See `docs/benchmarks/campaign-deck-library.json` for exact reference metrics, identical serial/eight-worker results, the previous/revised heuristic scores on the same lists, and an adapted owned-card Quick search followed by exact verification. Tests separately cover physical-copy counts, missing-card replacement, actual recipes and compatibility, strict attack ties, support-only draws, independent physical-hand enumeration, worker equality and frozen input identity. WPF rendering checks distinct icons, ownership refresh, missing details, selection, active-job protection and readable narrow-screen columns.

These checks establish rules/model behavior. They do not measure campaign win rates, model full duels, prove the best possible deck, account for every enemy equip/field/response, or certify native OS-DPI switching. Unpaused gameplay coexistence was not retested for this update. The fan completion and toolbox experiences supply strategy ideas, not controlled outcome data.
