namespace YfmCompanion.Engine;

// Unlike Progress<T>, this does not enqueue a second asynchronous stream out of stage order.
internal sealed class InlineProgress<T>(Action<T> callback) : IProgress<T>
{
    public void Report(T value) => callback(value);
}
