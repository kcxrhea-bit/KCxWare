using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using KCxWare.Core.Models;
using KCxWare.Core.Orchestration;
using KCxWare.Core.Persistence;
using KCxWare.Core.Windows;

namespace KCxWare.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly ModeOrchestrator _orchestrator =
        new(new JsonStateStore(), new WindowsSystemController(new CommandRunner()));
    private ModeState _state = new();
    private string _activePowerPlan = "Detecting…";
    private string _health = "CHECKING";

    public event PropertyChangedEventHandler? PropertyChanged;

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

    public async Task RefreshAsync()
    {
        try
        {
            _state = await _orchestrator.GetStateAsync();
            ActivePowerPlan = await new WindowsSystemController(new CommandRunner()).GetActivePowerPlanAsync() ?? "Unavailable";
            Health = _state.CurrentMode == MachineMode.RecoveryRequired ? "RECOVERY REQUIRED" : "HEALTHY";
        }
        catch (Exception exception)
        {
            Health = "STATE UNAVAILABLE";
            _state = _state with { LastError = exception.Message };
        }

        NotifyStateProperties();
    }

    public void StartElevated(string command)
    {
        var helperPath = Path.Combine(AppContext.BaseDirectory, "KCxWare.Helper.exe");
        if (!File.Exists(helperPath))
        {
            throw new FileNotFoundException("KCxWare.Helper.exe is missing. Repair or reinstall KCxWare.", helperPath);
        }

        Process.Start(new ProcessStartInfo(helperPath, command)
        {
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = AppContext.BaseDirectory
        });
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
}
