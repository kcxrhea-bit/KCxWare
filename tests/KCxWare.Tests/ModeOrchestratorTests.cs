using KCxWare.Core.Models;
using KCxWare.Core.Orchestration;
using KCxWare.Core.Policies;
using KCxWare.Core.Windows;

namespace KCxWare.Tests;

public sealed class ModeOrchestratorTests
{
    [Fact]
    public async Task NormalToGamingArmed_CapturesStateAndArmsTask()
    {
        var store = new MemoryStateStore();
        var system = SystemWithPlans();
        system.Services["WSearch"] = true;
        var result = await new ModeOrchestrator(store, system).ArmAsync(MachineMode.Gaming, TestHelperPath());
        Assert.Equal(MachineMode.GamingArmed, result.CurrentMode);
        Assert.True(result.RebootRequired);
        Assert.Contains(result.ChangedServices, service => service.Name == "WSearch" && service.WasRunning);
        Assert.True(system.TaskPresent);
    }

    [Fact]
    public async Task GamingArmedToGaming_StopsSupportedWorkloadsAndCleansTask()
    {
        var initial = Armed(MachineMode.GamingArmed, MachineMode.Gaming,
            [new ServiceSnapshot("WSearch", true)]);
        var store = new MemoryStateStore(initial);
        var system = SystemWithPlans();
        system.TaskPresent = true;
        system.Services["WSearch"] = true;
        system.Processes["Kudu"] = true;
        var result = await new ModeOrchestrator(store, system).ApplyArmedAsync();
        Assert.Equal(MachineMode.Gaming, result.CurrentMode);
        Assert.Contains("WSearch", system.StoppedServices);
        Assert.Contains("Kudu", system.StoppedProcesses);
        Assert.Equal(1, system.WslShutdownCount);
        Assert.Equal([ModePolicy.GamingSettleDelay, ModePolicy.GamingVerificationRetryDelay,
            ModePolicy.GamingCleanVerificationDelay], system.Delays);
        Assert.Equal(ModePolicy.GamingPowerPlan, system.ActivePlan);
        Assert.False(system.TaskPresent);
        Assert.Equal(MachineMode.Gaming, result.DesiredMode);
        Assert.True(result.Transaction?.Completed);
        Assert.Null(result.Transaction?.Error);
        Assert.Null(result.LastError);
    }

    [Fact]
    public async Task GamingCleanup_LeavesChromeAndNvidiaRecordingProtected()
    {
        var store = new MemoryStateStore(Armed(MachineMode.GamingArmed, MachineMode.Gaming, []));
        var system = SystemWithPlans();
        system.Processes["chrome"] = true;
        system.Processes["chrome-native-host"] = true;
        system.Processes["NVIDIA Overlay"] = true;
        system.Processes["NVIDIA Share"] = true;
        system.Processes["PresentMonService"] = true;
        system.Processes["KCxBrowsers.NativeHost"] = true;

        var result = await new ModeOrchestrator(store, system).ApplyArmedAsync();

        Assert.Equal(MachineMode.Gaming, result.CurrentMode);
        Assert.True(result.Transaction?.Completed);
        Assert.DoesNotContain("chrome", system.StoppedProcesses);
        Assert.DoesNotContain("chrome-native-host", system.StoppedProcesses);
        Assert.DoesNotContain("NVIDIA Overlay", system.StoppedProcesses);
        Assert.DoesNotContain("NVIDIA Share", system.StoppedProcesses);
        Assert.DoesNotContain("PresentMonService", system.StoppedProcesses);
        Assert.DoesNotContain("KCxBrowsers.NativeHost", system.StoppedProcesses);
        Assert.All(new[] { "chrome", "chrome-native-host", "NVIDIA Overlay", "NVIDIA Share",
            "PresentMonService", "KCxBrowsers.NativeHost" },
            process => Assert.True(system.Processes[process]));
    }

    [Theory]
    [InlineData("OpenRGB")]
    [InlineData("rustdesk")]
    public async Task GamingCleanup_RequiredProcessSurvivorFailsWithDiagnostic(string process)
    {
        var store = new MemoryStateStore(Armed(MachineMode.GamingArmed, MachineMode.Gaming, []));
        var system = SystemWithPlans();
        system.TaskPresent = true;
        system.Processes[process] = true;
        system.ProcessRestartsRemaining[process] = ModePolicy.GamingCleanupAttempts;

        var result = await new ModeOrchestrator(store, system).ApplyArmedAsync();

        Assert.Equal(MachineMode.RecoveryRequired, result.CurrentMode);
        Assert.False(result.Transaction?.Completed);
        Assert.Contains(process, result.Transaction?.Error);
        Assert.Contains(process, result.LastError);
        Assert.True(system.TaskPresent);
    }

    [Theory]
    [InlineData("PhoneExperienceHost")]
    [InlineData("CrossDeviceService")]
    [InlineData("CrossDeviceResume")]
    public async Task GamingCleanup_WindowsManagedProcessRespawnIsLoggedBestEffort(string process)
    {
        var store = new MemoryStateStore(Armed(MachineMode.GamingArmed, MachineMode.Gaming, []));
        var system = SystemWithPlans();
        system.Processes[process] = true;
        system.ProcessRestartsRemaining[process] = 1;
        var logs = new List<string>();

        var result = await new ModeOrchestrator(store, system, logs.Add).ApplyArmedAsync();

        Assert.Equal(MachineMode.Gaming, result.CurrentMode);
        Assert.True(result.Transaction?.Completed);
        Assert.Contains(process, system.StoppedProcesses);
        Assert.True(system.Processes[process]);
        Assert.Contains(logs, message => message.Contains("best-effort surviving processes=") &&
            message.Contains(process));
    }

    [Fact]
    public async Task GamingCleanup_AllowsWslServiceHostResidualWhenWorkloadIsStopped()
    {
        var store = new MemoryStateStore(Armed(MachineMode.GamingArmed, MachineMode.Gaming, []));
        var system = SystemWithPlans();
        system.Processes["wslservice"] = true;

        var result = await new ModeOrchestrator(store, system).ApplyArmedAsync();

        Assert.Equal(MachineMode.Gaming, result.CurrentMode);
        Assert.True(result.Transaction?.Completed);
        Assert.True(system.Processes["wslservice"]);
    }

    [Fact]
    public async Task GamingCleanup_ActiveWslWorkloadStillFailsVerification()
    {
        var store = new MemoryStateStore(Armed(MachineMode.GamingArmed, MachineMode.Gaming, []));
        var system = SystemWithPlans();
        system.Processes["vmmemWSL"] = true;
        system.ProcessRestartsRemaining["vmmemWSL"] = ModePolicy.GamingCleanupAttempts;

        var result = await new ModeOrchestrator(store, system).ApplyArmedAsync();

        Assert.Equal(MachineMode.RecoveryRequired, result.CurrentMode);
        Assert.Contains("vmmemWSL", result.LastError);
    }

    [Fact]
    public async Task GamingCleanup_RetriesWorkloadsThatRestartBeforeSuccess()
    {
        var store = new MemoryStateStore(Armed(MachineMode.GamingArmed, MachineMode.Gaming, []));
        var system = SystemWithPlans();
        system.Services["WslService"] = true;
        system.ServiceRestartsRemaining["WslService"] = 1;
        system.Processes["Kudu"] = true;
        system.ProcessRestartsRemaining["Kudu"] = 1;
        system.Processes["vmmemWSL"] = true;
        system.ProcessRestartsRemaining["vmmemWSL"] = 1;
        var logs = new List<string>();

        var result = await new ModeOrchestrator(store, system, logs.Add).ApplyArmedAsync();

        Assert.Equal(MachineMode.Gaming, result.CurrentMode);
        Assert.Equal(2, system.StoppedServices.Count(name => name == "WslService"));
        Assert.Equal(2, system.StoppedProcesses.Count(name => name == "Kudu"));
        Assert.DoesNotContain("vmmemWSL", system.StoppedProcesses);
        Assert.Equal(2, system.WslShutdownCount);
        Assert.Equal([ModePolicy.GamingSettleDelay, ModePolicy.GamingVerificationRetryDelay,
            ModePolicy.GamingVerificationRetryDelay, ModePolicy.GamingCleanVerificationDelay], system.Delays);
        Assert.Contains(logs, message => message.Contains("verification 1/3") && message.Contains("Kudu"));
        Assert.Contains(logs, message => message.Contains("verification 2/3") && message.Contains("surviving processes=none"));
    }

    [Fact]
    public async Task GamingCleanup_DoesNotCompleteWhenSuppressibleWorkloadSurvivesRetries()
    {
        var store = new MemoryStateStore(Armed(MachineMode.GamingArmed, MachineMode.Gaming, []));
        var system = SystemWithPlans();
        system.TaskPresent = true;
        system.Services["SaladBowl"] = true;
        system.ServiceRestartsRemaining["SaladBowl"] = ModePolicy.GamingCleanupAttempts;
        var logs = new List<string>();

        var result = await new ModeOrchestrator(store, system, logs.Add).ApplyArmedAsync();

        Assert.Equal(MachineMode.RecoveryRequired, result.CurrentMode);
        Assert.False(result.Transaction?.Completed);
        Assert.Contains("SaladBowl", result.LastError);
        Assert.True(system.TaskPresent);
        Assert.Equal(ModePolicy.GamingCleanupAttempts, system.WslShutdownCount);
        Assert.Contains(logs, message => message.Contains("verification 3/3") && message.Contains("SaladBowl"));
    }

    [Fact]
    public async Task GamingCleanup_StopsSaladServiceBeforeItsProcesses()
    {
        var store = new MemoryStateStore(Armed(MachineMode.GamingArmed, MachineMode.Gaming, []));
        var system = SystemWithPlans();
        system.Services["SaladBowl"] = true;
        system.Processes["Salad.Bootstrapper"] = true;
        system.Processes["Salad.Bowl.Service"] = true;

        var result = await new ModeOrchestrator(store, system).ApplyArmedAsync();

        Assert.Equal(MachineMode.Gaming, result.CurrentMode);
        var serviceStop = system.Operations.IndexOf("stop-service:SaladBowl");
        var bootstrapperStop = system.Operations.IndexOf("stop-process:Salad.Bootstrapper");
        var bowlStop = system.Operations.IndexOf("stop-process:Salad.Bowl.Service");
        Assert.True(serviceStop >= 0);
        Assert.True(bootstrapperStop > serviceStop);
        Assert.True(bowlStop > serviceStop);
    }

    [Fact]
    public async Task GamingCleanup_AllowsDcomRespawnedWidgetService()
    {
        var store = new MemoryStateStore(Armed(MachineMode.GamingArmed, MachineMode.Gaming, []));
        var system = SystemWithPlans();
        system.Processes["WidgetService"] = true;

        var result = await new ModeOrchestrator(store, system).ApplyArmedAsync();

        Assert.Equal(MachineMode.Gaming, result.CurrentMode);
        Assert.True(result.Transaction?.Completed);
        Assert.DoesNotContain("WidgetService", system.StoppedProcesses);
        Assert.True(system.Processes["WidgetService"]);
    }

    [Theory]
    [InlineData("SaladBowl")]
    [InlineData("WslService")]
    [InlineData("vmcompute")]
    [InlineData("WSearch")]
    public async Task GamingCleanup_RetriesServiceWhenItRespawnsDuringCleanWindow(string service)
    {
        var store = new MemoryStateStore(Armed(MachineMode.GamingArmed, MachineMode.Gaming, []));
        var system = SystemWithPlans();
        system.Services[service] = true;
        system.ServiceCleanWindowRespawnsRemaining[service] = 1;

        var result = await new ModeOrchestrator(store, system).ApplyArmedAsync();

        Assert.Equal(MachineMode.Gaming, result.CurrentMode);
        Assert.Equal(2, system.StoppedServices.Count(name => name.Equals(service,
            StringComparison.OrdinalIgnoreCase)));
        Assert.False(system.Services[service]);
    }

    [Fact]
    public void GamingCleanup_CleanWindowCoversObservedWSearchRestartDelayAndRemainsBounded()
    {
        Assert.True(ModePolicy.GamingCleanVerificationDelay > TimeSpan.FromSeconds(30));
        Assert.True(ModePolicy.GamingCleanVerificationDelay <= TimeSpan.FromSeconds(45));
        Assert.Equal(3, ModePolicy.GamingCleanupAttempts);
    }

    [Fact]
    public async Task GamingCleanup_ShutsDownWslBeforeStoppingWslAndVmcomputeServices()
    {
        var store = new MemoryStateStore(Armed(MachineMode.GamingArmed, MachineMode.Gaming, []));
        var system = SystemWithPlans();
        system.Services["WslService"] = true;
        system.Services["vmcompute"] = true;
        system.Processes["vmmemWSL"] = true;

        var result = await new ModeOrchestrator(store, system).ApplyArmedAsync();

        Assert.Equal(MachineMode.Gaming, result.CurrentMode);
        var shutdown = system.Operations.IndexOf("shutdown-wsl");
        var wslStop = system.Operations.IndexOf("stop-service:WslService");
        var vmcomputeStop = system.Operations.IndexOf("stop-service:vmcompute");
        Assert.True(shutdown >= 0);
        Assert.True(wslStop > shutdown);
        Assert.True(vmcomputeStop > wslStop);
        Assert.False(system.Processes["vmmemWSL"]);
    }

    [Fact]
    public async Task ArmThenApply_InvokesInlineDelayedWorkerAndDeletesOneShotTaskAfterSustainedSuccess()
    {
        var store = new MemoryStateStore();
        var system = SystemWithPlans();
        var orchestrator = new ModeOrchestrator(store, system);

        var armed = await orchestrator.ArmAsync(MachineMode.Gaming, TestHelperPath());
        Assert.Equal(MachineMode.GamingArmed, armed.CurrentMode);
        Assert.True(system.TaskPresent);
        Assert.Equal(1, system.TaskArmCount);

        var applied = await orchestrator.ApplyArmedAsync();

        Assert.Equal(MachineMode.Gaming, applied.CurrentMode);
        Assert.True(applied.Transaction?.Completed);
        Assert.Equal([ModePolicy.GamingSettleDelay, ModePolicy.GamingVerificationRetryDelay,
            ModePolicy.GamingCleanVerificationDelay], system.Delays);
        Assert.False(system.TaskPresent);
        Assert.Equal(1, system.TaskDeleteCount);
    }

    [Fact]
    public async Task GamingCleanup_SkipsWinFspWhenAServiceDependencyMakesItUnsafe()
    {
        var store = new MemoryStateStore(Armed(MachineMode.GamingArmed, MachineMode.Gaming, []));
        var system = SystemWithPlans();
        system.Services["WinFsp.Launcher"] = true;
        system.ServiceCanStopSafely["WinFsp.Launcher"] = false;
        var logs = new List<string>();

        var result = await new ModeOrchestrator(store, system, logs.Add).ApplyArmedAsync();

        Assert.Equal(MachineMode.Gaming, result.CurrentMode);
        Assert.DoesNotContain("WinFsp.Launcher", system.StoppedServices);
        Assert.Contains(logs, message => message.Contains("safety-skipped services=WinFsp.Launcher"));
    }

    [Fact]
    public async Task GamingToProgramming_RestoresServicesWithoutLaunchingHeavyApps()
    {
        var store = new MemoryStateStore(new ModeState
        {
            CurrentMode = MachineMode.Gaming,
            DesiredMode = MachineMode.Gaming,
            ChangedServices = [new ServiceSnapshot("WSearch", true)]
        });
        var system = SystemWithPlans();
        system.Services["WSearch"] = false;
        var orchestrator = new ModeOrchestrator(store, system);
        await orchestrator.ArmAsync(MachineMode.Programming, TestHelperPath());
        var result = await orchestrator.ApplyArmedAsync();
        Assert.Equal(MachineMode.Programming, result.CurrentMode);
        Assert.Contains("WSearch", system.StartedServices);
        Assert.Empty(system.StoppedProcesses);
    }

    [Fact]
    public async Task GamingToNormal_UsesBalancedFallback()
    {
        var store = new MemoryStateStore(new ModeState { CurrentMode = MachineMode.Gaming, DesiredMode = MachineMode.Gaming });
        var system = new FakeSystem();
        system.Plans.Add(ModePolicy.WindowsBalancedPowerPlan);
        var orchestrator = new ModeOrchestrator(store, system);
        await orchestrator.ArmAsync(MachineMode.Normal, TestHelperPath());
        var result = await orchestrator.ApplyArmedAsync();
        Assert.Equal(MachineMode.Normal, result.CurrentMode);
        Assert.Equal(ModePolicy.WindowsBalancedPowerPlan, system.ActivePlan);
    }

    [Fact]
    public async Task GamingToNormal_RestoresSnapshotPowerAndOnlyPreviouslyRunningServices()
    {
        const string originalPlan = "11111111-2222-3333-4444-555555555555";
        var store = new MemoryStateStore();
        var system = SystemWithPlans();
        system.Plans.Add(originalPlan);
        system.ActivePlan = originalPlan;
        system.Services["WSearch"] = true;
        system.Services["DoSvc"] = false;
        system.Processes["chrome"] = true;
        system.Processes["NVIDIA Overlay"] = true;
        var orchestrator = new ModeOrchestrator(store, system);

        await orchestrator.ArmAsync(MachineMode.Gaming, TestHelperPath());
        var gaming = await orchestrator.ApplyArmedAsync();
        Assert.Equal(MachineMode.Gaming, gaming.CurrentMode);
        Assert.False(system.Services["WSearch"]);

        await orchestrator.ArmAsync(MachineMode.Normal, TestHelperPath());
        var normal = await orchestrator.ApplyArmedAsync();

        Assert.Equal(MachineMode.Normal, normal.CurrentMode);
        Assert.Equal(MachineMode.Normal, normal.DesiredMode);
        Assert.True(normal.Transaction?.Completed);
        Assert.Null(normal.Transaction?.Error);
        Assert.Null(normal.LastError);
        Assert.Equal(originalPlan, system.ActivePlan);
        Assert.Contains("WSearch", system.StartedServices);
        Assert.DoesNotContain("DoSvc", system.StartedServices);
        Assert.True(system.Processes["chrome"]);
        Assert.True(system.Processes["NVIDIA Overlay"]);
        Assert.DoesNotContain("chrome", system.StoppedProcesses);
        Assert.DoesNotContain("NVIDIA Overlay", system.StoppedProcesses);
        Assert.Empty(normal.ChangedServices);
        Assert.Null(normal.PreviousPowerPlan);
        Assert.False(system.TaskPresent);
    }

    [Fact]
    public async Task InterruptedTransition_LoadsAsRecoveryRequiredAndRecovers()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"KCxWare-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "state.json");
        try
        {
            var diskStore = new KCxWare.Core.Persistence.JsonStateStore(path);
            await diskStore.SaveAsync(new ModeState
            {
                CurrentMode = MachineMode.GamingArmed,
                DesiredMode = MachineMode.Gaming,
                PreviousPowerPlan = "existing-plan",
                ChangedServices = [new ServiceSnapshot("WSearch", true)],
                Transaction = new TransitionRecord("test", MachineMode.Normal, MachineMode.GamingArmed, DateTimeOffset.UtcNow, false)
            });
            var detected = await diskStore.LoadAsync();
            Assert.Equal(MachineMode.RecoveryRequired, detected.CurrentMode);
            var system = SystemWithPlans();
            system.Plans.Add("existing-plan");
            system.Services["WSearch"] = false;
            var result = await new ModeOrchestrator(diskStore, system).RecoverAsync();
            Assert.Equal(MachineMode.Normal, result.CurrentMode);
            Assert.Contains("WSearch", system.StartedServices);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task RepeatedArm_IsIdempotent()
    {
        var store = new MemoryStateStore();
        var system = SystemWithPlans();
        var orchestrator = new ModeOrchestrator(store, system);
        await orchestrator.ArmAsync(MachineMode.Gaming, TestHelperPath());
        await orchestrator.ArmAsync(MachineMode.Gaming, TestHelperPath());
        Assert.Equal(1, system.TaskArmCount);
    }

    [Fact]
    public async Task CancelArmed_ReturnsToActualOriginMode()
    {
        var store = new MemoryStateStore(new ModeState
        {
            CurrentMode = MachineMode.Programming,
            DesiredMode = MachineMode.Programming
        });
        var system = SystemWithPlans();
        var orchestrator = new ModeOrchestrator(store, system);
        await orchestrator.ArmAsync(MachineMode.Gaming, TestHelperPath());
        var result = await orchestrator.CancelArmedAsync();
        Assert.Equal(MachineMode.Programming, result.CurrentMode);
        Assert.False(system.TaskPresent);
    }

    [Fact]
    public async Task MissingServices_AreIgnored()
    {
        var store = new MemoryStateStore(Armed(MachineMode.GamingArmed, MachineMode.Gaming, []));
        var system = SystemWithPlans();
        var result = await new ModeOrchestrator(store, system).ApplyArmedAsync();
        Assert.Equal(MachineMode.Gaming, result.CurrentMode);
        Assert.Empty(system.StoppedServices);
    }

    [Fact]
    public void ProtectedServices_NeverOverlapSuppressiblePolicy()
    {
        ModePolicy.AssertSafe();
        Assert.DoesNotContain(ModePolicy.GamingSuppressibleServices, ModePolicy.ProtectedServices.Contains);
        Assert.DoesNotContain(ModePolicy.GamingSuppressibleProcesses, ModePolicy.ProtectedProcesses.Contains);
        Assert.DoesNotContain(ModePolicy.GamingBestEffortProcesses, ModePolicy.ProtectedProcesses.Contains);
        Assert.DoesNotContain(ModePolicy.GamingSuppressibleBackgroundProcesses, ModePolicy.ProtectedProcesses.Contains);
        Assert.DoesNotContain(ModePolicy.GamingShutdownVerifiedProcesses, ModePolicy.ProtectedProcesses.Contains);
    }

    [Fact]
    public void RequiredGamingDependencies_RemainExplicitlyProtected()
    {
        Assert.All(new[] { "WinDefend", "mpssvc", "Dhcp", "Dnscache", "WlanSvc", "AudioSrv",
            "AudioEndpointBuilder", "NVDisplay.ContainerLocalSystem", "NvContainerLocalSystem",
            "LGHUBUpdaterService", "WavesTBSvc", "GamingServices", "GamingServicesNet", "GameInputSvc",
            "EasyAntiCheat", "EasyAntiCheat_EOS", "BEService" },
            service => Assert.Contains(service, ModePolicy.ProtectedServices));
        Assert.All(new[] { "MsMpEng", "explorer", "dwm", "audiodg", "NVDisplay.Container", "nvcontainer",
            "lghub", "GamingServices", "GamingServicesNet", "EpicGamesLauncher",
            "FortniteClient-Win64-Shipping", "EasyAntiCheat", "EasyAntiCheat_EOS", "BEService" },
            process => Assert.Contains(process, ModePolicy.ProtectedProcesses));
        Assert.All(new[] { "chrome", "chrome-native-host", "NVIDIA Overlay", "NVIDIA Share",
            "PresentMonService", "nvsphelper64", "nvfvsdksvc_x64" },
            process => Assert.Contains(process, ModePolicy.ProtectedProcesses));
        Assert.All(new[] { "OpenRGB", "rustdesk" },
            process => Assert.Contains(ModePolicy.GamingSuppressibleProcesses,
                candidate => candidate.Equals(process, StringComparison.OrdinalIgnoreCase)));
        Assert.DoesNotContain(ModePolicy.GamingSuppressibleProcesses,
            process => process.Equals("WidgetService", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ProtectedServiceStop_IsRejectedBeforeExecutingCommand()
    {
        var runner = new RecordingRunner();
        var controller = new WindowsSystemController(runner);
        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.StopServiceAsync("WinDefend"));
        Assert.Equal(0, runner.Calls);
    }

    [Fact]
    public async Task ProtectedProcessStop_IsRejectedBeforeProcessEnumeration()
    {
        var controller = new WindowsSystemController(new RecordingRunner());
        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.StopProcessAsync("MsMpEng"));
    }

    [Fact]
    public async Task ServiceStop_WaitsThroughStopPendingUntilStopped()
    {
        var runner = new SequencedRunner(
            new CommandResult(0, "STATE : 3 STOP_PENDING", string.Empty),
            new CommandResult(0, "STATE : 3 STOP_PENDING", string.Empty),
            new CommandResult(0, "STATE : 1 STOPPED", string.Empty));
        var controller = new WindowsSystemController(runner);

        await controller.StopServiceAsync("SaladBowl");

        Assert.Equal(3, runner.Calls.Count);
        Assert.Equal(["stop", "SaladBowl"], runner.Calls[0].Arguments);
        Assert.Equal(["query", "SaladBowl"], runner.Calls[1].Arguments);
        Assert.Equal(["query", "SaladBowl"], runner.Calls[2].Arguments);
    }

    [Fact]
    public async Task ApplyArmed_DeletesStaleOneShotTask()
    {
        var store = new MemoryStateStore(new ModeState());
        var system = SystemWithPlans();
        system.TaskPresent = true;
        await new ModeOrchestrator(store, system).ApplyArmedAsync();
        Assert.False(system.TaskPresent);
        Assert.Equal(1, system.TaskDeleteCount);
    }

    [Fact]
    public async Task LiveModes_CommitWithoutArmingOrReboot()
    {
        var store = new MemoryStateStore();
        var system = SystemWithPlans();
        var orchestrator = new ModeOrchestrator(store, system);

        var gaming = await orchestrator.ApplyLiveAsync(MachineMode.Gaming);
        Assert.Equal(MachineMode.Gaming, gaming.CurrentMode);
        Assert.False(gaming.RebootRequired);
        Assert.False(system.TaskPresent);

        var programming = await orchestrator.ApplyLiveAsync(MachineMode.Programming);
        Assert.Equal(MachineMode.Programming, programming.CurrentMode);
        Assert.False(programming.RebootRequired);

        var normal = await orchestrator.ApplyLiveAsync(MachineMode.Normal);
        Assert.Equal(MachineMode.Normal, normal.CurrentMode);
        Assert.False(normal.RebootRequired);
    }

    [Fact]
    public async Task NewSession_NormalizesTemporaryModeToNormal()
    {
        var initial = new ModeState
        {
            CurrentMode = MachineMode.Gaming,
            DesiredMode = MachineMode.Gaming,
            SessionId = "old-session",
            PreviousPowerPlan = "baseline",
            ChangedServices = [new ServiceSnapshot("WSearch", true)]
        };
        var store = new MemoryStateStore(initial);
        var system = SystemWithPlans();
        system.CurrentSessionId = "new-session";
        system.Services["WSearch"] = false;
        system.Plans.Add("baseline");

        var result = await new ModeOrchestrator(store, system).NormalizeAfterBootAsync();

        Assert.Equal(MachineMode.Normal, result.CurrentMode);
        Assert.Contains("WSearch", system.StartedServices);
        Assert.Equal("baseline", system.ActivePlan);
    }

    [Theory]
    [InlineData(MachineMode.Gaming)]
    [InlineData(MachineMode.Programming)]
    [InlineData(MachineMode.Normal)]
    public async Task LiveTransition_ReportsEveryPreCompletionMilestone(MachineMode target)
    {
        var progress = new RecordingProgress();
        var system = SystemWithPlans();
        var result = await new ModeOrchestrator(new MemoryStateStore(), system, progress: progress)
            .ApplyLiveAsync(target);

        Assert.Equal(target, result.CurrentMode);
        Assert.Equal([0, 20, 40, 60, 80], progress.Events.Select(item => item.Percent).Distinct().ToArray());
        Assert.True(progress.Events.All(item => item.Percent is >= 0 and <= 100));
        Assert.True(progress.Events.Zip(progress.Events.Skip(1), (left, right) => left.Percent <= right.Percent).All(value => value));
    }

    private static FakeSystem SystemWithPlans()
    {
        var system = new FakeSystem();
        system.Plans.Add(ModePolicy.GamingPowerPlan);
        system.Plans.Add(ModePolicy.AmdBalancedPowerPlan);
        system.Plans.Add(ModePolicy.WindowsBalancedPowerPlan);
        return system;
    }

    private static ModeState Armed(MachineMode armed, MachineMode desired, IReadOnlyList<ServiceSnapshot> services) =>
        new() { CurrentMode = armed, DesiredMode = desired, ChangedServices = services };

    private static string TestHelperPath() => typeof(ModeOrchestratorTests).Assembly.Location;
}
