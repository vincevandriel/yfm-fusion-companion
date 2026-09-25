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
        var hasTotal = value.TotalHands is > 0 && value.CompletedHands is not null;
        var indeterminate = !hasTotal && (value.State is DeckBuildState.Preparing or DeckBuildState.Verifying ||
            value.State == DeckBuildState.Searching && value.SearchBudget is null);
        // A sampled candidate finishing is not the timed search finishing.
        var timedSearch = value.State == DeckBuildState.Searching && value.SearchBudget is not null;
        var fraction = timedSearch
            ? Math.Clamp(value.SearchTimeConsumed.TotalSeconds / value.SearchBudget!.Value.TotalSeconds, 0, .99)
            : hasTotal
            ? Math.Clamp((double)value.CompletedHands!.Value / value.TotalHands!.Value, 0, .99)
            : value.SearchBudget is { } budget
                ? Math.Clamp(value.SearchTimeConsumed.TotalSeconds / budget.TotalSeconds, 0, .99)
                : 0;
        var hands = hasTotal ? $" • {value.CompletedHands:N0} / {value.TotalHands:N0} hands" : string.Empty;
        var eta = value.EstimatedRemaining is null ? string.Empty : $" • ETA {value.EstimatedRemainingLow:g}–{value.EstimatedRemainingHigh:g}";
        var proof = value.ProofTotalSpace is null ? string.Empty : $" • {value.ProofResolvedSpace:N0}/{value.ProofTotalSpace:N0} space • {value.ProofLegalDecksEvaluated:N0} legal decks";
        var search = value.State == DeckBuildState.Searching && value.SearchBudget is { } limit
            ? $" • search budget {value.SearchTimeConsumed.TotalSeconds:N1} / {limit.TotalSeconds:N0} s consumed" : string.Empty;
        var elapsed = value.Elapsed.ToString(@"hh\:mm\:ss");
        var detail = $"{value.Stage} • elapsed {elapsed} • {value.CandidatesExamined:N0} candidates{search}{hands}{eta}{proof}";
        return new(stage, detail, indeterminate, fraction);
    }
}
