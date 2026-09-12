using System.Globalization;
using YfmCompanion.Engine;

namespace YfmCompanion.Desktop;

internal sealed record CompactLiveRouteRow(string Result, int Attack, string Route);

internal static class CompactLivePresentation
{
    public static CompactLiveRouteRow CreateRow(TacticalRecommendation recommendation) =>
        new(
            recommendation.FinalCard.Name,
            recommendation.EffectiveAttack,
            FormatRoute(recommendation));

    public static string FormatRoute(TacticalRecommendation recommendation)
    {
        var selections = new List<string>(recommendation.ConsumedHandSlots.Count + 1);
        if (recommendation.FieldTarget is FieldCard target)
        {
            var fieldPosition = target.Zone == FieldZone.Monster
                ? target.Slot
                : target.Slot + 5;
            selections.Add($"F({fieldPosition})");
        }

        selections.AddRange(recommendation.ConsumedHandSlots.Select(slot => slot.ToString(CultureInfo.InvariantCulture)));
        return string.Join('+', selections);
    }

}
