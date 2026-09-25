using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace YfmCompanion.Desktop;

/// <summary>Shared inline and docked job presentation; never owns or restarts a search.</summary>
internal sealed class OptimizerActivityViewModel : INotifyPropertyChanged
{
    private string _stage = "PREPARE → SEARCH → VERIFY → READY", _detail = "Ready.";
    private double _value;
    private bool _indeterminate, _running, _paused;
    public event PropertyChangedEventHandler? PropertyChanged;
    public string Stage { get => _stage; set => Set(ref _stage, value); }
    public string Detail { get => _detail; set => Set(ref _detail, value); }
    public double Value { get => _value; set => Set(ref _value, value); }
    public bool IsIndeterminate { get => _indeterminate; set => Set(ref _indeterminate, value); }
    public bool IsActive => _running || _paused;
    public bool CanPause => _running;
    public bool CanResume => !_running && _paused;
    public bool CanEdit => !IsActive;
    public bool IsCollectionReadOnly => IsActive;
    public void SetState(bool running, bool paused)
    {
        _running = running;
        _paused = paused;
        foreach (var name in new[] { nameof(IsActive), nameof(CanPause), nameof(CanResume), nameof(CanEdit), nameof(IsCollectionReadOnly) })
            PropertyChanged?.Invoke(this, new(name));
    }
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new(name));
    }
}
