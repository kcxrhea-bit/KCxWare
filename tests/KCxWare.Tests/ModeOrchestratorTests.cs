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
        var result = await new ModeOrchestrator(store, system).ApplyArmedAsync();
        Assert.Equal(MachineMode.Gaming, result.CurrentMode);
        Assert.Contains("WSearch", system.StoppedServices);
        Assert.Equal(ModePolicy.GamingPowerPlan, system.ActivePlan);
        Assert.False(system.TaskPresent);
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
