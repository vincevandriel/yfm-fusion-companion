using System.Net;

namespace YfmCompanion.RetroArch;

public enum RetroArchPlaybackState
{
    Unknown,
    Contentless,
    Playing,
    Paused
}

public sealed record RetroArchStatus(
    RetroArchPlaybackState State,
    string? SystemId,
    string? GameBasename,
    uint? Crc32,
    string RawResponse)
{
    public bool HasContent => State is RetroArchPlaybackState.Playing or RetroArchPlaybackState.Paused;
}

public enum RetroArchConnectionKind
{
    Ready,
    RetroArchNotRunning,
    NetworkCommandsDisabled,
    NetworkCommandsUnreachable,
    NoContent,
    WrongContent,
    MemoryUnavailable,
    InvalidLiveData
}

public sealed record RetroArchConnectionState(
    RetroArchConnectionKind Kind,
    string Message,
    RetroArchStatus? Status = null,
    string? Detail = null)
{
    public bool IsReady => Kind == RetroArchConnectionKind.Ready;
}

public sealed record RetroArchConfiguration(
    string Path,
    bool Exists,
    bool NetworkCommandsEnabled,
    int NetworkCommandPort,
    string? SaveDirectory);

public sealed record LiveFieldCard(int Slot, int CardId, int Attack, int Defense, int PowerModifier = 0);

public sealed record ForbiddenMemoriesLiveSnapshot(
    RetroArchStatus Status,
    bool DuelActive,
    IReadOnlyList<int> HandCardIds,
    IReadOnlyList<LiveFieldCard> PlayerField,
    IReadOnlyList<LiveFieldCard> PlayerSpellTrapField,
    IReadOnlyList<LiveFieldCard> OpponentField,
    IReadOnlyList<LiveFieldCard> OpponentSpellTrapField,
    int TerrainId,
    IReadOnlyList<int> ShuffledDeckCardIds,
    IReadOnlyList<int> ConstructedDeckCardIds,
    IReadOnlyList<int> ChestQuantities,
    bool SaveDataAvailable,
    int PlayerLifePoints,
    int OpponentLifePoints,
    int GameState,
    DateTimeOffset CapturedAt,
    string MemoryCommand);

public class RetroArchProtocolException(string message) : Exception(message);

public sealed class RetroArchNoContentException(string message) : RetroArchProtocolException(message);

public sealed class RetroArchWrongContentException(string message) : RetroArchProtocolException(message);

public sealed class RetroArchTransientStateException(string message) : RetroArchProtocolException(message);
