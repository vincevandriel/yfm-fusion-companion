using YfmCompanion.Data;
using YfmCompanion.Engine;
using YfmCompanion.RetroArch;

if (args.Length is < 1 or > 2 || !File.Exists(args[0]) || (args.Length == 2 && !File.Exists(args[1])))
{
    Console.Error.WriteLine("Usage: YfmCompanion.LiveProbe <yfm.db> [memory-card.srm]");
    return 2;
}

var catalog = FusionCatalog.Load(Path.GetFullPath(args[0]));
using var client = new RetroArchNetworkClient();
var snapshot = await new ForbiddenMemoriesLiveReader(client).ReadSnapshotAsync();
Console.WriteLine($"{snapshot.Status.State} | duel active: {snapshot.DuelActive} | save loaded: {snapshot.SaveDataAvailable} | game state 0x{snapshot.GameState:X4} | LP {snapshot.PlayerLifePoints}/{snapshot.OpponentLifePoints} | terrain {TerrainName(snapshot.TerrainId)}");
Console.WriteLine("Hand:");
foreach (var item in snapshot.HandCardIds.Select((cardId, index) => (cardId, slot: index + 1)).Where(item => item.cardId > 0))
{
    var card = catalog.GetCard(item.cardId);
    Console.WriteLine($"  H{item.slot}: #{card.Id:000} {card.Name} ({card.PrimaryType}, {card.Attack}/{card.Defense})");
}

Console.WriteLine("Player field:");
foreach (var field in snapshot.PlayerField)
{
    var card = catalog.GetCard(field.CardId);
    var terrainModifier = snapshot.TerrainId is >= 1 and <= 6
        ? ForbiddenMemoriesStrategyEvaluator.GetFieldModifier(329 + snapshot.TerrainId, card.PrimaryType)
        : 0;
    Console.WriteLine($"  F{field.Slot}: #{card.Id:000} {card.Name} ({card.PrimaryType}, printed {field.Attack}/{field.Defense}, power {field.PowerModifier:+#;-#;0}, effective {Math.Max(0, field.Attack + field.PowerModifier + terrainModifier)}/{Math.Max(0, field.Defense + field.PowerModifier + terrainModifier)})");
}

Console.WriteLine("Player spell/trap field:");
foreach (var field in snapshot.PlayerSpellTrapField)
{
    var card = catalog.GetCard(field.CardId);
    Console.WriteLine($"  S{field.Slot}: #{card.Id:000} {card.Name} ({card.PrimaryType})");
}

Console.WriteLine("Opponent field:");
foreach (var field in snapshot.OpponentField)
{
    var card = catalog.GetCard(field.CardId);
    var terrainModifier = snapshot.TerrainId is >= 1 and <= 6
        ? ForbiddenMemoriesStrategyEvaluator.GetFieldModifier(329 + snapshot.TerrainId, card.PrimaryType)
        : 0;
    Console.WriteLine($"  F{field.Slot}: #{card.Id:000} {card.Name} ({card.PrimaryType}, printed {field.Attack}/{field.Defense}, power {field.PowerModifier:+#;-#;0}, effective {Math.Max(0, field.Attack + field.PowerModifier + terrainModifier)}/{Math.Max(0, field.Defense + field.PowerModifier + terrainModifier)})");
}

if (snapshot.DuelActive)
{
    Console.WriteLine("Spell/trap/equip cards in shuffled deck order:");
    foreach (var item in snapshot.ShuffledDeckCardIds.Select((cardId, index) => (cardId, position: index + 1)))
    {
        if (item.cardId is < 1 or > 722)
        {
            continue;
        }

        var card = catalog.GetCard(item.cardId);
        if (IsSpellOrTrap(card.PrimaryType))
        {
            Console.WriteLine($"  D{item.position}: #{card.Id:000} {card.Name} ({card.PrimaryType})");
        }
    }
}

if (int.TryParse(Environment.GetEnvironmentVariable("YFM_DRAWN_CARD_COUNT"), out var drawnCardCount) &&
    drawnCardCount is >= 0 and < 40)
{
    var futureDraws = snapshot.ShuffledDeckCardIds
        .Select((cardId, index) => (cardId, position: index + 1))
        .Skip(drawnCardCount)
        .TakeWhile(item => item.cardId != 333)
        .Where(item => item.cardId is >= 1 and <= 722)
        .ToArray();
    Console.WriteLine($"Upcoming valid cards after D{drawnCardCount}:");
    foreach (var item in snapshot.ShuffledDeckCardIds
                 .Select((cardId, index) => (cardId, position: index + 1))
                 .Skip(drawnCardCount)
                 .Where(item => item.cardId is >= 1 and <= 722)
                 .Take(10))
    {
        var card = catalog.GetCard(item.cardId);
        var beastFangsTarget = catalog.CanEquip(308, card.Id) ? " • Beast Fangs compatible" : string.Empty;
        Console.WriteLine($"  D{item.position}: #{card.Id:000} {card.Name} ({card.PrimaryType}, {card.Attack}/{card.Defense}){beastFangsTarget}");
    }
    Console.WriteLine($"Direct fusion opportunities after D{drawnCardCount} and before Sogen:");
    foreach (var handItem in snapshot.HandCardIds
                 .Select((cardId, index) => (cardId, slot: index + 1))
                 .Where(item => item.cardId is >= 1 and <= 722)
                 .GroupBy(item => item.cardId)
                 .Select(group => (cardId: group.Key, slots: string.Join('/', group.Select(item => $"H{item.slot}")))))
    {
        var handCard = catalog.GetCard(handItem.cardId);
        var hits = futureDraws
            .Select(item => (item, resolution: catalog.Resolve(handCard.Id, item.cardId, includeGlitches: true)))
            .Where(hit => hit.resolution is not null)
            .ToArray();
        Console.WriteLine($"  {handItem.slots} {handCard.Name}: {hits.Length}");
        foreach (var hit in hits)
        {
            var draw = catalog.GetCard(hit.item.cardId);
            var result = hit.resolution!.Result;
            Console.WriteLine($"    D{hit.item.position} {draw.Name} -> {result.Name} ({result.Attack}/{result.Defense})");
            foreach (var (laterCardId, laterPosition) in futureDraws.Where(item => item.position > hit.item.position))
            {
                var continuation = catalog.Resolve(result.Id, laterCardId, includeGlitches: true);
                if (continuation is not null)
                {
                    Console.WriteLine($"      then D{laterPosition} {catalog.GetCard(laterCardId).Name} -> {continuation.Result.Name} ({continuation.Result.Attack}/{continuation.Result.Defense})");
                }
            }
        }
    }
}

var monsters = snapshot.PlayerField
    .Select(field => new FieldCard(FieldZone.Monster, field.Slot, field.CardId));
var spells = snapshot.PlayerSpellTrapField
    .Select(field => new FieldCard(FieldZone.SpellTrap, field.Slot, field.CardId));
var hand = snapshot.HandCardIds
    .Select((cardId, index) => new HandCard(index + 1, cardId))
    .Where(card => card.CardId > 0);
var recommendations = new TacticalFusionPlanner(catalog)
    .FindRecommendations(hand, monsters, spells, includeGlitches: true);
Console.WriteLine($"Recommendations: {recommendations.Count}");
foreach (var recommendation in recommendations.Take(25))
{
    var route = string.Join(" -> ", recommendation.Steps.Select(FormatStep));
    Console.WriteLine($"  {recommendation.FinalCard.Name} {recommendation.EffectiveAttack}/{recommendation.EffectiveDefense} | {route}");
}

if (args.Length == 2)
{
    var saveResult = Ps1MemoryCardReader.Inspect(Path.GetFullPath(args[1]));
    if (!saveResult.IsValid || saveResult.Snapshot is null)
    {
        Console.Error.WriteLine($"Save comparison failed: {saveResult.Error}");
        return 3;
    }

    var save = saveResult.Snapshot;
    var deckOrderMatches = snapshot.ConstructedDeckCardIds.SequenceEqual(save.DeckCardIds);
    var liveDeckCounts = snapshot.ConstructedDeckCardIds.GroupBy(cardId => cardId).ToDictionary(group => group.Key, group => group.Count());
    var saveDeckCounts = save.DeckCardIds.GroupBy(cardId => cardId).ToDictionary(group => group.Key, group => group.Count());
    var deckContentsMatch = liveDeckCounts.Count == saveDeckCounts.Count &&
        liveDeckCounts.All(pair => saveDeckCounts.GetValueOrDefault(pair.Key) == pair.Value);
    var chestMatches = snapshot.ChestQuantities.SequenceEqual(save.ChestQuantities.Select(quantity => (int)quantity));
    Console.WriteLine($"Live/save constructed deck contents: {(deckContentsMatch ? "MATCH" : "DIFFERENT")}");
    Console.WriteLine($"Live/save constructed deck order: {(deckOrderMatches ? "MATCH" : "DIFFERENT (same contents may be ordered differently in RAM)")}");
    Console.WriteLine($"Live/save chest collection: {(chestMatches ? "MATCH" : "DIFFERENT")}");
    if (!deckContentsMatch || !chestMatches)
    {
        var allCardIds = liveDeckCounts.Keys.Union(saveDeckCounts.Keys).OrderBy(cardId => cardId);
        foreach (var cardId in allCardIds.Where(cardId => liveDeckCounts.GetValueOrDefault(cardId) != saveDeckCounts.GetValueOrDefault(cardId)))
        {
            Console.WriteLine($"  #{cardId:000}: live {liveDeckCounts.GetValueOrDefault(cardId)}, save {saveDeckCounts.GetValueOrDefault(cardId)}");
        }

        return 4;
    }
}

return 0;

static bool IsSpellOrTrap(string type) => type is "Magic" or "Spell" or "Trap" or "Equip" or "Ritual";

static string TerrainName(int terrainId) => terrainId switch
{
    1 => "Forest",
    2 => "Wasteland",
    3 => "Mountain",
    4 => "Sogen",
    5 => "Umi",
    6 => "Yami",
    _ => "Normal"
};

static string FormatStep(TacticalStep step) => step.Kind switch
{
    TacticalStepKind.StartFromHand => $"H{step.SourceSlot} {step.Material.Name}",
    TacticalStepKind.StartFromField => $"field {step.SourceSlot} {step.Material.Name}",
    TacticalStepKind.FuseFromHand => $"H{step.SourceSlot} {step.Material.Name} = {step.Result.Name}",
    TacticalStepKind.EquipFromHand => $"H{step.SourceSlot} {step.Material.Name} = {step.Result.Name} (+{step.AttackBonus})",
    TacticalStepKind.FuseOntoField => $"field {step.SourceSlot} {step.Material.Name} = {step.Result.Name}",
    TacticalStepKind.EquipOntoField => $"field {step.SourceSlot} {step.Material.Name} = {step.Result.Name} (+{step.AttackBonus})",
    _ => step.Result.Name
};
