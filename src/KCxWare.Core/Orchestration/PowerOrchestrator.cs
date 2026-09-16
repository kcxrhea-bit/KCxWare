using KCxWare.Core.Abstractions;
using KCxWare.Core.Models;

namespace KCxWare.Core.Orchestration;

public enum PowerResultStatus
{
    Success,
    RestorationFailed,
    VerificationFailed,
    PowerActionFailed
}

/// <summary>Outcome of one <see cref="PowerOrchestrator.ExecuteAsync"/> call.</summary>
public sealed record PowerActionResult(PowerResultStatus Status, string? ErrorMessage, ModeState FinalState)
{
    public bool Succeeded => Status == PowerResultStatus.Success;
}

/// <summary>
/// Coordinates the safe-power-ordering contract for RESTART WINDOWS / SHUT DOWN WINDOWS:
/// USER REQUEST -&gt; CONFIRMATION (handled by the caller before this is invoked) -&gt; NORMAL
/// RESTORATION IF NECESSARY -&gt; AUTHORITATIVE REFRESH/VERIFICATION -&gt; POWER FINAL-STATE MESSAGE
/// (the caller-supplied <c>beforePowerRequest</c> callback) -&gt; WINDOWS POWER REQUEST.
///
/// Restoration reuses <see cref="ModeOrchestrator.RecoverAsync"/> - the same authoritative,
/// already-accepted Normal-restoration path used by "Run Safe Recovery" - rather than
/// introducing a second way to mutate mode state. If the machine is already Normal, no
/// restoration/capture happens at all, so a temporary baseline is never unnecessarily recaptured.
///
/// The Windows power action is only ever requested after restoration succeeds and the
/// authoritative state has been re-read and verified as Normal; on any failure the power
/// invoker is never called.
/// </summary>
public sealed class PowerOrchestrator(
    ModeOrchestrator modeOrchestrator,
    IStateStore stateStore,
    IPowerActionInvoker powerInvoker)
{
    public async Task<PowerActionResult> ExecuteAsync(PowerAction action, Func<Task>? beforePowerRequest = null,
        CancellationToken cancellationToken = default)
    {
        var state = await stateStore.LoadAsync(cancellationToken);

        if (state.CurrentMode != MachineMode.Normal)
        {
            state = await modeOrchestrator.RecoverAsync(cancellationToken);
            if (state.CurrentMode != MachineMode.Normal)
            {
                return new PowerActionResult(PowerResultStatus.RestorationFailed,
                    state.LastError ?? "Normal restoration failed before the power action could proceed.", state);
            }
        }

        // Authoritative refresh/verification: re-read the persisted state rather than trusting
        // the in-memory value returned above, so a concurrent/failed write cannot be missed.
        var verified = await stateStore.LoadAsync(cancellationToken);
        if (verified.CurrentMode != MachineMode.Normal)
        {
            return new PowerActionResult(PowerResultStatus.VerificationFailed,
                "Authoritative state verification failed after Normal restoration.", verified);
        }

        if (beforePowerRequest is not null)
        {
            await beforePowerRequest();
        }

        var exitCode = await powerInvoker.InvokeAsync(action, cancellationToken);
        if (exitCode != 0)
        {
            return new PowerActionResult(PowerResultStatus.PowerActionFailed,
                $"KCxWare.Helper power action exited with code {exitCode}. See helper.log for details.", verified);
        }

        return new PowerActionResult(PowerResultStatus.Success, null, verified);
    }
}
