using System.Collections.ObjectModel;
using System.ComponentModel;
using YfmCompanion.Data;
using YfmCompanion.Engine;

namespace YfmCompanion.Desktop;

internal sealed record DeckCopyTile(Card Card, int Copies)
{
    public string Name => Card.Name;
    public string TileCaption => $"{Copies}× • #{Card.Id:000}";
    public string DetailLine => Card.PrimaryType + (Card.Level is null ? string.Empty : $" • {Card.Attack} / {Card.Defense}");
}

/// <summary>Grouped deck presentation; the slot editor retains individual card order.</summary>
internal sealed class DeckTrayViewModel : INotifyPropertyChanged
{
    public ObservableCollection<DeckCopyTile> Cards { get; } = [];
    public string Summary { get; private set; } = "0 / 40 cards • add cards below or load your saved deck";
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Update(IEnumerable<Card> cards)
    {
        var grouped = cards.GroupBy(card => card.Id).Select(group => new DeckCopyTile(group.First(), group.Count())).OrderBy(row => row.Card.Id).ToArray();
        for (var index = 0; index < grouped.Length; index++)
        {
            if (index >= Cards.Count) Cards.Add(grouped[index]);
            else if (Cards[index] != grouped[index]) Cards[index] = grouped[index];
        }
        while (Cards.Count > grouped.Length) Cards.RemoveAt(Cards.Count - 1);
        var count = grouped.Sum(row => row.Copies);
        var illegal = grouped.Any(row => row.Copies > OwnedDeckOptimizer.LegalCopyLimitForCard(row.Card.Id));
        Summary = $"{count} / 40 cards • {grouped.Length} distinct cards" +
            (illegal ? " • exceeds game copy limits; analysis is a custom what-if" : count == 40 ? " • complete deck" : " • empty slots are ignored");
        PropertyChanged?.Invoke(this, new(nameof(Summary)));
    }
}
