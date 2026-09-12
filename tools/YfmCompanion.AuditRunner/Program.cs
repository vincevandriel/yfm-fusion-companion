using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using YfmCompanion.Data;
using YfmCompanion.Engine;
using YfmCompanion.RetroArch;

namespace YfmCompanion.AuditRunner;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private static async Task<int> Main(string[] args)
    {
        if (args.Length is < 2 or > 4)
        {
            Console.Error.WriteLine("Usage: YfmCompanion.AuditRunner <yfm.db> <output-directory> [memory-card.srm] [retroarch.cfg]");
            return 2;
        }

        var databasePath = Path.GetFullPath(args[0]);
        var outputDirectory = Path.GetFullPath(args[1]);
        var savePath = args.Length >= 3 && !string.IsNullOrWhiteSpace(args[2]) ? Path.GetFullPath(args[2]) : null;
        var configPath = args.Length >= 4 && !string.IsNullOrWhiteSpace(args[3]) ? Path.GetFullPath(args[3]) : null;
        Directory.CreateDirectory(outputDirectory);

        var checks = new List<AuditCheck>();
        FusionCatalog? catalog = null;
        SaveSnapshot? save = null;

        Run(checks, "Database", "Load shared read-only catalog", () =>
        {
            catalog = FusionCatalog.Load(databasePath);
            Require(catalog.Cards.Count == 722, $"Expected 722 cards; found {catalog.Cards.Count}.");
            Require(catalog.FusionPairs.Count == 25_146, $"Expected 25,146 fusion pairs; found {catalog.FusionPairs.Count}.");
            return $"722 cards and 25,146 resolved pairs loaded from {Path.GetFileName(databasePath)}.";
        });

        if (catalog is null)
        {
            return WriteReports(outputDirectory, databasePath, savePath, configPath, checks, []);
        }

        Run(checks, "Search", "Autocomplete ranking and bounded lookup", () =>
        {
            var search = new CardSearchService(catalog.Cards);
            var prefix = search.Search("twin");
            Require(prefix.Any(card => card.Name == "Twin-headed Thunder Dragon"), "Broad prefix lookup omitted Twin-headed Thunder Dragon.");
            Require(search.Search("Twin-headed Thunder Dragon")[0].Name == "Twin-headed Thunder Dragon", "Exact-name lookup was not ranked first.");
            Require(search.Search("613")[0].Name == "Twin-headed Thunder Dragon", "Card-number lookup did not resolve card 613.");
            Require(search.Search("dragon", 3).Count <= 3, "Maximum-result bound was not honored.");
            return "Name prefix, case-insensitive substring, card number, and result bound passed.";
        });

        Run(checks, "Fusion engine", "Golden pair and provenance resolution", () =>
        {
            var forward = catalog.Resolve("Petit Dragon", "The Immortal of Thunder", includeGlitches: false)
                ?? throw new InvalidOperationException("Golden pair did not resolve.");
            var reverse = catalog.Resolve("The Immortal of Thunder", "Petit Dragon", includeGlitches: false)
                ?? throw new InvalidOperationException("Reverse golden pair did not resolve.");
            Require(forward.Result.Name == "Thunder Dragon", "Golden pair did not produce Thunder Dragon.");
            Require(reverse.Result.Id == forward.Result.Id, "Pair resolution was not order-independent.");
            Require(forward.RuleReferences.Count > 0, "Fusion provenance was absent.");
            return $"Golden pair resolved to {forward.Result.Name} with {forward.RuleReferences.Count} provenance reference(s).";
        });

        Run(checks, "Turn planner", "Ordered hand chain and field-first chain", () =>
        {
            var petit = catalog.GetCard("Petit Dragon");
            var thunder = catalog.GetCard("The Immortal of Thunder");
            var baby = catalog.GetCard("Baby Dragon");
            var planner = new TacticalFusionPlanner(catalog);
            var handResults = planner.FindRecommendations([new(1, petit.Id), new(2, thunder.Id), new(3, baby.Id)]);
            Require(handResults.Any(result => result.FinalCard.Name == "Twin-headed Thunder Dragon" &&
                                              result.ConsumedHandSlots.SequenceEqual([1, 2, 3])),
                "The intended three-card ordered hand chain was absent.");
            var fieldResults = planner.FindRecommendations(
                [new(1, petit.Id), new(2, thunder.Id)],
                [new(FieldZone.Monster, 4, baby.Id)]);
            Require(fieldResults.Any(result => result.FinalCard.Name == "Twin-headed Thunder Dragon" &&
                                               result.Steps[0].Kind == TacticalStepKind.StartFromField),
                "The field-first fusion path was absent.");
            return $"{handResults.Count} hand route(s) and {fieldResults.Count} field-aware route(s) evaluated.";
        });

        Run(checks, "Turn planner", "Terminal equip semantics", () =>
        {
            var axe = catalog.GetCard("Axe of Despair");
            var battleOx = catalog.GetCard("Battle Ox");
            var result = new TacticalFusionPlanner(catalog)
                .FindRecommendations([new(1, axe.Id)], [new(FieldZone.Monster, 2, battleOx.Id)])
                .Single();
            Require(result.Steps[^1].Kind == TacticalStepKind.EquipFromHand, "Equip was not the terminal step.");
            Require(result.EffectiveAttack == battleOx.Attack + 500, "Equip attack bonus was incorrect.");
            Require(result.EffectiveDefense == battleOx.Defense + 500, "Equip defense bonus was incorrect.");
            return $"Axe of Despair remained terminal and produced {result.EffectiveAttack}/{result.EffectiveDefense}.";
        });

        Run(checks, "Strategy", "Fields, utility cards, and ritual boundary", () =>
        {
            var evaluator = new ForbiddenMemoriesStrategyEvaluator(catalog);
            Require(ForbiddenMemoriesStrategyEvaluator.GetFieldModifier(332, "Dragon") == 500, "Sogen Dragon modifier was incorrect.");
            Require(ForbiddenMemoriesStrategyEvaluator.GetFieldModifier(334, "Pyro") == -500, "Umi Pyro penalty was incorrect.");
            var widespreadRuin = evaluator.Assess(catalog.GetCard(686), new DeckOptimizationOptions());
            var ritual = evaluator.Assess(catalog.Cards.First(card => card.PrimaryType.Equals("Ritual", StringComparison.OrdinalIgnoreCase)), new DeckOptimizationOptions());
            Require(widespreadRuin.Tier == CardViabilityTier.Essential, "Widespread Ruin was not ranked as essential control.");
            Require(ritual.Tier == CardViabilityTier.NonViable, "Ritual was not excluded from normal profiles.");
            return $"Field modifiers passed; Widespread Ruin is {widespreadRuin.Tier}; normal ritual tier is {ritual.Tier}.";
        });

        Run(checks, "Deck analyzer", "Exact 40-card hand enumeration", () =>
        {
            var deck = Enumerable.Repeat(catalog.GetCard("Petit Dragon").Id, 10)
                .Concat(Enumerable.Repeat(catalog.GetCard("The Immortal of Thunder").Id, 10))
                .Concat(Enumerable.Repeat(catalog.GetCard("Baby Dragon").Id, 10))
                .Concat(Enumerable.Repeat(catalog.GetCard("Skull Servant").Id, 10));
            var report = new DeckAnalyzer(catalog).Analyze(deck, includeGlitches: false);
            Require(report.TotalHands == 658_008, $"Expected 658,008 hands; enumerated {report.TotalHands}.");
            Require(report.FusionResults.Any(result => result.Result.Name == "Twin-headed Thunder Dragon"), "Expected multi-step result was absent.");
            return $"658,008 physical hands; {report.AnyFusionProbability:P2} any fusion; {report.AtLeast2800Probability:P2} at least 2,800 ATK.";
        });

        if (savePath is not null && File.Exists(savePath))
        {
            Run(checks, "Saved snapshot", "Read and validate current memory card", () =>
            {
                var inspection = Ps1MemoryCardReader.Inspect(savePath);
                Require(inspection.IsValid, inspection.Error ?? "Unknown save-reader failure.");
                var snapshot = inspection.Snapshot ?? throw new InvalidOperationException("The valid save result had no snapshot.");
                Require(snapshot.DeckCardIds.Count == 40, "Saved constructed deck was not 40 cards.");
                Require(snapshot.ChestQuantities.Count == 722, "Saved chest did not contain 722 quantity slots.");
                save = snapshot;
                return $"Bank {snapshot.MemoryCardBank}, block {snapshot.BlockNumber}; 40-card deck and 722-card chest validated read-only.";
            });
        }
        else
        {
            checks.Add(AuditCheck.Skipped("Saved snapshot", "Read and validate current memory card", "No existing save path was supplied."));
        }

        if (save is not null)
        {
            Run(checks, "Saved snapshot", "Analyze the saved constructed deck", () =>
            {
                var report = new DeckAnalyzer(catalog).Analyze(save.DeckCardIds, includeGlitches: false);
                Require(report.TotalHands == 658_008, "Saved deck did not produce the full exact hand count.");
                return $"Saved deck: {report.AnyFusionProbability:P2} any fusion, {report.AtLeast2800Probability:P2} at least 2,800 ATK.";
            });
        }

        var currentOwned = BuildOwnedCollection(catalog, save);
        if (save is not null)
        {
            Run(checks, "Deck optimizer", "Current owned collection and deck comparison", () =>
            {
                var options = new DeckOptimizationOptions(
                    SampleHands: 24,
                    ExactFinalists: 1,
                    RandomSeed: 0x59464D,
                    IncludeGlitches: false);
                var report = new OwnedDeckOptimizer(catalog).Optimize(currentOwned, options, save.DeckCardIds);
                ValidateOptimizedDeck(report, currentOwned);
                var comparison = report.Comparison ?? throw new InvalidOperationException("Current-deck comparison was not produced.");
                return $"40 legal cards; any-fusion change {comparison.AnyFusionProbabilityChange:+0.00%;-0.00%;0.00%}.";
            });
        }

        var broadOwned = catalog.Cards.Select(card => new OwnedCardQuantity(card.Id, 3)).ToArray();
        var profiles = Enum.GetValues<DeckStrategyProfile>();
        var profileSummaries = new List<ProfileAudit>();
        var profileDecks = new HashSet<string>(StringComparer.Ordinal);
        foreach (var profile in profiles)
        {
            Run(checks, "Deck optimizer", $"Profile: {profile}", () =>
            {
                var options = new DeckOptimizationOptions(
                    SampleHands: 24,
                    ExactFinalists: 1,
                    RandomSeed: 0x59464D,
                    IncludeGlitches: false,
                    Profile: profile,
                    PreferredMonsterTypes: profile == DeckStrategyProfile.FieldAndType ? ["Dragon", "Thunder"] : null,
                    PreferredFieldCardId: profile == DeckStrategyProfile.FieldAndType ? 332 : null,
                    OpponentMonsterTypes: profile == DeckStrategyProfile.ControlAndSafety ? ["Dragon"] : null);
                var report = new OwnedDeckOptimizer(catalog).Optimize(broadOwned, options);
                ValidateOptimizedDeck(report, broadOwned);
                var expanded = report.Deck.SelectMany(entry => Enumerable.Repeat(entry.Card.Id, entry.Copies)).Order().ToArray();
                profileDecks.Add(string.Join(',', expanded));
                var topTarget = report.ImportantTargets.Count == 0 ? null : report.ImportantTargets[0];
                profileSummaries.Add(new ProfileAudit(
                    profile.ToString(),
                    report.ExactAnalysis.AnyFusionProbability,
                    report.ExactAnalysis.AtLeast2800Probability,
                    report.ExactAnalysis.ExpectedBestFusionAttack,
                    topTarget?.Result.Name));
                return $"40 legal cards; {report.ExactAnalysis.AnyFusionProbability:P2} any fusion; top target {topTarget?.Result.Name ?? "none"}.";
            });
        }

        Run(checks, "Deck optimizer", "Profiles influence broad-collection deck construction", () =>
        {
            Require(profileDecks.Count >= 2, "All strategy profiles produced the same deck despite a broad owned collection.");
            return $"Six profiles produced {profileDecks.Count} distinct legal deck constructions.";
        });

        await RunAsync(checks, "RetroArch", "Configuration and current read-only connection", async () =>
        {
            var resolvedConfig = configPath ?? RetroArchConfigInspector.FindConfigurationPath();
            if (resolvedConfig is null || !File.Exists(resolvedConfig))
            {
                throw new AuditSkipException("No RetroArch configuration was available on this machine.");
            }

            var configuration = RetroArchConfigInspector.Read(resolvedConfig);
            if (!configuration.NetworkCommandsEnabled)
            {
                throw new AuditSkipException("RetroArch configuration was parsed, but Network Commands are currently disabled.");
            }

            using var client = new RetroArchNetworkClient(port: configuration.NetworkCommandPort);
            RetroArchStatus status;
            try
            {
                status = await client.GetStatusAsync().ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is SocketException or TimeoutException)
            {
                throw new AuditSkipException($"RetroArch configuration was validated, but its local listener is not currently available: {exception.Message}");
            }

            if (!status.HasContent)
            {
                return $"Connected to 127.0.0.1:{configuration.NetworkCommandPort}; RetroArch is currently contentless.";
            }

            if (status.GameBasename?.Contains("Forbidden Memories", StringComparison.OrdinalIgnoreCase) != true)
            {
                throw new AuditSkipException($"RetroArch is available but currently running different content: {status.GameBasename ?? "unknown"}.");
            }

            ForbiddenMemoriesLiveSnapshot snapshot;
            try
            {
                snapshot = await new ForbiddenMemoriesLiveReader(client).ReadSnapshotAsync().ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is SocketException or TimeoutException)
            {
                throw new AuditSkipException($"Forbidden Memories was detected, but the local listener stopped responding during the live read: {exception.Message}");
            }

            Require(snapshot.HandCardIds.Count == 5, "Live reader did not return five ordered hand slots.");
            return $"{status.State}; duel active {snapshot.DuelActive}; five hand slots and separated field zones validated.";
        });

        return WriteReports(outputDirectory, databasePath, savePath, configPath, checks, profileSummaries);
    }

    private static OwnedCardQuantity[] BuildOwnedCollection(FusionCatalog catalog, SaveSnapshot? save)
    {
        if (save is not null)
        {
            var saved = catalog.Cards
                .Select(card => new OwnedCardQuantity(card.Id, save.GetTotalOwned(card.Id)))
                .Where(item => item.Quantity > 0)
                .ToArray();
            var legalCapacity = saved.Sum(item => Math.Min(item.Quantity, OwnedDeckOptimizer.LegalCopyLimitForCard(item.CardId)));
            if (legalCapacity >= 40)
            {
                return saved;
            }
        }

        return [.. catalog.Cards.Select(card => new OwnedCardQuantity(card.Id, 3))];
    }

    private static void ValidateOptimizedDeck(DeckOptimizationReport report, IReadOnlyCollection<OwnedCardQuantity> owned)
    {
        var ownedByCard = owned.ToDictionary(item => item.CardId, item => item.Quantity);
        var expanded = report.Deck.SelectMany(entry => Enumerable.Repeat(entry.Card.Id, entry.Copies)).ToArray();
        Require(expanded.Length == 40, "Optimizer did not return exactly 40 cards.");
        foreach (var group in expanded.GroupBy(cardId => cardId))
        {
            var available = ownedByCard[group.Key];
            Require(group.Count() <= Math.Min(available, OwnedDeckOptimizer.LegalCopyLimitForCard(group.Key)),
                $"Card #{group.Key:000} exceeded ownership or legal copy limits.");
        }

        Require(report.ExactAnalysis.TotalHands == 658_008, "Finalist was not exactly analyzed.");
    }

    private static void Run(List<AuditCheck> checks, string section, string name, Func<string> action)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var detail = action();
            checks.Add(AuditCheck.Passed(section, name, stopwatch.Elapsed.TotalMilliseconds, detail));
        }
        catch (AuditSkipException exception)
        {
            checks.Add(AuditCheck.Skipped(section, name, exception.Message, stopwatch.Elapsed.TotalMilliseconds));
        }
        catch (Exception exception)
        {
            checks.Add(AuditCheck.Failed(section, name, stopwatch.Elapsed.TotalMilliseconds, exception.Message));
        }
    }

    private static async Task RunAsync(List<AuditCheck> checks, string section, string name, Func<Task<string>> action)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var detail = await action().ConfigureAwait(false);
            checks.Add(AuditCheck.Passed(section, name, stopwatch.Elapsed.TotalMilliseconds, detail));
        }
        catch (AuditSkipException exception)
        {
            checks.Add(AuditCheck.Skipped(section, name, exception.Message, stopwatch.Elapsed.TotalMilliseconds));
        }
        catch (Exception exception)
        {
            checks.Add(AuditCheck.Failed(section, name, stopwatch.Elapsed.TotalMilliseconds, exception.Message));
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static int WriteReports(
        string outputDirectory,
        string databasePath,
        string? savePath,
        string? configPath,
        IReadOnlyCollection<AuditCheck> checks,
        IReadOnlyCollection<ProfileAudit> profiles)
    {
        var report = new IntegrationAuditReport(
            DateTimeOffset.Now,
            Environment.MachineName,
            Environment.Version.ToString(),
            databasePath,
            savePath,
            configPath,
            checks.All(check => check.Outcome != AuditOutcome.Fail),
            checks,
            profiles);
        File.WriteAllText(
            Path.Combine(outputDirectory, "integration-audit.json"),
            JsonSerializer.Serialize(report, JsonOptions),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.WriteAllText(
            Path.Combine(outputDirectory, "integration-audit.md"),
            BuildMarkdown(report),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        Console.WriteLine($"Integration audit: {(report.Passed ? "PASS" : "FAIL")} ({checks.Count(check => check.Outcome == AuditOutcome.Pass)} passed, {checks.Count(check => check.Outcome == AuditOutcome.Skip)} skipped, {checks.Count(check => check.Outcome == AuditOutcome.Fail)} failed)");
        return report.Passed ? 0 : 1;
    }

    private static string BuildMarkdown(IntegrationAuditReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Phase 9 integration and performance audit");
        builder.AppendLine();
        builder.AppendLine($"- Result: **{(report.Passed ? "PASS" : "FAIL")}**");
        builder.AppendLine($"- Generated: {report.GeneratedAt:O}");
        builder.AppendLine($"- Runtime: .NET {report.DotNetVersion}");
        builder.AppendLine($"- Database: `{report.DatabasePath}`");
        builder.AppendLine();
        builder.AppendLine("| Section | Check | Outcome | Time (ms) | Detail |");
        builder.AppendLine("|---|---|---:|---:|---|");
        foreach (var check in report.Checks)
        {
            builder.AppendLine($"| {Escape(check.Section)} | {Escape(check.Name)} | {check.Outcome} | {check.DurationMilliseconds.ToString("N1", CultureInfo.InvariantCulture)} | {Escape(check.Detail)} |");
        }

        if (report.Profiles.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Optimizer profile results");
            builder.AppendLine();
            builder.AppendLine("| Profile | Any fusion | At least 2800 | Expected best ATK | Top target |");
            builder.AppendLine("|---|---:|---:|---:|---|");
            foreach (var profile in report.Profiles)
            {
                builder.AppendLine($"| {profile.Profile} | {profile.AnyFusionProbability:P2} | {profile.AtLeast2800Probability:P2} | {profile.ExpectedBestAttack:N1} | {Escape(profile.TopTarget ?? "none")} |");
            }
        }

        return builder.ToString();
    }

    private static string Escape(string value) => value.Replace("|", "\\|", StringComparison.Ordinal).ReplaceLineEndings(" ");

    private sealed class AuditSkipException(string message) : Exception(message);
}

internal enum AuditOutcome
{
    Pass,
    Skip,
    Fail
}

internal sealed record AuditCheck(
    string Section,
    string Name,
    AuditOutcome Outcome,
    double DurationMilliseconds,
    string Detail)
{
    public static AuditCheck Passed(string section, string name, double milliseconds, string detail) =>
        new(section, name, AuditOutcome.Pass, milliseconds, detail);

    public static AuditCheck Skipped(string section, string name, string detail, double milliseconds = 0) =>
        new(section, name, AuditOutcome.Skip, milliseconds, detail);

    public static AuditCheck Failed(string section, string name, double milliseconds, string detail) =>
        new(section, name, AuditOutcome.Fail, milliseconds, detail);
}

internal sealed record ProfileAudit(
    string Profile,
    double AnyFusionProbability,
    double AtLeast2800Probability,
    double ExpectedBestAttack,
    string? TopTarget);

internal sealed record IntegrationAuditReport(
    DateTimeOffset GeneratedAt,
    string MachineName,
    string DotNetVersion,
    string DatabasePath,
    string? SavePath,
    string? RetroArchConfigPath,
    bool Passed,
    IReadOnlyCollection<AuditCheck> Checks,
    IReadOnlyCollection<ProfileAudit> Profiles);
