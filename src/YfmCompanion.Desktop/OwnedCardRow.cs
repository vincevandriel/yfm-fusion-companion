using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows.Media;
using YfmCompanion.Data;
using YfmCompanion.Engine;

namespace YfmCompanion.Desktop;

internal sealed class OwnedCardRow : INotifyPropertyChanged
{
    private readonly Action<OwnedCardRow> _changed;
    private int _quantity;
    private int _proposedCopies;
    private string? _artworkPath;
    private ThumbnailCache? _artworkCache;
    private int _chestCopies;
    private int _deckCopies;
    private bool _isManual;

    public OwnedCardRow(Card card, Action<OwnedCardRow> changed)
    {
        Card = card;
        _changed = changed;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public Card Card { get; }
    public int ChestCopies { get => _chestCopies; set { _chestCopies = value; Raise(nameof(QuantityLine)); } }
    public int DeckCopies { get => _deckCopies; set { _deckCopies = value; Raise(nameof(QuantityLine)); } }
    public string DetailLine => $"{Card.PrimaryType} • ATK {Card.Attack:N0} / DEF {Card.Defense:N0} • {FormatStars(Card)}";
    public bool IsManual { get => _isManual; set { _isManual = value; Raise(nameof(QuantityLine)); } }
    public string QuantityLine => IsManual ? $"Owned {Quantity} • manual quantity" : $"Owned {Quantity} • chest {ChestCopies} / deck {DeckCopies}";
    public string ProposedLine => ProposedCopies > 0 ? $"Proposed deck: {ProposedCopies}×" : string.Empty;
    public ImageSource? Artwork => _artworkCache?.Load(_artworkPath);

    public int Quantity
    {
        get => _quantity;
        set
        {
            var clamped = Math.Clamp(value, 0, 295);
            if (_quantity == clamped) return;
            _quantity = clamped;
            Raise(nameof(Quantity));
            Raise(nameof(QuantityLine));
            _changed(this);
        }
    }

    public int ProposedCopies
    {
        get => _proposedCopies;
        set
        {
            if (_proposedCopies == value) return;
            _proposedCopies = value;
            Raise(nameof(ProposedCopies));
            Raise(nameof(ProposedLine));
        }
    }

    public void RefreshArtwork(string? folder, ThumbnailCache cache, string? overridePath = null)
    {
        _artworkPath = File.Exists(overridePath) ? overridePath : FindArtwork(folder, Card.Id);
        _artworkCache = cache;
        if (_artworkPath is not null) cache.Invalidate(_artworkPath);
        Raise(nameof(Artwork));
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    private static string FormatStars(Card card) => string.Join(" / ", new[] { card.GuardianStar1, card.GuardianStar2 }
        .Where(star => !string.IsNullOrWhiteSpace(star)).Select(star => GuardianStarRules.TryGetSymbol(star, out var symbol) ? symbol : "?"))
        is { Length: > 0 } stars ? stars : "no guardian stars";
    private static string? FindArtwork(string? folder, int id)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return null;
        foreach (var name in new[] { id.ToString(CultureInfo.InvariantCulture), id.ToString("D3", CultureInfo.InvariantCulture) })
            foreach (var extension in new[] { ".png", ".jpg", ".jpeg" })
            {
                var path = Path.Combine(folder, name + extension);
                if (File.Exists(path)) return path;
            }
        return null;
    }
}
