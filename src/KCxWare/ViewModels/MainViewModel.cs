using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using KCxWare.Core.Abstractions;
using KCxWare.Core.Loading;
using KCxWare.Core.Models;
using KCxWare.Core.Orchestration;
using KCxWare.Core.Persistence;
using KCxWare.Core.Windows;

namespace KCxWare.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly JsonStateStore _stateStore = new();
    private readonly ModeOrchestrator _orchestrator;
    private ModeState _state = new();
    private string _activePowerPlan = "Detecting…";
    private string _health = "CHECKING";
    private bool _isBusy;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Central loading coordinator shared by every async operation this window triggers.</summary>
    public LoadingCoordinator LoadingCoordinator { get; } = new();

    /// <summary>
    /// Coordinates the RESTART WINDOWS / SHUT DOWN WINDOWS safe-power-ordering contract. Shares
    /// this view model's state store and mode orchestrator so restoration is the exact same
    /// authoritative path used by "Run Safe Recovery" - not a second way to mutate mode state.
    /// </summary>
    public PowerOrchestrator PowerOrchestrator { get; }

    public string CurrentMode => FormatMode(_state.CurrentMode);
    public string ActivePowerPlan { get => _activePowerPlan; private set => SetField(ref _activePowerPlan, value); }
    public string Health { get => _health; private set => SetField(ref _health, value); }
    public string ArmedStatus => _state.RebootRequired ? $"ARMED · {_state.DesiredMode}" : "NOT ARMED";
    public string RebootStatus => _state.RebootRequired ? "REQUIRED" : "NOT REQUIRED";
    public string LastTransition => _state.LastSuccessfulTransitionUtc?.ToLocalTime().ToString("g") ?? "No completed transition";
    public string ErrorDetail => _state.LastError ?? "No recovery issue detected.";
    public bool ShowGamingExit => _state.CurrentMode is MachineMode.Gaming or MachineMode.GamingArmed;
    public bool ShowRecovery => _state.CurrentMode == MachineMode.RecoveryRequired;
    public bool ShowCancel => _state.RebootRequired;
    public bool NeedsBootNormalization => _state.CurrentMode is MachineMode.Gaming or MachineMode.Programming &&
        !string.Equals(_state.SessionId, _orchestrator.CurrentSessionId, StringComparison.Ordinal);

    public async Task NormalizeAfterBootAsync() => await RunElevatedAsync("normalize-after-boot");

    /// <summary>True while any tracked operation is active. Buttons bind to this to prevent double-submission.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (_isBusy == value) return;
            _isBusy = value;
            OnPropertyChanged();
        }
    }

    public MainViewModel()
    {
        _orchestrator = new ModeOrchestrator(_stateStore,
            new WindowsSystemController(new CommandRunner(), new ServiceRecoveryPolicyStore()));
        PowerOrchestrator = new PowerOrchestrator(_orchestrator, _stateStore, new HelperPowerActionInvoker(this));
        LoadingCoordinator.Changed += () => IsBusy = LoadingCoordinator.Current is not null;
    }

    public async Task RefreshAsync()
    {
        using var op = LoadingCoordinator.Begin("Loading configuration…", "Reading KCxWare state…", indeterminate: false);
        try
        {
            _state = await _orchestrator.GetStateAsync();
            op.Update("Reading current power plan…", progress: 60);
            ActivePowerPlan = await new WindowsSystemController(new CommandRunner()).GetActivePowerPlanAsync() ?? "Unavailable";
            Health = _state.CurrentMode == MachineMode.RecoveryRequired ? "RECOVERY REQUIRED" : "HEALTHY";
            op.Complete("Configuration loaded.");
        }
        catch (Exception exception)
        {
            Health = "STATE UNAVAILABLE";
            _state = _state with { LastError = exception.Message };
            op.Fail(exception.Message);
        }

        NotifyStateProperties();
    }

    public async Task<int> RunElevatedAsync(string command)
    {
        using var process = StartElevatedProcess(command);

        await process.WaitForExitAsync();
        return process.ExitCode;
    }

    public void StartElevated(string command) => StartElevatedProcess(command).Dispose();

    private static Process StartElevatedProcess(string command)
    {
        var helperPath = HelperLocator.ResolveHelperPath(AppContext.BaseDirectory);
        if (!File.Exists(helperPath))
        {
            throw new FileNotFoundException("KCxWare.Helper.exe is missing. Repair or reinstall KCxWare.", helperPath);
        }

        return Process.Start(new ProcessStartInfo(helperPath, command)
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = AppContext.BaseDirectory
        }) ?? throw new InvalidOperationException("KCxWare.Helper.exe could not be started.");
    }

    private void NotifyStateProperties()
    {
        OnPropertyChanged(nameof(CurrentMode));
        OnPropertyChanged(nameof(ArmedStatus));
        OnPropertyChanged(nameof(RebootStatus));
        OnPropertyChanged(nameof(LastTransition));
        OnPropertyChanged(nameof(ErrorDetail));
        OnPropertyChanged(nameof(ShowGamingExit));
        OnPropertyChanged(nameof(ShowRecovery));
        OnPropertyChanged(nameof(ShowCancel));
    }

    private static string FormatMode(MachineMode mode) => mode switch
    {
        MachineMode.Gaming => "GAMING MODE",
        MachineMode.Programming => "PROGRAMMING MODE",
        MachineMode.Normal => "NORMAL MODE",
        MachineMode.GamingArmed => "GAMING MODE · ARMED",
        MachineMode.ProgrammingArmed => "PROGRAMMING MODE · ARMED",
        MachineMode.NormalArmed => "NORMAL MODE · ARMED",
        _ => "RECOVERY REQUIRED"
    };

    private void SetField(ref string field, string value, [CallerMemberName] string? propertyName = null)
    {
        if (field == value) return;
        field = value;
        OnPropertyChanged(propertyName);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>
    /// Bridges <see cref="PowerOrchestrator"/> to the exact same elevated KCxWare.Helper/UAC
    /// path already used for arm/apply-armed/recover/cancel - no separate elevation mechanism,
    /// just the two additional validated helper commands "restart" and "shutdown".
    /// </summary>
    private sealed class HelperPowerActionInvoker(MainViewModel owner) : IPowerActionInvoker
    {
        public Task<int> InvokeAsync(PowerAction action, CancellationToken cancellationToken = default) =>
            owner.RunElevatedAsync(action == PowerAction.Restart ? "restart" : "shutdown");
    }
}
