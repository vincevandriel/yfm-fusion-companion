using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using YfmCompanion.Data;
using YfmCompanion.Engine;
using YfmCompanion.RetroArch;

// Independent acceptance probes. A nonzero exit means the release gate is blocked,
// even if the application's pre-existing regression suite passes.
if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: Phase4Audit <repository-root> <result-json>");
    return 2;
}

var root = Path.GetFullPath(args[0]);
var output = Path.GetFullPath(args[1]);
var checks = new List<AuditCheck>();
var options = new DeckOptimizationOptions(SampleHands: 1, ExactFinalists: 1, IncludeGlitches: false);
var scratch = Path.Combine(Path.GetTempPath(), "YfmPhase4Audit-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(scratch);

Run("P4-01", "Do not spend chips on a strategically identical substitute", () =>
{
    var cards = Enumerable.Range(1, 15).Select(id => Monster(id, 1000) with
    {
        Password = id == 1 ? "00000001" : null,
        StarchipCost = id == 1 ? 1 : null
    }).ToArray();
    var catalog = new FusionCatalog(cards, []);
    var owned = Enumerable.Range(2, 14).Select(id => new OwnedCardQuantity(id, 3)).ToArray();
    var planner = new StarChipDeckPlanner(catalog);
    var baseline = planner.Plan(owned, 1, false, options);
    var purchase = planner.Plan(owned, 1, true, options);
    return (purchase.SpentStarChips == 0,
        $"Spent {purchase.SpentStarChips}; all monsters have identical stats, stars, types and no fusions/equips. " +
        $"Baseline/result expected fusion ATK: {baseline.ResultingDeck.ExactAnalysis.ExpectedBestFusionAttack}/{purchase.ResultingDeck.ExactAnalysis.ExpectedBestFusionAttack}.");
});

Run("P4-02", "Find an affordable legal deck before claiming none exists", () =>
{
    var cards = Enumerable.Range(1, 13).Select(id => Monster(id, 100)).Concat([
        Monster(100, 3000) with { Password = "00000100", StarchipCost = 100 },
        Monster(101, 100) with { Password = "00000101", StarchipCost = 50 },
        Monster(102, 100) with { Password = "00000102", StarchipCost = 50 }
    ]).ToArray();
    var catalog = new FusionCatalog(cards, []);
    var owned = Enumerable.Range(1, 13).Select(id => new OwnedCardQuantity(id, id == 13 ? 2 : 3)).ToArray();
    var witness = new OwnedDeckOptimizer(catalog).Optimize(
        owned.Concat([new OwnedCardQuantity(101, 1), new OwnedCardQuantity(102, 1)]), options);
    try
    {
        var result = new StarChipDeckPlanner(catalog).Plan(owned, 100, true, options);
        return (result.ResultingDeck.TotalCards == 40, $"Returned {result.ResultingDeck.TotalCards} cards.");
    }
    catch (InvalidOperationException exception)
    {
        return (false, $"Planner rejected the collection: {exception.Message} Witness: buy 101 + 102 for 50 + 50 = 100, yielding {witness.TotalCards} legal cards.");
    }
});

Run("P4-03", "Include sequential opponent fusion threats", () =>
{
    var catalog = new FusionCatalog(
        [Monster(1, 100), Monster(2, 100), Monster(3, 100), Monster(4, 1000), Monster(5, 3000)],
        [new FusionPair(1, 2, 4, true, false), new FusionPair(3, 4, 5, true, false)]);
    var report = new OpponentThreatEvaluator(catalog).Evaluate([
        new OpponentDeckPoolEntry(1, 700), new OpponentDeckPoolEntry(2, 700), new OpponentDeckPoolEntry(3, 648)]);
    return (report.FusionThreats.Any(threat => threat.Result.Id == 5),
        $"Pool contains 1,2,3. Legal chain 1+2=4; 4+3=5. Reported result IDs: {string.Join(',', report.FusionThreats.Select(threat => threat.Result.Id))}.");
});

Run("P4-04", "Reject guardian data that reverses a verified cycle", () =>
    ValidateMutation("reversed-stars", "guardian_star_rules.json", json =>
    {
        var cycle = json["cycles"]![0]!["order"]!.AsArray();
        var reversed = cycle.Select(node => node!.GetValue<string>()).Reverse().ToArray();
        cycle.Clear();
        foreach (var star in reversed) cycle.Add(star);
    }));

Run("P4-05", "Reject swapped normal-campaign and gauntlet IDs", () =>
{
    var directory = CopyResearch("swapped-scope");
    Mutate(directory, "optimizer_policy.json", json =>
    {
        var general = json["general_safety_duelist_ids"]!.AsArray();
        general[0] = 33;
        json["final_gauntlet_duelist_ids"]![0] = 1;
    });
    Mutate(directory, "opponent_reference.json", json =>
    {
        foreach (var opponent in json["opponents"]!.AsArray())
        {
            var id = opponent!["duelist_id"]!.GetValue<int>();
            if (id == 1) opponent["optimizer_scope"] = "final_gauntlet_only";
            if (id == 33) opponent["optimizer_scope"] = "general_safety_candidate";
        }
    });
    return Rejects(directory);
});

Run("P4-06", "Reject invalid opponent pool totals", () =>
    ValidateMutation("bad-pool", "opponent_reference.json", json =>
    {
        var entry = json["opponents"]![0]!["deck_pool"]![0]!;
        entry["weight"] = entry["weight"]!.GetValue<int>() + 100;
    }));

Run("P4-07", "Reject an invalid password string before recommending purchase", () =>
{
    var cards = Enumerable.Range(1, 14).Select(id => Monster(id, id == 14 ? 3000 : 100) with
    {
        Password = id == 14 ? "abcdefgh" : null,
        StarchipCost = id == 14 ? 100 : null
    });
    try
    {
        var plan = new StarChipDeckPlanner(new FusionCatalog(cards, [])).Plan(
            Enumerable.Range(1, 13).Select(id => new OwnedCardQuantity(id, 3)), 100, true, options);
        return (plan.Purchases.All(purchase => purchase.Card.Password!.All(char.IsAsciiDigit)),
            $"Purchase passwords: {string.Join(',', plan.Purchases.Select(purchase => purchase.Card.Password))}.");
    }
    catch (InvalidOperationException)
    {
        return (true, "No valid affordable purchase; rejected.");
    }
});

Run("P4-08", "Do not turn malformed auxiliary save data into a spendable balance", () =>
{
    var bytes = CreateSave(uint.MaxValue, emptyDeck: false);
    var snapshots = Ps1MemoryCardReader.Parse(Path.Combine(scratch, "synthetic.srm"), DateTime.UnixEpoch, bytes);
    var snapshot = snapshots.SingleOrDefault();
    return (snapshot is not null && snapshot.StarChips != uint.MaxValue && snapshot.Warnings.Count > 0,
        snapshot is null ? "Entire snapshot rejected." : $"Accepted balance: {snapshot.StarChips}; warnings: {string.Join(';', snapshot.Warnings)}.");
});

Run("P4-09", "Preserve a valid chest when the saved deck is empty", () =>
{
    var bytes = CreateSave(100, emptyDeck: true);
    var snapshots = Ps1MemoryCardReader.Parse(Path.Combine(scratch, "empty-deck.srm"), DateTime.UnixEpoch, bytes);
    return (snapshots.Count == 1, $"Parsed {snapshots.Count} snapshots from structurally valid matching save copies with 42 chest cards and zero deck IDs.");
});

Run("P4-10", "Count a guaranteed removal answer in campaign safety", () =>
{
    var cards = Enumerable.Range(1, 14).Select(id => Monster(id, 100)).Append(
        Monster(337, 0) with { Name = "Raigeki", Defense = 0, PrimaryType = "Magic" });
    var catalog = new FusionCatalog(cards, []);
    var context = new OpponentSafetyContext("Synthetic unbeatable attacker", [1], ["Warrior"],
        [new DeckSafetyTarget(1, "Synthetic opponent", 99, "Threat", 3000, ["Sun", "Mars"], 1, false)], "Audit fixture");
    var safetyOptions = options with { Profile = DeckStrategyProfile.ControlAndSafety, SafetyContext = context };
    var filler = Enumerable.Range(1, 13).Select(id => new OwnedCardQuantity(id, 3)).ToArray();
    var baseline = new OwnedDeckOptimizer(catalog).Optimize(filler.Append(new OwnedCardQuantity(14, 1)), safetyOptions);
    var withRemoval = new OwnedDeckOptimizer(catalog).Optimize(filler.Append(new OwnedCardQuantity(337, 1)), safetyOptions);
    return (withRemoval.SafetyAssessment!.HeuristicScore > baseline.SafetyAssessment!.HeuristicScore,
        $"Exactly 40 owned cards in both cases, no fusions. Safety without/with Raigeki: {baseline.SafetyAssessment!.HeuristicScore}/{withRemoval.SafetyAssessment!.HeuristicScore}.");
});

Run("P4-C01", "Read-only parsing leaves the source unchanged", () =>
{
    var bytes = CreateSave(100, emptyDeck: false);
    var before = SHA256.HashData(bytes);
    var result = Ps1MemoryCardReader.Parse(Path.Combine(scratch, "read-only.srm"), DateTime.UnixEpoch, bytes);
    return (result.Count == 1 && before.SequenceEqual(SHA256.HashData(bytes)), "Input bytes compared by SHA-256 before and after parsing.");
});

Run("P4-C02", "Exact duplicate-hand weighting matches independent enumeration", () =>
{
    var catalog = new FusionCatalog([Monster(1, 100), Monster(2, 100), Monster(3, 100), Monster(4, 2100)],
        [new FusionPair(1, 2, 4, true, false)]);
    int[] deck = [1, 1, 1, 2, 2, 3, 3, 3];
    long total = 0, successful = 0;
    for (var a = 0; a < deck.Length - 4; a++)
        for (var b = a + 1; b < deck.Length - 3; b++)
            for (var c = b + 1; c < deck.Length - 2; c++)
                for (var d = c + 1; d < deck.Length - 1; d++)
                    for (var e = d + 1; e < deck.Length; e++)
                    {
                        int[] hand = [deck[a], deck[b], deck[c], deck[d], deck[e]];
                        total++;
                        if (hand.Contains(1) && hand.Contains(2)) successful++;
                    }
    var result = new DeckAnalyzer(catalog).Analyze(deck, false);
    return (result.TotalHands == total && Math.Abs(result.AnyFusionProbability - (double)successful / total) < 1e-12,
        $"Independent {successful}/{total}; engine {result.AnyFusionProbability:R} over {result.TotalHands} hands.");
});

Run("P4-C03", "Cancellation interrupts exact analysis", () =>
{
    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();
    try
    {
        new DeckAnalyzer(new FusionCatalog([Monster(1, 100)], [])).Analyze(
            Enumerable.Repeat(1, 40), cancellationToken: cancellation.Token);
        return (false, "Completed despite cancellation.");
    }
    catch (OperationCanceledException)
    {
        return (true, "OperationCanceledException observed.");
    }
});

var report = new
{
    SchemaVersion = 1,
    BaselineCommit = "974cce4",
    SourceSha256 = Directory.GetFiles(Path.Combine(root, "src"), "*", SearchOption.AllDirectories)
        .Where(path => !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) &&
                       !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
        .Where(path => Path.GetExtension(path) is ".cs" or ".xaml" or ".csproj")
        .Order(StringComparer.Ordinal)
        .ToDictionary(path => Path.GetRelativePath(root, path).Replace('\\', '/'),
            path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant()),
    GeneratedAtUtc = DateTimeOffset.UtcNow,
    ReleaseGate = checks.All(check => check.Passed) ? "PASS" : "BLOCKED",
    Passed = checks.Count(check => check.Passed),
    Failed = checks.Count(check => !check.Passed),
    Checks = checks
};
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
return checks.All(check => check.Passed) ? 0 : 1;

void Run(string id, string requirement, Func<(bool Passed, string Evidence)> probe)
{
    var timer = Stopwatch.StartNew();
    try
    {
        var result = probe();
        checks.Add(new AuditCheck(id, requirement, result.Passed, result.Evidence, timer.ElapsedMilliseconds));
    }
    catch (Exception exception)
    {
        checks.Add(new AuditCheck(id, requirement, false, $"Unexpected {exception.GetType().Name}: {exception.Message}", timer.ElapsedMilliseconds));
    }
}

string CopyResearch(string name)
{
    var directory = Path.Combine(scratch, name);
    Directory.CreateDirectory(directory);
    foreach (var file in Directory.GetFiles(Path.Combine(root, "docs", "research", "data"), "*.json"))
        File.Copy(file, Path.Combine(directory, Path.GetFileName(file)));
    return directory;
}

static void Mutate(string directory, string name, Action<JsonNode> mutation)
{
    var path = Path.Combine(directory, name);
    var json = JsonNode.Parse(File.ReadAllText(path))!;
    mutation(json);
    File.WriteAllText(path, json.ToJsonString());
}

(bool, string) ValidateMutation(string name, string file, Action<JsonNode> mutation)
{
    var directory = CopyResearch(name);
    Mutate(directory, file, mutation);
    return Rejects(directory);
}

static (bool, string) Rejects(string directory)
{
    try
    {
        CampaignResearchData.Load(directory);
        return (false, "Malformed research data was accepted.");
    }
    catch (InvalidDataException)
    {
        return (true, "Malformed research data rejected with InvalidDataException.");
    }
}

static Card Monster(int id, int attack) =>
    new(id, $"Synthetic {id}", null, "Sun", "Mars", 4, "Warrior", null, attack, 1000, null, null, true, true, true);

static byte[] CreateSave(uint chips, bool emptyDeck)
{
    var bytes = new byte[Ps1MemoryCardReader.MemoryCardBankSize];
    bytes[0] = (byte)'M';
    bytes[1] = (byte)'C';
    const int directory = 0x80;
    bytes[directory] = 0x51;
    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(directory + 4, 4), Ps1MemoryCardReader.BlockSize);
    BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(directory + 8, 2), ushort.MaxValue);
    Encoding.ASCII.GetBytes(Ps1MemoryCardReader.ForbiddenMemoriesSaveName).CopyTo(bytes, directory + 10);
    for (var index = 0; index < 0x7F; index++) bytes[directory + 0x7F] ^= bytes[directory + index];
    const int block = Ps1MemoryCardReader.BlockSize;
    bytes[block] = (byte)'S';
    bytes[block + 1] = (byte)'C';
    var copy = new byte[Ps1MemoryCardReader.SaveCopyLength];
    if (!emptyDeck)
        for (var slot = 0; slot < 40; slot++)
            BinaryPrimitives.WriteUInt16LittleEndian(copy.AsSpan(slot * 2, 2), (ushort)(1 + slot / 3));
    if (emptyDeck)
        for (var id = 0; id < 14; id++) copy[Ps1MemoryCardReader.ChestOffset + id] = 3;
    BinaryPrimitives.WriteUInt32LittleEndian(copy.AsSpan(Ps1MemoryCardReader.StarChipsOffset, 4), chips);
    copy.CopyTo(bytes, block + Ps1MemoryCardReader.FirstSaveCopyOffset);
    copy.CopyTo(bytes, block + Ps1MemoryCardReader.SecondSaveCopyOffset);
    return bytes;
}

internal sealed record AuditCheck(string Id, string Requirement, bool Passed, string Evidence, long ElapsedMilliseconds);
