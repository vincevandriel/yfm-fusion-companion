using YfmCompanion.Data;

namespace YfmCompanion.Engine;

public sealed record GuideSupportStructure(int Points, int UsefulFieldCopies, int CompatibleEquipCopies, string TargetName)
{
    public bool MeetsSupportTarget => UsefulFieldCopies >= 1 && CompatibleEquipCopies >= 2;
    public static GuideSupportStructure Evaluate(FusionCatalog catalog, IReadOnlyList<int> deck, DeckAnalysisReport analysis)
    {
        var fields = deck.Where(ForbiddenMemoriesStrategyEvaluator.IsFieldCard).ToArray();
        var equips = deck.Where(id => catalog.GetCard(id).PrimaryType.Equals("Equip", StringComparison.OrdinalIgnoreCase)).ToArray();
        var targets = analysis.FusionResults.Where(r => !r.IsEquipped && r.Probability >= .05).Select(r => r.Result)
            .Concat(deck.Distinct().Select(catalog.GetCard)).Where(c => c.Attack >= 2000).DistinctBy(c => c.Id);
        var best = new GuideSupportStructure(0, 0, 0, "No established strong target");
        var bestAttack = 0;
        foreach (var target in targets)
        {
            var compatible = equips.Count(id => catalog.CanEquip(id, target.Id));
            var usefulFields = fields.Count(id => ForbiddenMemoriesStrategyEvaluator.GetFieldModifier(id, target.PrimaryType) > 0);
            // Soft structure target: one synergistic field, two compatible copies,
            // a smaller third-equip bonus. Further copies must earn their draw value.
            var points = (usefulFields > 0 ? 3 : 0) + Math.Min(2, compatible) * 3 + (compatible >= 3 ? 1 : 0);
            if (points > best.Points || points == best.Points && target.Attack > bestAttack)
            { best = new(points, usefulFields, compatible, target.Name); bestAttack = target.Attack; }
        }
        return best;
    }
}
