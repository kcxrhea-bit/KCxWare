namespace KCxWare.Core.Models;

/// <summary>Mirrors the Win32 SC_ACTION_TYPE values used by SCM failure/recovery actions.</summary>
public enum ServiceFailureActionType
{
    None = 0,
    RestartService = 1,
    RebootComputer = 2,
    RunCommand = 3
}

/// <summary>A single SCM failure-recovery action: what to do, after how long.</summary>
public sealed record ServiceFailureAction(ServiceFailureActionType Type, uint DelayMs);

/// <summary>
/// A Windows service's SCM-level failure/recovery-action configuration - what
/// <c>sc.exe qfailure</c>/<c>sc.exe failure</c> read and write, captured/restored here via the typed
/// advapi32 QueryServiceConfig2/ChangeServiceConfig2 APIs so KCxWare can temporarily suppress a
/// service's auto-restart behavior (e.g. WSearch during Gaming Mode cleanup) and restore the exact
/// previously configured behavior afterward, rather than a hardcoded guess.
/// </summary>
public sealed record ServiceFailureActionsConfig(
    uint ResetPeriodSeconds,
    string? RebootMessage,
    string? Command,
    IReadOnlyList<ServiceFailureAction> Actions)
{
    /// <summary>
    /// Represents "no failure actions configured" - used to temporarily suppress SCM auto-restart
    /// of a service KCxWare intentionally stops. Never persisted as a captured baseline value; only
    /// ever written transiently and later replaced by the real captured configuration on restore.
    /// </summary>
    public static ServiceFailureActionsConfig NoRecovery { get; } = new(0, null, null, []);
}
