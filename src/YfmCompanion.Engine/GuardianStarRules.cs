namespace YfmCompanion.Engine;

public enum GuardianStarOutcome
{
    Unknown,
    Disadvantage,
    Neutral,
    Advantage
}

public sealed record GuardianStarChain(
    string WeakTo,
    string Selected,
    string StrongAgainst,
    string WeakToSymbol,
    string SelectedSymbol,
    string StrongAgainstSymbol)
{
    public string CompactNotation => $"{WeakToSymbol} > {SelectedSymbol} > {StrongAgainstSymbol}";
}

public static class GuardianStarRules
{
    public const int AdvantageModifier = 500;

    private static readonly string[][] Cycles =
    [
        ["Sun", "Moon", "Venus", "Mercury"],
        ["Mars", "Jupiter", "Saturn", "Uranus", "Pluto", "Neptune"]
    ];

    private static readonly IReadOnlyDictionary<string, string> Symbols =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Mars"] = "♂",
            ["Jupiter"] = "♃",
            ["Saturn"] = "♄",
            ["Uranus"] = "⚲",
            ["Pluto"] = "♇",
            ["Neptune"] = "♆",
            ["Mercury"] = "☿",
            ["Sun"] = "☉",
            ["Moon"] = "☾",
            ["Venus"] = "♀"
        };

    public static GuardianStarOutcome Resolve(string? attacker, string? defender)
    {
        if (!TryNormalize(attacker, out var attackerName) || !TryNormalize(defender, out var defenderName))
        {
            return GuardianStarOutcome.Unknown;
        }

        if (attackerName.Equals(defenderName, StringComparison.OrdinalIgnoreCase))
        {
            return GuardianStarOutcome.Neutral;
        }

        var attackerChain = GetChain(attackerName);
        if (attackerChain.StrongAgainst.Equals(defenderName, StringComparison.OrdinalIgnoreCase))
        {
            return GuardianStarOutcome.Advantage;
        }

        if (attackerChain.WeakTo.Equals(defenderName, StringComparison.OrdinalIgnoreCase))
        {
            return GuardianStarOutcome.Disadvantage;
        }

        return GuardianStarOutcome.Neutral;
    }

    public static int ModifierFor(string? attacker, string? defender) => Resolve(attacker, defender) switch
    {
        GuardianStarOutcome.Advantage => AdvantageModifier,
        GuardianStarOutcome.Disadvantage => -AdvantageModifier,
        _ => 0
    };

    public static GuardianStarChain GetChain(string guardianStar)
    {
        if (!TryNormalize(guardianStar, out var normalized))
        {
            throw new ArgumentException($"Unknown guardian star: {guardianStar}", nameof(guardianStar));
        }

        foreach (var cycle in Cycles)
        {
            var selectedIndex = Array.FindIndex(
                cycle,
                item => item.Equals(normalized, StringComparison.OrdinalIgnoreCase));
            if (selectedIndex < 0)
            {
                continue;
            }

            var weakTo = cycle[(selectedIndex - 1 + cycle.Length) % cycle.Length];
            var selected = cycle[selectedIndex];
            var strongAgainst = cycle[(selectedIndex + 1) % cycle.Length];
            return new GuardianStarChain(
                weakTo,
                selected,
                strongAgainst,
                Symbols[weakTo],
                Symbols[selected],
                Symbols[strongAgainst]);
        }

        throw new InvalidOperationException($"Guardian star {normalized} was normalized but is absent from every cycle.");
    }

    public static bool TryGetSymbol(string? guardianStar, out string symbol)
    {
        if (TryNormalize(guardianStar, out var normalized))
        {
            symbol = Symbols[normalized];
            return true;
        }

        symbol = "?";
        return false;
    }

    private static bool TryNormalize(string? guardianStar, out string normalized)
    {
        if (!string.IsNullOrWhiteSpace(guardianStar))
        {
            foreach (var name in Symbols.Keys)
            {
                if (name.Equals(guardianStar.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    normalized = name;
                    return true;
                }
            }
        }

        normalized = string.Empty;
        return false;
    }
}
