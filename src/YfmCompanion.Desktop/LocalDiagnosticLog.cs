using System.Text;

namespace YfmCompanion.Desktop;

internal sealed record DiagnosticEntry(DateTimeOffset Timestamp, string Category, string Status, string Detail);

internal sealed class LocalDiagnosticLog
{
    private const int MaximumEntries = 500;
    private readonly Queue<DiagnosticEntry> _entries = new();

    public void Add(string category, string status, string detail)
    {
        _entries.Enqueue(new DiagnosticEntry(DateTimeOffset.Now, category, status, Sanitize(detail)));
        while (_entries.Count > MaximumEntries)
        {
            _entries.Dequeue();
        }
    }

    public string ExportText()
    {
        var output = new StringBuilder();
        output.AppendLine("YFM Fusion Companion local diagnostics");
        output.AppendLine("Contains connection and application status only; no card, hand, deck, collection, or emulator-memory values.");
        output.AppendLine();
        foreach (var entry in _entries)
        {
            output.Append(entry.Timestamp.ToString("O"));
            output.Append(" | ");
            output.Append(entry.Category);
            output.Append(" | ");
            output.Append(entry.Status);
            output.Append(" | ");
            output.AppendLine(entry.Detail);
        }

        return output.ToString();
    }

    private static string Sanitize(string detail) =>
        detail.Replace('\r', ' ').Replace('\n', ' ').Trim();
}
