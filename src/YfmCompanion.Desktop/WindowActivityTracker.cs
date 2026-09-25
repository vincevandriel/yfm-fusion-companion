namespace YfmCompanion.Desktop;

/// <summary>Keep the dispatcher alive until cancelled background operations finish their cleanup/checkpoints.</summary>
internal sealed class WindowActivityTracker
{
    private readonly object _gate = new();
    private int _active;
    private TaskCompletionSource _idle = CompletedSource();
    public IDisposable Begin()
    {
        lock (_gate)
        {
            if (_active++ == 0) _idle = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        return new Lease(this);
    }
    public Task WhenIdle() { lock (_gate) return _idle.Task; }
    private void End()
    {
        lock (_gate) { if (--_active == 0) _idle.TrySetResult(); }
    }
    private static TaskCompletionSource CompletedSource()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        completion.SetResult();
        return completion;
    }
    private sealed class Lease(WindowActivityTracker owner) : IDisposable
    {
        private WindowActivityTracker? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.End();
    }
}
