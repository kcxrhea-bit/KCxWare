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
        Assert.Equal([ModePolicy.GamingSettleDelay, ModePolicy.GamingVerificationRetryDelay], system.Delays);
        Assert.Equal(ModePolicy.GamingPowerPlan, system.ActivePlan);
        Assert.False(system.TaskPresent);
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
            ModePolicy.GamingVerificationRetryDelay], system.Delays);
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
    public async Task ApplyArmed_DeletesStaleOneShotTask()
    {
        var store = new MemoryStateStore(new ModeState());
        var system = SystemWithPlans();
        system.TaskPresent = true;
        await new ModeOrchestrator(store, system).ApplyArmedAsync();
        Assert.False(system.TaskPresent);
        Assert.Equal(1, system.TaskDeleteCount);
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
