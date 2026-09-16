using System.ComponentModel;
using System.Windows.Threading;
using KCxWare.Core.Loading;

namespace KCxWare.ViewModels;

/// <summary>
/// WPF-facing projection of <see cref="LoadingCoordinator"/> for the KCx loading overlay.
/// Marshals coordinator change events onto the UI dispatcher and exposes bindable
/// milestone/progress/status properties; it never fabricates progress itself.
/// </summary>
public sealed class LoadingViewModel : INotifyPropertyChanged
{
    private static readonly string[] AllProperties =
    {
        nameof(IsVisible), nameof(Title), nameof(Status), nameof(IsIndeterminate), nameof(Progress),
        nameof(ProgressText), nameof(IsCompleted), nameof(IsFailed), nameof(ErrorMessage),
        nameof(Milestone20), nameof(Milestone40), nameof(Milestone60), nameof(Milestone80), nameof(Milestone100),
        nameof(CompletionHeadline), nameof(HasTextCompletion), nameof(CompletionTitleText),
        nameof(CompletionSubtitleText), nameof(ShowProgressVisuals), nameof(IsPlainCompletion)
    };

    private readonly LoadingCoordinator _coordinator;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _dismissTimer;
    private LoadingOperationState? _current;

    public event PropertyChangedEventHandler? PropertyChanged;

    public LoadingViewModel(LoadingCoordinator coordinator)
    {
        _coordinator = coordinator;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _dismissTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        _dismissTimer.Tick += OnDismissTimerTick;
        _coordinator.Changed += OnCoordinatorChanged;
        Refresh();
    }

    public bool IsVisible => _current is not null;
    public string Title => _current?.Title ?? string.Empty;
    public string Status => _current?.Status ?? string.Empty;
    public bool IsIndeterminate => _current?.IsIndeterminate ?? true;
    public int Progress => _current?.Progress ?? 0;
    public string ProgressText => IsIndeterminate ? string.Empty : $"{Progress}%";
    public bool IsCompleted => _current?.IsCompleted ?? false;
    public bool IsFailed => _current?.IsFailed ?? false;
    public string ErrorMessage => _current?.ErrorMessage ?? string.Empty;

    public bool Milestone20 => IsCompleted || MilestoneCalculator.IsActive(20, Progress, IsIndeterminate);
    public bool Milestone40 => IsCompleted || MilestoneCalculator.IsActive(40, Progress, IsIndeterminate);
    public bool Milestone60 => IsCompleted || MilestoneCalculator.IsActive(60, Progress, IsIndeterminate);
    public bool Milestone80 => IsCompleted || MilestoneCalculator.IsActive(80, Progress, IsIndeterminate);
    public bool Milestone100 => IsCompleted || MilestoneCalculator.IsActive(100, Progress, IsIndeterminate);

    public string CompletionHeadline => _current?.IsGamingRelated == true ? "KCX GAMING MODE ACTIVE" : "KCX · COMPLETE";

    /// <summary>True when this operation completed with a text-only completion screen (mode/power final-state copy).</summary>
    public bool HasTextCompletion => IsCompleted && !string.IsNullOrEmpty(_current?.CompletionTitle);
    public string CompletionTitleText => _current?.CompletionTitle ?? string.Empty;
    public string CompletionSubtitleText => _current?.CompletionSubtitle ?? string.Empty;

    /// <summary>False once a text-only completion is showing - the parade video/progress/milestones fade out.</summary>
    public bool ShowProgressVisuals => !HasTextCompletion;

    /// <summary>True for the original generic completion presentation (no exact completion copy set).</summary>
    public bool IsPlainCompletion => IsCompleted && !HasTextCompletion;

    public RelayCommand DismissCommand => new(() =>
    {
        if (_current is { IsFailed: true } failed) _coordinator.Dismiss(failed.Id);
    });

    private void OnCoordinatorChanged()
    {
        if (_dispatcher.CheckAccess()) Refresh();
        else _dispatcher.BeginInvoke(Refresh);
    }

    private void Refresh()
    {
        _current = _coordinator.Current;
        foreach (var name in AllProperties) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        if (_current is { IsCompleted: true })
        {
            // Text-only completion/final-state screens (mode + power) need a bit longer on
            // screen than the generic completion presentation so the copy is actually readable.
            _dismissTimer.Interval = HasTextCompletion ? TimeSpan.FromMilliseconds(1800) : TimeSpan.FromMilliseconds(900);
            _dismissTimer.Stop();
            _dismissTimer.Start();
        }
        else
        {
            _dismissTimer.Stop();
        }
    }

    private void OnDismissTimerTick(object? sender, EventArgs e)
    {
        _dismissTimer.Stop();
        if (_current is { IsCompleted: true } completed) _coordinator.Dismiss(completed.Id);
    }
}
