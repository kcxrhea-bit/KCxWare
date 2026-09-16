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
        if (state.CurrentMode == MachineMode.RecoveryRequired)
        {
            await system.DeleteOneShotTaskAsync(cancellationToken);
            return state;
        }

        var capabilities = await DetectCapabilitiesSafeAsync(cancellationToken);
        var previousPowerPlan = state.PreviousPowerPlan ?? await system.GetActivePowerPlanAsync(cancellationToken);
        if (state.CurrentMode == MachineMode.Normal && string.IsNullOrWhiteSpace(previousPowerPlan))
        {
            throw new InvalidOperationException(
                "KCxWare could not capture the active power plan, so a safely reversible transition cannot begin.");
        }

        var installedPowerPlans = await DetectInstalledPowerPlansAsync(previousPowerPlan, cancellationToken);
        var baseline = state.ChangedServices.Count == 0 && state.CurrentMode == MachineMode.Normal
            ? await CaptureGamingServicesAsync(cancellationToken) : state.ChangedServices;
        log?.Invoke($"Capability preflight: graphics={FormatNames(capabilities.GraphicsVendors)}; " +
            $"development={FormatNames(capabilities.DevelopmentTools)}; " +
            $"installed supported power plans={FormatNames(installedPowerPlans)}.");

        // Everything above is read-only. Remove a stale legacy task only after preflight succeeds.
        await system.DeleteOneShotTaskAsync(cancellationToken);
        var transaction = NewTransaction(state.CurrentMode, target);
        var operationId = transaction.Id;
        progress?.Report(new(operationId, 0, $"Preparing {target} Mode…"));
        state = state with { Transaction = transaction, ChangedServices = baseline, LastError = null };
        await stateStore.SaveAsync(state, cancellationToken);
        progress?.Report(new(operationId, 20, "Baseline secured"));

        try
        {
            if (target == MachineMode.Gaming)
            {
                await ApplyGamingAsync(operationId, capabilities, installedPowerPlans, cancellationToken);
            }
            else
            {
                await RestoreCapturedServicesAsync(state, cancellationToken);
                progress?.Report(new(operationId, 40, $"{target} services restored"));
                await ApplyNonGamingPowerPlanAsync(target, state.PreviousPowerPlan, installedPowerPlans,
                    cancellationToken);
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
                var capabilities = await DetectCapabilitiesSafeAsync(cancellationToken);
                var installedPowerPlans = await DetectInstalledPowerPlansAsync(state.PreviousPowerPlan,
                    cancellationToken);
                await ApplyGamingAsync(transaction.Id, capabilities, installedPowerPlans, cancellationToken);
            }
            else
            {
                await RestoreCapturedServicesAsync(state, cancellationToken);
                var installedPowerPlans = await DetectInstalledPowerPlansAsync(state.PreviousPowerPlan,
                    cancellationToken);
                await ApplyNonGamingPowerPlanAsync(target, state.PreviousPowerPlan, installedPowerPlans,
                    cancellationToken);
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
                log?.Invoke($"Captured recovery policy for {service}: action count={failureActions.Actions.Count}, " +
                    $"types={string.Join(",", failureActions.Actions.Select(action => action.Type))}, " +
                    $"delaysMs={string.Join(",", failureActions.Actions.Select(action => action.DelayMs))}.");
            }

            snapshots.Add(new ServiceSnapshot(service, running.Value, failureActions));
        }

        return snapshots;
    }

    private async Task ApplyGamingAsync(string operationId, MachineCapabilities capabilities,
        IReadOnlySet<string> installedPowerPlans, CancellationToken cancellationToken)
    {
        // Suppress SCM-level auto-restart for services whose recovery actions would otherwise
        // fight the cleanup-verification loop below (e.g. WSearch's default 5x RESTART actions),
        // BEFORE stopping them. This prevents the restart at the SCM level instead of racing it.
        foreach (var service in ModePolicy.RecoverySuppressedServices)
        {
            log?.Invoke($"Requesting recovery-policy suppression for {service}.");
            await system.SetServiceFailureActionsAsync(service, ServiceFailureActionsConfig.NoRecovery, cancellationToken);

            // Fail closed: SetServiceFailureActionsAsync succeeding does not by itself prove the SCM
            // is honoring the suppressed policy going forward (the store's own readback only proves
            // the change was accepted at the moment it was made). Re-read it here, immediately before
            // any StopServiceAsync call, so a suppression that silently reverted - or was never
            // effective for this specific service/flag combination - aborts BEFORE cleanup starts
            // rather than surfacing 80+ seconds later as an opaque verification-loop failure.
            var effective = await system.GetServiceFailureActionsAsync(service, cancellationToken);
            log?.Invoke($"Post-suppression readback for {service}: action count={effective.Actions.Count}, " +
                $"types={string.Join(",", effective.Actions.Select(action => action.Type))}.");
            if (!IsRecoveryPolicySuppressed(effective))
            {
                throw new InvalidOperationException("WSearch recovery-policy suppression could not be verified.");
            }
        }

        var safetySkippedServices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<string> survivingServices = [];
        IReadOnlyList<string> survivingProcesses = [];
        IReadOnlyList<string> bestEffortSurvivingProcesses = [];

        for (var attempt = 1; attempt <= ModePolicy.GamingCleanupAttempts; attempt++)
        {
            if (capabilities.DevelopmentTools.Contains("WSL", StringComparer.OrdinalIgnoreCase))
            {
                await system.ShutdownWslAsync(cancellationToken);
            }
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

                if (ModePolicy.RecoverySuppressedServices.Contains(service))
                {
                    log?.Invoke($"Requesting stop for recovery-suppressed service {service} (attempt {attempt}).");
                }

                await system.StopServiceAsync(service, cancellationToken);
            }
            progress?.Report(new(operationId, 40, "Gaming services configured"));

            foreach (var process in ModePolicy.GamingSuppressibleProcesses)
            {
                if (await system.IsProcessRunningAsync(process, cancellationToken: cancellationToken))
                {
                    await system.StopProcessAsync(process, cancellationToken: cancellationToken);
                }
            }

            foreach (var process in ModePolicy.GamingBestEffortProcesses)
            {
                if (await system.IsProcessRunningAsync(process, cancellationToken: cancellationToken))
                {
                    await system.StopProcessAsync(process, cancellationToken: cancellationToken);
                }
            }

            foreach (var process in ModePolicy.GamingSuppressibleBackgroundProcesses)
            {
                if (await system.IsProcessRunningAsync(process, backgroundOnly: true, cancellationToken))
                {
                    await system.StopProcessAsync(process, backgroundOnly: true, cancellationToken);
                }
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

        var gamingPlan = ModePolicy.GamingPowerPlanCandidates.FirstOrDefault(installedPowerPlans.Contains);
        if (gamingPlan is not null)
        {
            await system.SetPowerPlanAsync(gamingPlan, cancellationToken);
            log?.Invoke($"Selected installed Gaming power plan {gamingPlan}.");
        }
        else
        {
            log?.Invoke("No supported Gaming power-plan candidate is installed; preserving the active plan.");
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

    private async Task<MachineCapabilities> DetectCapabilitiesSafeAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await system.DetectCapabilitiesAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            log?.Invoke($"Capability preflight was unavailable and optional capabilities will be treated as absent: " +
                $"{exception.GetType().Name}: {exception.Message}");
            return MachineCapabilities.Empty;
        }
    }

    private async Task<IReadOnlySet<string>> DetectInstalledPowerPlansAsync(string? previousPowerPlan,
        CancellationToken cancellationToken)
    {
        var candidates = ModePolicy.GamingPowerPlanCandidates
            .Concat(ModePolicy.ProgrammingPowerPlanCandidates)
            .Concat([ModePolicy.AmdBalancedPowerPlan, ModePolicy.WindowsBalancedPowerPlan])
            .Concat(string.IsNullOrWhiteSpace(previousPowerPlan) ? [] : [previousPowerPlan])
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            if (await system.PowerPlanExistsAsync(candidate, cancellationToken))
            {
                installed.Add(candidate);
            }
        }

        return installed;
    }

    private static string FormatNames(IEnumerable<string> names)
    {
        var values = names.Order(StringComparer.OrdinalIgnoreCase).ToArray();
        return values.Length == 0 ? "none" : string.Join(", ", values);
    }

    private static bool IsRecoveryPolicySuppressed(ServiceFailureActionsConfig config) =>
        !config.ActionsOnNonCrashFailures &&
        (config.Actions.Count == 0 ||
         config.Actions is [{ Type: ServiceFailureActionType.None, DelayMs: 0 }]);

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
                log?.Invoke($"Requesting recovery-policy restoration for {snapshot.Name}: " +
                    $"action count={snapshot.FailureActions.Actions.Count}.");
                await system.SetServiceFailureActionsAsync(snapshot.Name, snapshot.FailureActions, cancellationToken);
                var restored = await system.GetServiceFailureActionsAsync(snapshot.Name, cancellationToken);
                log?.Invoke($"Post-restore verification for {snapshot.Name}: action count={restored.Actions.Count}, " +
                    $"types={string.Join(",", restored.Actions.Select(action => action.Type))}, " +
                    $"delaysMs={string.Join(",", restored.Actions.Select(action => action.DelayMs))}.");
            }

            if (snapshot.WasRunning && await system.IsServiceRunningAsync(snapshot.Name, cancellationToken) == false)
            {
                await system.StartServiceAsync(snapshot.Name, cancellationToken);
            }
        }
    }

    private async Task ApplyNonGamingPowerPlanAsync(MachineMode target, string? previousPowerPlan,
        IReadOnlySet<string> installedPowerPlans, CancellationToken cancellationToken)
    {
        IEnumerable<string> candidates = target == MachineMode.Programming
            ? ModePolicy.ProgrammingPowerPlanCandidates
            : new[] { previousPowerPlan, ModePolicy.AmdBalancedPowerPlan, ModePolicy.WindowsBalancedPowerPlan }
                .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
                .Cast<string>();

        foreach (var candidate in candidates)
        {
            if (installedPowerPlans.Contains(candidate))
            {
                await system.SetPowerPlanAsync(candidate, cancellationToken);
                log?.Invoke($"Selected installed {target} power plan {candidate}.");
                return;
            }
        }

        log?.Invoke($"No supported {target} power-plan candidate is installed; preserving the active plan.");
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
