using KCxWare.Core.Abstractions;
using KCxWare.Core.Models;
using KCxWare.Core.Windows;

namespace KCxWare.Tests;

internal sealed class MemoryStateStore(ModeState? initial = null) : IStateStore
{
    public ModeState State { get; private set; } = initial ?? new ModeState();
    public bool FailNextSave { get; set; }

    public Task<ModeState> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(State);

    public Task SaveAsync(ModeState state, CancellationToken cancellationToken = default)
    {
        if (FailNextSave)
        {
            FailNextSave = false;
            throw new IOException("Injected state failure.");
        }

        State = state;
        return Task.CompletedTask;
    }
}

internal sealed class RecordingRunner : ICommandRunner
{
    public int Calls { get; private set; }

    public Task<CommandResult> RunAsync(string fileName, IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default)
    {
        Calls++;
        return Task.FromResult(new CommandResult(0, string.Empty, string.Empty));
    }
}

internal sealed class FakeSystem : ISystemController
{
    public Dictionary<string, bool> Services { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Plans { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> StoppedServices { get; } = [];
    public List<string> StartedServices { get; } = [];
    public List<string> StoppedProcesses { get; } = [];
    public string? ActivePlan { get; set; } = "existing-plan";
    public bool TaskPresent { get; set; }
    public int TaskArmCount { get; private set; }
    public int TaskDeleteCount { get; private set; }

    public Task<string?> GetActivePowerPlanAsync(CancellationToken cancellationToken = default) => Task.FromResult(ActivePlan);
    public Task<bool> PowerPlanExistsAsync(string planId, CancellationToken cancellationToken = default) => Task.FromResult(Plans.Contains(planId));
    public Task SetPowerPlanAsync(string planId, CancellationToken cancellationToken = default) { ActivePlan = planId; return Task.CompletedTask; }
    public Task<bool?> IsServiceRunningAsync(string name, CancellationToken cancellationToken = default) =>
        Task.FromResult(Services.TryGetValue(name, out var running) ? (bool?)running : null);
    public Task StopServiceAsync(string name, CancellationToken cancellationToken = default) { Services[name] = false; StoppedServices.Add(name); return Task.CompletedTask; }
    public Task StartServiceAsync(string name, CancellationToken cancellationToken = default) { Services[name] = true; StartedServices.Add(name); return Task.CompletedTask; }
    public Task StopProcessAsync(string name, CancellationToken cancellationToken = default) { StoppedProcesses.Add(name); return Task.CompletedTask; }
    public Task ArmOneShotTaskAsync(string helperPath, CancellationToken cancellationToken = default) { TaskPresent = true; TaskArmCount++; return Task.CompletedTask; }
    public Task<bool> IsOneShotTaskPresentAsync(CancellationToken cancellationToken = default) => Task.FromResult(TaskPresent);
    public Task DeleteOneShotTaskAsync(CancellationToken cancellationToken = default) { TaskPresent = false; TaskDeleteCount++; return Task.CompletedTask; }
}
