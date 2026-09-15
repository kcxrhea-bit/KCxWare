using KCxWare.Core.Abstractions;
using KCxWare.Core.Models;
using KCxWare.Core.Policies;

namespace KCxWare.Core.Orchestration;

public sealed class ModeOrchestrator(IStateStore stateStore, ISystemController system)
{
    public Task<ModeState> GetStateAsync(CancellationToken cancellationToken = default) =>
        stateStore.LoadAsync(cancellationToken);

    public async Task<ModeState> ArmAsync(MachineMode target, string helperPath,
        CancellationToken cancellationToken = default)
    {
        ModePolicy.AssertSafe();
        var armedMode = target switch
        {
            MachineMode.Gaming => MachineMode.GamingArmed,
            MachineMode.Programming => MachineMode.ProgrammingArmed,
            MachineMode.Normal => MachineMode.NormalArmed,
            _ => throw new ArgumentOutOfRangeException(nameof(target), "Only final modes can be armed.")
        };

        var state = await stateStore.LoadAsync(cancellationToken);
        if (state.CurrentMode == MachineMode.RecoveryRequired)
        {
            throw new InvalidOperationException("Run safe recovery before arming another mode.");
        }

        if (state.CurrentMode == armedMode && await system.IsOneShotTaskPresentAsync(cancellationToken))
        {
            return state;
        }

        var transaction = NewTransaction(state.CurrentMode, armedMode);
        state = state with { Transaction = transaction, LastError = null };
        await stateStore.SaveAsync(state, cancellationToken);

        try
        {
            var previousPowerPlan = state.PreviousPowerPlan ?? await system.GetActivePowerPlanAsync(cancellationToken);
            var snapshots = target == MachineMode.Gaming
                ? await CaptureGamingServicesAsync(cancellationToken)
                : state.ChangedServices;
            await system.ArmOneShotTaskAsync(helperPath, cancellationToken);

            state = state with
            {
                CurrentMode = armedMode,
                DesiredMode = target,
                PreviousPowerPlan = previousPowerPlan,
                ChangedServices = snapshots,
                Transaction = transaction with { Completed = true },
                LastSuccessfulTransitionUtc = DateTimeOffset.UtcNow
            };
            await stateStore.SaveAsync(state, cancellationToken);
            return state;
        }
        catch (Exception exception)
        {
            return await FailAsync(state, transaction, exception, cancellationToken);
        }
    }

    public async Task<ModeState> ApplyArmedAsync(CancellationToken cancellationToken = default)
    {
        ModePolicy.AssertSafe();
        var state = await stateStore.LoadAsync(cancellationToken);
        if (!state.RebootRequired)
        {
            await system.DeleteOneShotTaskAsync(cancellationToken);
            return state;
        }

        var target = state.DesiredMode;
        var transaction = NewTransaction(state.CurrentMode, target);
        state = state with { Transaction = transaction, LastError = null };
        await stateStore.SaveAsync(state, cancellationToken);

        try
        {
            if (target == MachineMode.Gaming)
            {
                await ApplyGamingAsync(cancellationToken);
            }
            else
            {
                await RestoreCapturedServicesAsync(state, cancellationToken);
                await ApplyNonGamingPowerPlanAsync(target, cancellationToken);
            }

            await system.DeleteOneShotTaskAsync(cancellationToken);
            state = state with
            {
                CurrentMode = target,
                DesiredMode = target,
                ChangedServices = target == MachineMode.Gaming ? state.ChangedServices : [],
                PreviousPowerPlan = target == MachineMode.Gaming ? state.PreviousPowerPlan : null,
                Transaction = transaction with { Completed = true },
                LastSuccessfulTransitionUtc = DateTimeOffset.UtcNow
            };
            await stateStore.SaveAsync(state, cancellationToken);
            return state;
        }
        catch (Exception exception)
        {
            return await FailAsync(state, transaction, exception, cancellationToken);
        }
    }

    public async Task<ModeState> RecoverAsync(CancellationToken cancellationToken = default)
    {
        var state = await stateStore.LoadAsync(cancellationToken);
        try
        {
            await RestoreCapturedServicesAsync(state, cancellationToken);
            if (state.PreviousPowerPlan is { } previous && await system.PowerPlanExistsAsync(previous, cancellationToken))
            {
                await system.SetPowerPlanAsync(previous, cancellationToken);
            }

            await system.DeleteOneShotTaskAsync(cancellationToken);
            var recovered = state with
            {
                CurrentMode = MachineMode.Normal,
                DesiredMode = MachineMode.Normal,
                ChangedServices = [],
                PreviousPowerPlan = null,
                Transaction = state.Transaction is null ? null : state.Transaction with { Completed = true },
                LastError = null,
                LastSuccessfulTransitionUtc = DateTimeOffset.UtcNow
            };
            await stateStore.SaveAsync(recovered, cancellationToken);
            return recovered;
        }
        catch (Exception exception)
        {
            var failed = state with { CurrentMode = MachineMode.RecoveryRequired, LastError = exception.Message };
            await stateStore.SaveAsync(failed, cancellationToken);
            return failed;
        }
    }

    public async Task<ModeState> CancelArmedAsync(CancellationToken cancellationToken = default)
    {
        var state = await stateStore.LoadAsync(cancellationToken);
        await system.DeleteOneShotTaskAsync(cancellationToken);
        var restoredMode = state.RebootRequired && state.Transaction?.From is
            MachineMode.Normal or MachineMode.Gaming or MachineMode.Programming
            ? state.Transaction.From
            : state.CurrentMode;
        var cancelled = state with
        {
            CurrentMode = restoredMode,
            DesiredMode = restoredMode,
            ChangedServices = restoredMode == MachineMode.Gaming ? state.ChangedServices : [],
            PreviousPowerPlan = restoredMode == MachineMode.Gaming ? state.PreviousPowerPlan : null,
            Transaction = null,
            LastError = null
        };
        await stateStore.SaveAsync(cancelled, cancellationToken);
        return cancelled;
    }

    private async Task<IReadOnlyList<ServiceSnapshot>> CaptureGamingServicesAsync(CancellationToken cancellationToken)
    {
        var snapshots = new List<ServiceSnapshot>();
        foreach (var service in ModePolicy.GamingSuppressibleServices)
        {
            var running = await system.IsServiceRunningAsync(service, cancellationToken);
            if (running.HasValue)
            {
                snapshots.Add(new ServiceSnapshot(service, running.Value));
            }
        }

        return snapshots;
    }

    private async Task ApplyGamingAsync(CancellationToken cancellationToken)
    {
        foreach (var service in ModePolicy.GamingSuppressibleServices)
        {
            if (ModePolicy.ProtectedServices.Contains(service))
            {
                throw new InvalidOperationException($"Protected service policy violation: {service}");
            }

            if (await system.IsServiceRunningAsync(service, cancellationToken) == true)
            {
                await system.StopServiceAsync(service, cancellationToken);
            }
        }

        foreach (var process in ModePolicy.GamingSuppressibleProcesses)
        {
            await system.StopProcessAsync(process, cancellationToken);
        }

        if (await system.PowerPlanExistsAsync(ModePolicy.GamingPowerPlan, cancellationToken))
        {
            await system.SetPowerPlanAsync(ModePolicy.GamingPowerPlan, cancellationToken);
        }
    }

    private async Task RestoreCapturedServicesAsync(ModeState state, CancellationToken cancellationToken)
    {
        foreach (var snapshot in state.ChangedServices.Where(snapshot => snapshot.WasRunning))
        {
            if (await system.IsServiceRunningAsync(snapshot.Name, cancellationToken) == false)
            {
                await system.StartServiceAsync(snapshot.Name, cancellationToken);
            }
        }
    }

    private async Task ApplyNonGamingPowerPlanAsync(MachineMode target, CancellationToken cancellationToken)
    {
        var candidates = target == MachineMode.Programming
            ? new[] { ModePolicy.GamingPowerPlan, ModePolicy.AmdBalancedPowerPlan, ModePolicy.WindowsBalancedPowerPlan }
            : new[] { ModePolicy.AmdBalancedPowerPlan, ModePolicy.WindowsBalancedPowerPlan };

        foreach (var candidate in candidates)
        {
            if (await system.PowerPlanExistsAsync(candidate, cancellationToken))
            {
                await system.SetPowerPlanAsync(candidate, cancellationToken);
                return;
            }
        }

        throw new InvalidOperationException("No supported power plan is installed.");
    }

    private async Task<ModeState> FailAsync(ModeState state, TransitionRecord transaction, Exception exception,
        CancellationToken cancellationToken)
    {
        var failed = state with
        {
            CurrentMode = MachineMode.RecoveryRequired,
            Transaction = transaction with { Error = exception.Message },
            LastError = exception.Message
        };
        await stateStore.SaveAsync(failed, cancellationToken);
        return failed;
    }

    private static TransitionRecord NewTransaction(MachineMode from, MachineMode to) =>
        new(Guid.NewGuid().ToString("N"), from, to, DateTimeOffset.UtcNow, false);
}
