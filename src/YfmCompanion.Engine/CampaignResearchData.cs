using System.Text.Json;
using System.Text.Json.Serialization;

namespace YfmCompanion.Engine;

public sealed record OpponentAiEvidence(
    bool LowMage,
    bool HighMage,
    bool AggressiveFieldSpellBehavior,
    string Confidence,
    string ImplementationNote);

public sealed record OpponentReference(
    int DuelistId,
    string Name,
    int OpeningHandSize,
    string OptimizerScope,
    OpponentAiEvidence AiEvidence,
    IReadOnlyList<OpponentDeckPoolEntry> DeckPool);

public sealed record CampaignOptimizerPolicy(
    string Objective,
    IReadOnlySet<int> GeneralSafetyDuelistIds,
    IReadOnlySet<int> FinalGauntletDuelistIds,
    bool StarChipModeDefaultEnabled,
    string StarChipModeBehavior,
    IReadOnlyList<string> RequiredOutputGuards,
    string NearEqualTieBreak);

public sealed record CampaignResearchData(
    DateTimeOffset GeneratedAtUtc,
    string OpponentSource,
    string OpponentSourceRevision,
    IReadOnlyDictionary<int, OpponentReference> Opponents,
    CampaignOptimizerPolicy Policy)
{
    public const string BundledDirectoryName = "ResearchData";

    public static CampaignResearchData LoadBundled(string baseDirectory) =>
        Load(Path.Combine(Path.GetFullPath(baseDirectory), BundledDirectoryName));

    public static CampaignResearchData Load(string researchDataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(researchDataDirectory);
        var directory = Path.GetFullPath(researchDataDirectory);
        var opponent = Read<OpponentFile>(Path.Combine(directory, "opponent_reference.json"));
        var policy = Read<PolicyFile>(Path.Combine(directory, "optimizer_policy.json"));
        var guardian = Read<GuardianFile>(Path.Combine(directory, "guardian_star_rules.json"));
        var manifest = Read<ManifestFile>(Path.Combine(directory, "source_manifest.json"));

        ValidateSchema(opponent.SchemaVersion, "opponent_reference.json");
        ValidateSchema(policy.SchemaVersion, "optimizer_policy.json");
        ValidateSchema(guardian.SchemaVersion, "guardian_star_rules.json");
        ValidateSchema(manifest.SchemaVersion, "source_manifest.json");
        ValidateGuardianRules(guardian);

        if (opponent.Opponents is null ||
            policy.GeneralSafetyDuelistIds is null ||
            policy.FinalGauntletDuelistIds is null ||
            policy.StarChipMode is null ||
            policy.RequiredOutputGuards is null ||
            manifest.Sources is null)
        {
            throw new InvalidDataException("Campaign research data is missing a required collection or policy section.");
        }

        if (opponent.OpponentCount != 39 || opponent.Opponents.Count != 39)
        {
            throw new InvalidDataException("Opponent research data must contain exactly 39 duelists.");
        }

        var duplicateDuelist = opponent.Opponents
            .GroupBy(item => item.DuelistId)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateDuelist is not null)
        {
            throw new InvalidDataException($"Opponent research data repeats duelist ID {duplicateDuelist.Key}.");
        }

        var expectedIds = Enumerable.Range(1, 39).ToHashSet();
        var actualIds = opponent.Opponents.Select(item => item.DuelistId).ToHashSet();
        if (!expectedIds.SetEquals(actualIds))
        {
            throw new InvalidDataException("Opponent research data must contain each duelist ID from 1 through 39 exactly once.");
        }

        var generalIds = policy.GeneralSafetyDuelistIds.ToHashSet();
        var gauntletIds = policy.FinalGauntletDuelistIds.ToHashSet();
        if (generalIds.Overlaps(gauntletIds) ||
            !expectedIds.SetEquals(generalIds.Concat(gauntletIds)) ||
            generalIds.Count != 33 ||
            gauntletIds.Count != 6)
        {
            throw new InvalidDataException("Optimizer policy must split all 39 duelists into 33 general-safety and 6 final-gauntlet opponents.");
        }

        var opponents = new Dictionary<int, OpponentReference>();
        foreach (var item in opponent.Opponents)
        {
            if (string.IsNullOrWhiteSpace(item.Name) ||
                item.OpeningHandSize is < 1 or > 40 ||
                item.AiFlags is null ||
                item.DeckPool is null)
            {
                throw new InvalidDataException($"Duelist {item.DuelistId} has invalid identifying or opening-hand data.");
            }

            if (item.DeckPool.Count == 0 ||
                item.DeckPool.Any(entry => entry.CardId is < 1 or > 722 || entry.Weight <= 0) ||
                item.DeckPool.Select(entry => entry.CardId).Distinct().Count() != item.DeckPool.Count)
            {
                throw new InvalidDataException($"Duelist {item.DuelistId} has an invalid weighted Deck pool.");
            }

            var expectedScope = generalIds.Contains(item.DuelistId)
                ? "general_safety_candidate"
                : "final_gauntlet_only";
            if (!string.Equals(item.OptimizerScope, expectedScope, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Duelist {item.DuelistId} has optimizer scope '{item.OptimizerScope}', expected '{expectedScope}'.");
            }

            opponents.Add(item.DuelistId, new OpponentReference(
                item.DuelistId,
                item.Name,
                item.OpeningHandSize,
                item.OptimizerScope,
                new OpponentAiEvidence(
                    item.AiFlags.LowMage,
                    item.AiFlags.HighMage,
                    item.AiFlags.AggressiveFieldSpellBehavior,
                    item.AiFlags.Confidence,
                    item.AiFlags.ImplementationNote),
                item.DeckPool.Select(entry => new OpponentDeckPoolEntry(entry.CardId, entry.Weight)).ToArray()));
        }

        var source = manifest.Sources.SingleOrDefault(item => item.Id == opponent.Source)
            ?? throw new InvalidDataException($"Source manifest does not contain opponent source '{opponent.Source}'.");
        if (string.IsNullOrWhiteSpace(source.Revision) ||
            !source.Revision.Equals(opponent.SourceRevision, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Opponent source revision does not match the source manifest.");
        }

        return new CampaignResearchData(
            opponent.GeneratedAtUtc,
            opponent.Source,
            opponent.SourceRevision,
            opponents,
            new CampaignOptimizerPolicy(
                policy.UserSelectedObjective,
                generalIds,
                gauntletIds,
                policy.StarChipMode.DefaultEnabled,
                policy.StarChipMode.BehaviorWhenEnabled,
                policy.RequiredOutputGuards,
                policy.NearEqualTieBreak));
    }

    private static T Read<T>(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Required campaign research data is missing.", path);
        }

        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions)
                ?? throw new InvalidDataException($"Research data file '{Path.GetFileName(path)}' is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Research data file '{Path.GetFileName(path)}' is invalid JSON.", exception);
        }
    }

    private static void ValidateSchema(int schemaVersion, string fileName)
    {
        if (schemaVersion != 1)
        {
            throw new InvalidDataException($"Unsupported schema version {schemaVersion} in {fileName}; expected 1.");
        }
    }

    private static void ValidateGuardianRules(GuardianFile guardian)
    {
        if (guardian.BattleModifier is null ||
            guardian.Cycles is null ||
            guardian.Symbols is null ||
            guardian.BattleModifier.Amount != GuardianStarRules.AdvantageModifier ||
            guardian.Cycles.Count != 2)
        {
            throw new InvalidDataException("Guardian-star research data does not match the verified runtime rules.");
        }

        foreach (var cycle in guardian.Cycles)
        {
            if (!cycle.BeatsNext || cycle.Order.Count < 2)
            {
                throw new InvalidDataException("Guardian-star research data contains an invalid cycle.");
            }

            foreach (var star in cycle.Order)
            {
                if (!guardian.Symbols.TryGetValue(star, out var symbol) ||
                    !GuardianStarRules.TryGetSymbol(star, out var runtimeSymbol) ||
                    !symbol.Equals(runtimeSymbol, StringComparison.Ordinal))
                {
                    throw new InvalidDataException($"Guardian-star symbol for '{star}' does not match the runtime rules.");
                }
            }
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip
    };

    private sealed record OpponentFile(
        [property: JsonPropertyName("schema_version")] int SchemaVersion,
        [property: JsonPropertyName("generated_at_utc")] DateTimeOffset GeneratedAtUtc,
        [property: JsonPropertyName("source")] string Source,
        [property: JsonPropertyName("source_revision")] string SourceRevision,
        [property: JsonPropertyName("opponent_count")] int OpponentCount,
        [property: JsonPropertyName("opponents")] IReadOnlyList<OpponentItem> Opponents);

    private sealed record OpponentItem(
        [property: JsonPropertyName("duelist_id")] int DuelistId,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("opening_hand_size")] int OpeningHandSize,
        [property: JsonPropertyName("optimizer_scope")] string OptimizerScope,
        [property: JsonPropertyName("ai_flags")] AiFlagsItem AiFlags,
        [property: JsonPropertyName("deck_pool")] IReadOnlyList<DeckPoolItem> DeckPool);

    private sealed record AiFlagsItem(
        [property: JsonPropertyName("low_mage")] bool LowMage,
        [property: JsonPropertyName("high_mage")] bool HighMage,
        [property: JsonPropertyName("aggressive_field_spell_behavior")] bool AggressiveFieldSpellBehavior,
        [property: JsonPropertyName("confidence")] string Confidence,
        [property: JsonPropertyName("implementation_note")] string ImplementationNote);

    private sealed record DeckPoolItem(
        [property: JsonPropertyName("card_id")] int CardId,
        [property: JsonPropertyName("weight")] int Weight);

    private sealed record PolicyFile(
        [property: JsonPropertyName("schema_version")] int SchemaVersion,
        [property: JsonPropertyName("user_selected_objective")] string UserSelectedObjective,
        [property: JsonPropertyName("general_safety_duelist_ids")] IReadOnlyList<int> GeneralSafetyDuelistIds,
        [property: JsonPropertyName("final_gauntlet_duelist_ids")] IReadOnlyList<int> FinalGauntletDuelistIds,
        [property: JsonPropertyName("star_chip_mode")] StarChipModeItem StarChipMode,
        [property: JsonPropertyName("required_output_guards")] IReadOnlyList<string> RequiredOutputGuards,
        [property: JsonPropertyName("near_equal_tie_break")] string NearEqualTieBreak);

    private sealed record StarChipModeItem(
        [property: JsonPropertyName("default_enabled")] bool DefaultEnabled,
        [property: JsonPropertyName("behavior_when_enabled")] string BehaviorWhenEnabled);

    private sealed record GuardianFile(
        [property: JsonPropertyName("schema_version")] int SchemaVersion,
        [property: JsonPropertyName("battle_modifier")] BattleModifierItem BattleModifier,
        [property: JsonPropertyName("cycles")] IReadOnlyList<GuardianCycleItem> Cycles,
        [property: JsonPropertyName("symbols")] IReadOnlyDictionary<string, string> Symbols);

    private sealed record BattleModifierItem([property: JsonPropertyName("amount")] int Amount);

    private sealed record GuardianCycleItem(
        [property: JsonPropertyName("order")] IReadOnlyList<string> Order,
        [property: JsonPropertyName("beats_next")] bool BeatsNext);

    private sealed record ManifestFile(
        [property: JsonPropertyName("schema_version")] int SchemaVersion,
        [property: JsonPropertyName("sources")] IReadOnlyList<ManifestSourceItem> Sources);

    private sealed record ManifestSourceItem(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("revision")] string? Revision = null);
}
