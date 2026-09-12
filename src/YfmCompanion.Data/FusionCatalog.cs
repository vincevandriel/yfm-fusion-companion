using Microsoft.Data.Sqlite;

namespace YfmCompanion.Data;

public sealed class FusionCatalog
{
    private readonly Dictionary<int, Card> _cards;
    private readonly Dictionary<string, Card> _cardsByName;
    private readonly Dictionary<(int Low, int High), FusionPair> _pairs;
    private readonly int[,] _pairResults = new int[723, 723];
    private readonly byte[,] _pairFlags = new byte[723, 723];
    private readonly Dictionary<(int Low, int High), IReadOnlyList<FusionRuleReference>> _ruleReferences;
    private readonly HashSet<(int EquipCardId, int EquippedCardId)> _equipCompatibility;
    private readonly Dictionary<int, IReadOnlyList<string>> _categories;

    public FusionCatalog(
        IEnumerable<Card> cards,
        IEnumerable<FusionPair> fusionPairs,
        IEnumerable<((int Low, int High) Pair, FusionRuleReference Reference)>? ruleReferences = null,
        IEnumerable<(int EquipCardId, int EquippedCardId)>? equipCompatibility = null,
        IEnumerable<(int CardId, string Category)>? categories = null)
    {
        _cards = cards.ToDictionary(card => card.Id);
        _cardsByName = _cards.Values.ToDictionary(card => card.Name, StringComparer.OrdinalIgnoreCase);
        _pairs = [];
        foreach (var pair in fusionPairs)
        {
            if (pair.MaterialLowId > pair.MaterialHighId)
            {
                throw new InvalidDataException(
                    $"Fusion pair {pair.MaterialLowId}+{pair.MaterialHighId} is not canonical; the lower card ID must be first.");
            }

            if (!_pairs.TryAdd((pair.MaterialLowId, pair.MaterialHighId), pair))
            {
                throw new InvalidDataException(
                    $"Duplicate fusion pair {pair.MaterialLowId}+{pair.MaterialHighId}.");
            }
        }
        _ruleReferences = (ruleReferences ?? [])
            .GroupBy(item => item.Pair)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<FusionRuleReference>)[.. group.Select(item => item.Reference)]);
        _equipCompatibility = [.. (equipCompatibility ?? [])];
        _categories = (categories ?? [])
            .GroupBy(item => item.CardId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)[.. group.Select(item => item.Category).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)]);

        foreach (var pair in _pairs.Values)
        {
            if (!_cards.ContainsKey(pair.MaterialLowId) ||
                !_cards.ContainsKey(pair.MaterialHighId) ||
                !_cards.ContainsKey(pair.ResultCardId))
            {
                throw new InvalidDataException("A fusion pair references a missing card.");
            }

            _pairResults[pair.MaterialLowId, pair.MaterialHighId] = pair.ResultCardId;
            _pairResults[pair.MaterialHighId, pair.MaterialLowId] = pair.ResultCardId;
            var flags = (byte)((pair.IsIntended ? 1 : 0) | (pair.IsGlitch ? 2 : 0));
            _pairFlags[pair.MaterialLowId, pair.MaterialHighId] = flags;
            _pairFlags[pair.MaterialHighId, pair.MaterialLowId] = flags;
        }
    }

    public IReadOnlyCollection<Card> Cards => _cards.Values;
    public IReadOnlyCollection<FusionPair> FusionPairs => _pairs.Values;

    public Card GetCard(int cardId) => _cards.TryGetValue(cardId, out var card)
        ? card
        : throw new KeyNotFoundException($"Unknown card ID {cardId}.");

    public Card GetCard(string cardName) => _cardsByName.TryGetValue(cardName, out var card)
        ? card
        : throw new KeyNotFoundException($"Unknown card '{cardName}'.");

    public FusionResolution? Resolve(int firstCardId, int secondCardId, bool includeGlitches = true)
    {
        var first = GetCard(firstCardId);
        var second = GetCard(secondCardId);
        var key = NormalizePair(firstCardId, secondCardId);
        if (!_pairs.TryGetValue(key, out var pair) || (!includeGlitches && pair.IsGlitch))
        {
            return null;
        }

        _ruleReferences.TryGetValue(key, out var references);
        return new FusionResolution(
            first,
            second,
            GetCard(pair.ResultCardId),
            pair.IsIntended,
            pair.IsGlitch,
            references ?? []);
    }

    public bool TryResolvePair(
        int firstCardId,
        int secondCardId,
        bool includeGlitches,
        out int resultCardId,
        out bool isGlitch)
    {
        if (firstCardId is < 1 or > 722 || !_cards.ContainsKey(firstCardId))
        {
            throw new KeyNotFoundException($"Unknown card ID {firstCardId}.");
        }

        if (secondCardId is < 1 or > 722 || !_cards.ContainsKey(secondCardId))
        {
            throw new KeyNotFoundException($"Unknown card ID {secondCardId}.");
        }

        resultCardId = _pairResults[firstCardId, secondCardId];
        isGlitch = (_pairFlags[firstCardId, secondCardId] & 2) != 0;
        return resultCardId != 0 && (includeGlitches || !isGlitch);
    }

    public FusionResolution? Resolve(string firstCardName, string secondCardName, bool includeGlitches = true) =>
        Resolve(GetCard(firstCardName).Id, GetCard(secondCardName).Id, includeGlitches);

    public bool CanEquip(int equipCardId, int equippedCardId) =>
        _equipCompatibility.Contains((equipCardId, equippedCardId));

    public EquipResolution? ResolveEquip(int firstCardId, int secondCardId)
    {
        var first = GetCard(firstCardId);
        var second = GetCard(secondCardId);
        if (CanEquip(firstCardId, secondCardId))
        {
            var bonus = EquipBonus(firstCardId);
            return new EquipResolution(first, second, bonus, bonus);
        }

        if (CanEquip(secondCardId, firstCardId))
        {
            var bonus = EquipBonus(secondCardId);
            return new EquipResolution(second, first, bonus, bonus);
        }

        return null;
    }

    public CardAdvancedDetails GetAdvancedDetails(int cardId)
    {
        var card = GetCard(cardId);
        _categories.TryGetValue(cardId, out var categories);
        var fusionPartners = _pairs.Values.Count(pair =>
            pair.MaterialLowId == cardId || pair.MaterialHighId == cardId);
        var recipes = _pairs.Values.Count(pair => pair.ResultCardId == cardId);
        var canEquip = _equipCompatibility.Count(pair => pair.EquipCardId == cardId);
        var equippedBy = _equipCompatibility.Count(pair => pair.EquippedCardId == cardId);
        return new CardAdvancedDetails(
            card,
            categories ?? [],
            fusionPartners,
            recipes,
            canEquip,
            equippedBy);
    }

    public static FusionCatalog Load(string databasePath)
    {
        using var connection = new SqliteConnection($"Data Source={Path.GetFullPath(databasePath)};Mode=ReadOnly;Pooling=False");
        connection.Open();
        var cards = LoadCards(connection);
        var pairs = LoadPairs(connection);
        var references = LoadReferences(connection);
        var equips = LoadEquipCompatibility(connection);
        var categories = LoadCategories(connection);
        return new FusionCatalog(cards, pairs, references, equips, categories);
    }

    private static Card[] LoadCards(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT card_id, card_name, description, guardian_star_1, guardian_star_2,
                   level, primary_type, attribute, attack, defense, password,
                   starchip_cost, droppable, fusible, starter_available
            FROM cards ORDER BY card_id;
            """;
        using var reader = command.ExecuteReader();
        var cards = new List<Card>();
        while (reader.Read())
        {
            cards.Add(new Card(
                reader.GetInt32(0), reader.GetString(1), NullableString(reader, 2),
                NullableString(reader, 3), NullableString(reader, 4), NullableInt(reader, 5),
                reader.GetString(6), NullableString(reader, 7), reader.GetInt32(8), reader.GetInt32(9),
                NullableString(reader, 10), NullableInt(reader, 11), reader.GetBoolean(12),
                reader.GetBoolean(13), reader.GetBoolean(14)));
        }

        return [.. cards];
    }

    private static FusionPair[] LoadPairs(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT material_low_id, material_high_id, result_card_id, is_intended, is_glitch
            FROM fusion_pairs ORDER BY material_low_id, material_high_id;
            """;
        using var reader = command.ExecuteReader();
        var pairs = new List<FusionPair>();
        while (reader.Read())
        {
            pairs.Add(new FusionPair(
                reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2),
                reader.GetBoolean(3), reader.GetBoolean(4)));
        }

        return [.. pairs];
    }

    private static List<((int Low, int High) Pair, FusionRuleReference Reference)> LoadReferences(
        SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT refs.material_low_id, refs.material_high_id, refs.rule_id, refs.tier_id,
                   rules.rule_kind, rules.material_1_expression, rules.material_2_expression
            FROM fusion_pair_rule_refs refs
            JOIN fusion_rules rules ON rules.rule_id = refs.rule_id
            ORDER BY refs.material_low_id, refs.material_high_id, rules.source_order, refs.tier_id;
            """;
        using var reader = command.ExecuteReader();
        var references = new List<((int, int), FusionRuleReference)>();
        while (reader.Read())
        {
            references.Add(((reader.GetInt32(0), reader.GetInt32(1)), new FusionRuleReference(
                reader.GetInt32(2), NullableInt(reader, 3), reader.GetString(4),
                reader.GetString(5), reader.GetString(6))));
        }

        return references;
    }

    private static List<(int EquipCardId, int EquippedCardId)> LoadEquipCompatibility(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT rules.equip_card_id, compatibility.equipped_card_id
            FROM equip_compatibility compatibility
            JOIN equip_rules rules USING (equip_rule_id)
            ORDER BY rules.equip_card_id, compatibility.equipped_card_id;
            """;
        using var reader = command.ExecuteReader();
        var pairs = new List<(int, int)>();
        while (reader.Read())
        {
            pairs.Add((reader.GetInt32(0), reader.GetInt32(1)));
        }

        return pairs;
    }

    private static List<(int CardId, string Category)> LoadCategories(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT membership.card_id, category.category_name
            FROM card_categories membership
            JOIN categories category USING (category_id)
            ORDER BY membership.card_id, category.category_name COLLATE NOCASE;
            """;
        using var reader = command.ExecuteReader();
        var categories = new List<(int, string)>();
        while (reader.Read())
        {
            categories.Add((reader.GetInt32(0), reader.GetString(1)));
        }

        return categories;
    }

    private static (int Low, int High) NormalizePair(int first, int second) =>
        first <= second ? (first, second) : (second, first);

    private static int EquipBonus(int equipCardId) => equipCardId == 657 ? 1_000 : 500;

    private static string? NullableString(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static int? NullableInt(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
}
