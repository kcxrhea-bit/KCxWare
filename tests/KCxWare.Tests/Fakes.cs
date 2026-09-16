using KCxWare.Core.Abstractions;
using KCxWare.Core.Models;
using KCxWare.Core.Loading;
using KCxWare.Core.Policies;
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

internal sealed class RecordingProgress : ITransitionProgressReporter
{
    public List<TransitionProgress> Events { get; } = [];
    public void Report(TransitionProgress progress) => Events.Add(progress);
}

internal sealed class SequencedRunner(params CommandResult[] results) : ICommandRunner
{
    private readonly Queue<CommandResult> _results = new(results);
    public List<(string FileName, IReadOnlyList<string> Arguments)> Calls { get; } = [];

    public Task<CommandResult> RunAsync(string fileName, IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default)
    {
        Calls.Add((fileName, arguments));
        return Task.FromResult(_results.Dequeue());
    }
}

internal sealed class FakeSystem : ISystemController
{
    public string CurrentSessionId { get; set; } = "test-session";
    public Dictionary<string, bool> Services { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> ServiceRestartsRemaining { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, bool> ServiceCanStopSafely { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, bool> Processes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> ProcessRestartsRemaining { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> ServiceCleanWindowRespawnsRemaining { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> ProcessCleanWindowRespawnsRemaining { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Plans { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> StoppedServices { get; } = [];
    public List<string> StartedServices { get; } = [];
    public List<string> StoppedProcesses { get; } = [];
    public List<string> Operations { get; } = [];
    public List<TimeSpan> Delays { get; } = [];
    public string? ActivePlan { get; set; } = "existing-plan";
    public bool TaskPresent { get; set; }
    public int TaskArmCount { get; private set; }
    public int TaskDeleteCount { get; private set; }
    public int WslShutdownCount { get; private set; }

    public Task<string?> GetActivePowerPlanAsync(CancellationToken cancellationToken = default) => Task.FromResult(ActivePlan);
    public Task<bool> PowerPlanExistsAsync(string planId, CancellationToken cancellationToken = default) => Task.FromResult(Plans.Contains(planId));
    public Task SetPowerPlanAsync(string planId, CancellationToken cancellationToken = default) { ActivePlan = planId; return Task.CompletedTask; }
    public Task<bool?> IsServiceRunningAsync(string name, CancellationToken cancellationToken = default) =>
        Task.FromResult(Services.TryGetValue(name, out var running) ? (bool?)running : null);
    public Task<bool> CanStopServiceSafelyAsync(string name, CancellationToken cancellationToken = default) =>
        Task.FromResult(!ServiceCanStopSafely.TryGetValue(name, out var safe) || safe);
    public Task StopServiceAsync(string name, CancellationToken cancellationToken = default)
    {
        Services[name] = false;
        StoppedServices.Add(name);
        Operations.Add($"stop-service:{name}");
        if (ServiceRestartsRemaining.TryGetValue(name, out var restarts) && restarts > 0)
        {
            ServiceRestartsRemaining[name] = restarts - 1;
            Services[name] = true;
        }
        return Task.CompletedTask;
    }
    public Task StartServiceAsync(string name, CancellationToken cancellationToken = default) { Services[name] = true; StartedServices.Add(name); return Task.CompletedTask; }
    public Task<bool> IsProcessRunningAsync(string name, bool backgroundOnly = false,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Processes.TryGetValue(name, out var running) && running);
    public Task StopProcessAsync(string name, bool backgroundOnly = false, CancellationToken cancellationToken = default)
    {
        Processes[name] = false;
        StoppedProcesses.Add(name);
        Operations.Add($"stop-process:{name}");
        if (ProcessRestartsRemaining.TryGetValue(name, out var restarts) && restarts > 0)
        {
            ProcessRestartsRemaining[name] = restarts - 1;
            Processes[name] = true;
        }
        return Task.CompletedTask;
    }
    public Task ShutdownWslAsync(CancellationToken cancellationToken = default)
    {
        WslShutdownCount++;
        Operations.Add("shutdown-wsl");
        if (Processes.TryGetValue("vmmemWSL", out var running) && running)
        {
            Processes["vmmemWSL"] = false;
            if (ProcessRestartsRemaining.TryGetValue("vmmemWSL", out var restarts) && restarts > 0)
            {
                ProcessRestartsRemaining["vmmemWSL"] = restarts - 1;
                Processes["vmmemWSL"] = true;
            }
        }
        return Task.CompletedTask;
    }
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default)
    {
        Delays.Add(delay);
        if (delay == ModePolicy.GamingCleanVerificationDelay)
        {
            Respawn(ServiceCleanWindowRespawnsRemaining, Services);
            Respawn(ProcessCleanWindowRespawnsRemaining, Processes);
        }

        return Task.CompletedTask;
    }
    public Task ArmOneShotTaskAsync(string helperPath, CancellationToken cancellationToken = default) { TaskPresent = true; TaskArmCount++; return Task.CompletedTask; }
    public Task<bool> IsOneShotTaskPresentAsync(CancellationToken cancellationToken = default) => Task.FromResult(TaskPresent);
    public Task DeleteOneShotTaskAsync(CancellationToken cancellationToken = default) { TaskPresent = false; TaskDeleteCount++; return Task.CompletedTask; }

    private static void Respawn(Dictionary<string, int> remainingByName, Dictionary<string, bool> states)
    {
        foreach (var name in remainingByName.Keys.ToArray())
        {
            if (remainingByName[name] <= 0)
            {
                continue;
            }

            remainingByName[name]--;
            states[name] = true;
        }
    }
}

/// <summary>Records every invocation so tests can assert power actions never fire prematurely or on failure.</summary>
internal sealed class FakePowerActionInvoker : IPowerActionInvoker
{
    public List<(PowerAction Action, MachineMode ModeAtInvocationTime)> Invocations { get; } = [];
    public int ExitCodeToReturn { get; set; }
    public Func<MachineMode>? CurrentModeAccessor { get; set; }

    public Task<int> InvokeAsync(PowerAction action, CancellationToken cancellationToken = default)
    {
        Invocations.Add((action, CurrentModeAccessor?.Invoke() ?? MachineMode.RecoveryRequired));
        return Task.FromResult(ExitCodeToReturn);
    }
}

/// <summary>Deterministic <see cref="IRandomProvider"/> that always returns a fixed index for assertions,
/// or can be driven through a sequence to exercise multiple selections.</summary>
internal sealed class SequencedRandomProvider(params int[] values) : IRandomProvider
{
    private int _index;

    public int Next(int minInclusive, int maxExclusive)
    {
        var value = values[_index % values.Length];
        _index++;
        return Math.Clamp(value, minInclusive, maxExclusive - 1);
    }
}
