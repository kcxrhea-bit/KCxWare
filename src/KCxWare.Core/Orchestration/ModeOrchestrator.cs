using KCxWare.Core.Abstractions;
using KCxWare.Core.Loading;
using KCxWare.Core.Models;
using KCxWare.Core.Policies;

namespace KCxWare.Core.Orchestration;

public sealed class ModeOrchestrator(IStateStore stateStore, ISystemController system, Action<string>? log = null,
    ITransitionProgressReporter? progress = null)
{
    public string CurrentSessionId => system.CurrentSessionId;
    public Task<ModeState> GetStateAsync(CancellationToken cancellationToken = default) =>
        stateStore.LoadAsync(cancellationToken);

    public async Task<ModeState> ApplyLiveAsync(MachineMode target, CancellationToken cancellationToken = default)
    {
        if (target is not (MachineMode.Gaming or MachineMode.Programming or MachineMode.Normal))
        {
            throw new ArgumentOutOfRangeException(nameof(target), "Only final modes can be applied live.");
        }

        ModePolicy.AssertSafe();
        var state = await stateStore.LoadAsync(cancellationToken);
        await system.DeleteOneShotTaskAsync(cancellationToken);
        if (state.CurrentMode == MachineMode.RecoveryRequired)
        {
            return state;
        }

        var transaction = NewTransaction(state.CurrentMode, target);
        var operationId = transaction.Id;
        progress?.Report(new(operationId, 0, $"Preparing {target} Mode…"));
        var previousPowerPlan = state.PreviousPowerPlan ?? await system.GetActivePowerPlanAsync(cancellationToken);
        var baseline = state.ChangedServices.Count == 0 && state.CurrentMode == MachineMode.Normal
            ? await CaptureGamingServicesAsync(cancellationToken) : state.ChangedServices;
        state = state with { Transaction = transaction, ChangedServices = baseline, LastError = null };
        await stateStore.SaveAsync(state, cancellationToken);
        progress?.Report(new(operationId, 20, "Baseline secured"));

        try
        {
            if (target == MachineMode.Gaming)
            {
                await ApplyGamingAsync(operationId, cancellationToken);
            }
            else
            {
                await RestoreCapturedServicesAsync(state, cancellationToken);
                progress?.Report(new(operationId, 40, $"{target} services restored"));
                await ApplyNonGamingPowerPlanAsync(target, state.PreviousPowerPlan, cancellationToken);
                progress?.Report(new(operationId, 60, $"{target} power configuration applied"));
                _ = await system.GetActivePowerPlanAsync(cancellationToken);
                progress?.Report(new(operationId, 80, $"Verifying {target} Mode…", true));
            }

            var completed = state with
            {
                CurrentMode = target,
                DesiredMode = target,
                PreviousPowerPlan = target == MachineMode.Normal ? null : previousPowerPlan,
                ChangedServices = target == MachineMode.Normal ? [] : baseline,
                SessionId = target == MachineMode.Normal ? null : system.CurrentSessionId,
                Transaction = transaction with { Completed = true },
                LastSuccessfulTransitionUtc = DateTimeOffset.UtcNow
            };
            await stateStore.SaveAsync(completed, cancellationToken);
            return completed;
        }
        catch (Exception exception)
        {
            return await FailAsync(state, transaction, exception, cancellationToken);
        }
    }

    public async Task<ModeState> NormalizeAfterBootAsync(CancellationToken cancellationToken = default)
    {
        var state = await stateStore.LoadAsync(cancellationToken);
        if (state.CurrentMode is not (MachineMode.Gaming or MachineMode.Programming) ||
            string.Equals(state.SessionId, system.CurrentSessionId, StringComparison.Ordinal))
        {
            return state;
        }

        return await ApplyLiveAsync(MachineMode.Normal, cancellationToken);
    }

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
                await ApplyGamingAsync(transaction.Id, cancellationToken);
            }
            else
            {
                await RestoreCapturedServicesAsync(state, cancellationToken);
                await ApplyNonGamingPowerPlanAsync(target, state.PreviousPowerPlan, cancellationToken);
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
            if (!running.HasValue)
            {
                continue;
            }

            ServiceFailureActionsConfig? failureActions = null;
            if (ModePolicy.RecoverySuppressedServices.Contains(service))
            {
                // Captured durably as part of the transactional baseline before anything is
                // changed, so a crash mid-transition still leaves the exact original SCM
                // recovery configuration recoverable via safe recovery / boot normalization.
                failureActions = await system.GetServiceFailureActionsAsync(service, cancellationToken);
            }

            snapshots.Add(new ServiceSnapshot(service, running.Value, failureActions));
        }

        return snapshots;
    }

    private async Task ApplyGamingAsync(string operationId, CancellationToken cancellationToken)
    {
        await system.DelayAsync(ModePolicy.GamingSettleDelay, cancellationToken);

        // Suppress SCM-level auto-restart for services whose recovery actions would otherwise
        // fight the cleanup-verification loop below (e.g. WSearch's default 5x RESTART actions),
        // BEFORE stopping them. This prevents the restart at the SCM level instead of racing it.
        foreach (var service in ModePolicy.RecoverySuppressedServices)
        {
            await system.SetServiceFailureActionsAsync(service, ServiceFailureActionsConfig.NoRecovery, cancellationToken);
        }

        var safetySkippedServices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<string> survivingServices = [];
        IReadOnlyList<string> survivingProcesses = [];
        IReadOnlyList<string> bestEffortSurvivingProcesses = [];

        for (var attempt = 1; attempt <= ModePolicy.GamingCleanupAttempts; attempt++)
        {
            await system.ShutdownWslAsync(cancellationToken);
            safetySkippedServices.Clear();

            foreach (var service in ModePolicy.GamingSuppressibleServices)
            {
                if (ModePolicy.ProtectedServices.Contains(service))
                {
                    throw new InvalidOperationException($"Protected service policy violation: {service}");
                }

                if (await system.IsServiceRunningAsync(service, cancellationToken) != true)
                {
                    continue;
                }

                if (!await system.CanStopServiceSafelyAsync(service, cancellationToken))
                {
                    safetySkippedServices.Add(service);
                    continue;
                }

                await system.StopServiceAsync(service, cancellationToken);
            }
            progress?.Report(new(operationId, 40, "Gaming services configured"));

            foreach (var process in ModePolicy.GamingSuppressibleProcesses)
            {
                await system.StopProcessAsync(process, cancellationToken: cancellationToken);
            }

            foreach (var process in ModePolicy.GamingBestEffortProcesses)
            {
                await system.StopProcessAsync(process, cancellationToken: cancellationToken);
            }

            foreach (var process in ModePolicy.GamingSuppressibleBackgroundProcesses)
            {
                await system.StopProcessAsync(process, backgroundOnly: true, cancellationToken);
            }
            progress?.Report(new(operationId, 60, "Gaming processes cleaned"));

            await system.DelayAsync(ModePolicy.GamingVerificationRetryDelay, cancellationToken);
            survivingServices = await FindSurvivingServicesAsync(safetySkippedServices, cancellationToken);
            survivingProcesses = await FindSurvivingProcessesAsync(cancellationToken);
            bestEffortSurvivingProcesses = await FindBestEffortSurvivingProcessesAsync(cancellationToken);
            log?.Invoke($"Gaming cleanup verification {attempt}/{ModePolicy.GamingCleanupAttempts}: " +
                $"surviving services={FormatNames(survivingServices)}; " +
                $"surviving processes={FormatNames(survivingProcesses)}; " +
                $"best-effort surviving processes={FormatNames(bestEffortSurvivingProcesses)}; " +
                $"safety-skipped services={FormatNames(safetySkippedServices)}.");

            if (survivingServices.Count == 0 && survivingProcesses.Count == 0)
            {
                await system.DelayAsync(ModePolicy.GamingCleanVerificationDelay, cancellationToken);
                survivingServices = await FindSurvivingServicesAsync(safetySkippedServices, cancellationToken);
                survivingProcesses = await FindSurvivingProcessesAsync(cancellationToken);
                bestEffortSurvivingProcesses = await FindBestEffortSurvivingProcessesAsync(cancellationToken);
                log?.Invoke($"Gaming cleanup sustained verification {attempt}/{ModePolicy.GamingCleanupAttempts}: " +
                    $"surviving services={FormatNames(survivingServices)}; " +
                    $"surviving processes={FormatNames(survivingProcesses)}; " +
                    $"best-effort surviving processes={FormatNames(bestEffortSurvivingProcesses)}.");

                if (survivingServices.Count == 0 && survivingProcesses.Count == 0)
                {
                    break;
                }
            }
        }

        if (survivingServices.Count != 0 || survivingProcesses.Count != 0)
        {
            throw new InvalidOperationException("Gaming cleanup verification failed. " +
                $"Surviving services: {FormatNames(survivingServices)}. " +
                $"Surviving processes: {FormatNames(survivingProcesses)}.");
        }

        if (await system.PowerPlanExistsAsync(ModePolicy.GamingPowerPlan, cancellationToken))
        {
            await system.SetPowerPlanAsync(ModePolicy.GamingPowerPlan, cancellationToken);
        }
        progress?.Report(new(operationId, 80, "Verifying Gaming Mode…", true));
    }

    private async Task<IReadOnlyList<string>> FindSurvivingServicesAsync(
        IReadOnlySet<string> safetySkippedServices, CancellationToken cancellationToken)
    {
        var surviving = new List<string>();
        foreach (var service in ModePolicy.GamingSuppressibleServices)
        {
            if (!safetySkippedServices.Contains(service) &&
                await system.IsServiceRunningAsync(service, cancellationToken) == true)
            {
                surviving.Add(service);
            }
        }

        return surviving;
    }

    private async Task<IReadOnlyList<string>> FindSurvivingProcessesAsync(CancellationToken cancellationToken)
    {
        var surviving = new List<string>();
        foreach (var process in ModePolicy.GamingSuppressibleProcesses)
        {
            if (await system.IsProcessRunningAsync(process, cancellationToken: cancellationToken))
            {
                surviving.Add(process);
            }
        }

        foreach (var process in ModePolicy.GamingSuppressibleBackgroundProcesses)
        {
            if (await system.IsProcessRunningAsync(process, backgroundOnly: true, cancellationToken))
            {
                surviving.Add($"{process} (background)");
            }
        }

        foreach (var process in ModePolicy.GamingShutdownVerifiedProcesses)
        {
            if (await system.IsProcessRunningAsync(process, cancellationToken: cancellationToken))
            {
                surviving.Add(process);
            }
        }

        return surviving;
    }

    private async Task<IReadOnlyList<string>> FindBestEffortSurvivingProcessesAsync(
        CancellationToken cancellationToken)
    {
        var surviving = new List<string>();
        foreach (var process in ModePolicy.GamingBestEffortProcesses)
        {
            if (await system.IsProcessRunningAsync(process, cancellationToken: cancellationToken))
            {
                surviving.Add(process);
            }
        }

        return surviving;
    }

    private static string FormatNames(IEnumerable<string> names)
    {
        var values = names.Order(StringComparer.OrdinalIgnoreCase).ToArray();
        return values.Length == 0 ? "none" : string.Join(", ", values);
    }

    private async Task RestoreCapturedServicesAsync(ModeState state, CancellationToken cancellationToken)
    {
        foreach (var snapshot in state.ChangedServices)
        {
            if (snapshot.FailureActions is not null)
            {
                // Restore the exact captured SCM recovery configuration - not a default, not a
                // hardcoded guess - before touching the service's running state. This is the same
                // path used by Normal restoration, Programming, safe recovery, interrupted-transition
                // recovery, and boot normalization, since they all funnel through this method.
                await system.SetServiceFailureActionsAsync(snapshot.Name, snapshot.FailureActions, cancellationToken);
            }

            if (snapshot.WasRunning && await system.IsServiceRunningAsync(snapshot.Name, cancellationToken) == false)
            {
                await system.StartServiceAsync(snapshot.Name, cancellationToken);
            }
        }
    }

    private async Task ApplyNonGamingPowerPlanAsync(MachineMode target, string? previousPowerPlan,
        CancellationToken cancellationToken)
    {
        IEnumerable<string> candidates = target == MachineMode.Programming
            ? new[] { ModePolicy.GamingPowerPlan, ModePolicy.AmdBalancedPowerPlan, ModePolicy.WindowsBalancedPowerPlan }
            : new[] { previousPowerPlan, ModePolicy.AmdBalancedPowerPlan, ModePolicy.WindowsBalancedPowerPlan }
                .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
                .Cast<string>();

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
