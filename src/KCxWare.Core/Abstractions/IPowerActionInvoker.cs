namespace KCxWare.Core.Abstractions;

/// <summary>The narrowly scoped set of Windows power actions KCxWare is allowed to request.</summary>
public enum PowerAction
{
    Restart,
    Shutdown
}

/// <summary>
/// Boundary between <see cref="Orchestration.PowerOrchestrator"/> and whatever actually asks
/// Windows to restart or shut down (in production, the elevated KCxWare.Helper via the same
/// UAC/process-elevation path already used for mode transitions). Kept as a minimal seam purely
/// so tests can verify ordering/failure behavior without ever invoking a real power action.
/// </summary>
public interface IPowerActionInvoker
{
    Task<int> InvokeAsync(PowerAction action, CancellationToken cancellationToken = default);
}
