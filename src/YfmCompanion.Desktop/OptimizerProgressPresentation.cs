using YfmCompanion.Engine;

namespace YfmCompanion.Desktop;

internal sealed record OptimizerProgressPresentation(string Stage, string Detail, bool IsIndeterminate, double Value);

internal static class OptimizerProgressPresenter
{
    public static OptimizerProgressPresentation Present(DeckBuildProgress value)
    {
        var stage = value.State switch
        {
            DeckBuildState.Preparing => "PREPARE ●  SEARCH  VERIFY  READY",
            DeckBuildState.Searching => "PREPARE ✓  SEARCH ●  VERIFY  READY",
            DeckBuildState.Verifying => "PREPARE ✓  SEARCH ✓  VERIFY ●  READY",
            _ => value.Stage
        };
        var hasTotal = value.TotalHands is > 0;
        var indeterminate = !hasTotal && value.State is DeckBuildState.Preparing or DeckBuildState.Verifying;
        var fraction = hasTotal
            ? Math.Clamp((double)value.CompletedHands!.Value / value.TotalHands!.Value, 0, 1)
            : value.SearchBudget is { } budget
                ? Math.Clamp(value.SearchTimeConsumed.TotalSeconds / budget.TotalSeconds, 0, .99)
                : 0;
        var hands = hasTotal ? $" • {value.CompletedHands:N0} / {value.TotalHands:N0} hands" : string.Empty;
        var eta = value.EstimatedRemaining is null ? string.Empty : $" • ETA {value.EstimatedRemainingLow:g}–{value.EstimatedRemainingHigh:g}";
        var proof = value.ProofTotalSpace is null ? string.Empty : $" • {value.ProofResolvedSpace:N0}/{value.ProofTotalSpace:N0} space • {value.ProofLegalDecksEvaluated:N0} legal decks";
        var detail = $"{value.Stage} • elapsed {value.Elapsed:g} • {value.CandidatesExamined:N0} candidates{hands}{eta}{proof}";
        return new(stage, detail, indeterminate, fraction);
    }
}
