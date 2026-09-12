using YfmCompanion.Data;

namespace YfmCompanion.Tests;

internal static class TestCatalogFactory
{
    public static Card Card(int id, string name, int attack = 0, int defense = 0, string primaryType = "Test") =>
        new(id, name, null, null, null, null, primaryType, null, attack, defense, null, null, false, true, false);

    public static FusionPair Pair(int first, int second, int result, bool glitch = false) =>
        new(Math.Min(first, second), Math.Max(first, second), result, !glitch, glitch);

    public static FusionCatalog Create(
        IEnumerable<Card> cards,
        IEnumerable<FusionPair> pairs,
        IEnumerable<(int EquipCardId, int EquippedCardId)>? equips = null) =>
        new(cards, pairs, equipCompatibility: equips);
}
