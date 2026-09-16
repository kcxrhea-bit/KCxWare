namespace KCxWare.Core.Models;

public sealed record ServiceSnapshot(string Name, bool WasRunning);

public sealed record TransitionRecord(
    string Id,
    MachineMode From,
    MachineMode To,
    DateTimeOffset StartedAtUtc,
    bool Completed,
    string? Error = null);

public sealed record ModeState
{
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public MachineMode CurrentMode { get; init; } = MachineMode.Normal;
    public MachineMode DesiredMode { get; init; } = MachineMode.Normal;
    public string? PreviousPowerPlan { get; init; }
    public IReadOnlyList<ServiceSnapshot> ChangedServices { get; init; } = [];
    public DateTimeOffset? LastSuccessfulTransitionUtc { get; init; }
    public TransitionRecord? Transaction { get; init; }
    public string? LastError { get; init; }
    public string? SessionId { get; init; }

    public bool RebootRequired => CurrentMode is MachineMode.GamingArmed
        or MachineMode.ProgrammingArmed or MachineMode.NormalArmed;
}
