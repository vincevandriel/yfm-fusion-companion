using System.IO;

namespace YfmCompanion.Desktop;

internal static class ProofCheckpointFiles
{
    // An explicit player action, never an automatic response to a compatibility error.
    // Acquire the same lease as the engine so another job's active checkpoint cannot move.
    public static string? PreserveAndRestart(string checkpointPath)
    {
        if (!File.Exists(checkpointPath)) return null;
        using var lease = new FileStream(checkpointPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var backup = checkpointPath + $".previous-{DateTime.UtcNow:yyyyMMddTHHmmss}-{Guid.NewGuid():N}";
        File.Move(checkpointPath, backup);
        return backup;
    }
}
