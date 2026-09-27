# Balanced support-card review

2026-09-26. Read-only review of current production source and artifacts/yfm.db. No scoring changes made by this review.

Finding: current optimizer partially values support, but cannot enforce or accurately evaluate the requested one-field/two-equip, 3500+ ATK and board-clear plan.

- Balanced search mode controls the time budget (one minute). Strategy profile and campaign goal independently choose the scoring objective. Campaign goals force ControlAndSafety.
- ForbiddenMemoriesStrategyEvaluator.Assess assigns positive heuristic values to useful equips, fields, Raigeki, Dark Hole and attack traps; low threshold traps are penalized. Preferred fields and types improve seed scores, not legality requirements.
- DeckQuantitySpace.IsLegal enforces 40 cards, owned/purchasable capacity, copy limits and chip budget. It does not require fields, equips, board clears or an achievable attack threshold.
- DeckAnalyzer.Explore records a compatible equip as a terminal result and never recurses through the equipped state. It models one final equip, not stacked equips. Field bonuses are absent from these exact hand attack counts.
- DeckObjectiveComparer.ComparePower ranks 3000/2800/2500/2000 attack counts and fusion availability, then expected fusion attack. There is no 3500 threshold or support-readiness metric. Without campaign contexts, removal utility does not enter final candidate comparison directly, despite influencing seed generation.
- Campaign safety does use reachable single-equip fusion outcomes. OpponentSafetyScoring explicitly recognizes Raigeki, Widespread Ruin and Crush Card, but not Dark Hole, which has zero attack and falls through to zero counter value. This differs from its strong strategy assessment.
- MainWindow supplies the preferred field as ActiveFieldCardId to safety contexts. Safety applies that terrain to both monsters and enemy threats, but does not condition it on including, drawing or playing the field. This can overestimate attainable coverage.
- Safety scores are heuristic coverage, not a duel simulation or win guarantee. Strong monsters, defense position, guardian choices, opponent field bonuses and turn order can change an actual answer.

Verified local catalog examples: Twin-headed Thunder Dragon (613) has 2800 ATK and Thunder type; Mountain (332) and Umi (334) give Thunder +500 under the current field rules. A compatible ordinary equip adds 500: 2800 + 500 + 500 = 3800. Two such equips with that field would total 4300 if legally established; their stacked value is currently outside the analyzer model. Megamorph (657) is modeled as +1000. Blue-eyes Ultimate Dragon (380) has 4500 base ATK, so 3500 is a floor, not universal late-game coverage.

Needed correction: owned-card-aware minimum one useful field and two compatible equip copies; validated final-monster compatibility and field selection; accurate stacked-equip and terrain outcomes with availability distinguished from already-active setup; achievable 3500+ ATK probabilities and answers against actual stronger opponents; honest board-clear/trap coverage and setup turn costs; explicit infeasibility/missing-card reporting instead of counting unavailable cards or guaranteeing victory. New objective/constraint identities must invalidate incompatible proof checkpoints. Preserve the four-search/eight-analysis policy, deterministic exact counts, cancellation and bounded caches.

This review has not re-parsed the current save or simulated gameplay; it establishes model behavior from source, not the best legal package in the user's collection.
