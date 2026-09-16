using KCxWare.Core.Abstractions;
using KCxWare.Core.Models;
using KCxWare.Core.Orchestration;
using KCxWare.Core.Policies;

namespace KCxWare.Tests;

public sealed class PowerOrchestratorTests
{
    [Fact]
    public async Task RestartFromGaming_RestoresNormalBeforeRestarting()
    {
        var store = new MemoryStateStore(new ModeState
        {
            CurrentMode = MachineMode.Gaming,
            DesiredMode = MachineMode.Gaming,
            ChangedServices = [new ServiceSnapshot("WSearch", true)],
            PreviousPowerPlan = ModePolicy.WindowsBalancedPowerPlan
        });
        var system = SystemWithPlans();
        system.Services["WSearch"] = false;

        var invoker = new FakePowerActionInvoker { CurrentModeAccessor = () => store.State.CurrentMode };
        var orchestrator = new PowerOrchestrator(new ModeOrchestrator(store, system), store, invoker);

        var result = await orchestrator.ExecuteAsync(PowerAction.Restart);

        Assert.True(result.Succeeded);
        Assert.Contains("WSearch", system.StartedServices);
        Assert.Single(invoker.Invocations);
        Assert.Equal(PowerAction.Restart, invoker.Invocations[0].Action);
        // The power action must only ever be invoked once restoration has already landed Normal.
        Assert.Equal(MachineMode.Normal, invoker.Invocations[0].ModeAtInvocationTime);
        Assert.Equal(MachineMode.Normal, store.State.CurrentMode);
    }

    [Fact]
    public async Task ShutdownFromProgramming_RestoresNormalBeforeShuttingDown()
    {
        var store = new MemoryStateStore(new ModeState
        {
            CurrentMode = MachineMode.Programming,
            DesiredMode = MachineMode.Programming
        });
        var system = SystemWithPlans();
        var invoker = new FakePowerActionInvoker { CurrentModeAccessor = () => store.State.CurrentMode };
        var orchestrator = new PowerOrchestrator(new ModeOrchestrator(store, system), store, invoker);

        var result = await orchestrator.ExecuteAsync(PowerAction.Shutdown);

        Assert.True(result.Succeeded);
        Assert.Single(invoker.Invocations);
        Assert.Equal(PowerAction.Shutdown, invoker.Invocations[0].Action);
        Assert.Equal(MachineMode.Normal, invoker.Invocations[0].ModeAtInvocationTime);
        Assert.Equal(MachineMode.Normal, store.State.CurrentMode);
    }

    [Fact]
    public async Task RestorationFailureBeforeRestart_RestartIsNeverInvoked()
    {
        var store = new MemoryStateStore(new ModeState { CurrentMode = MachineMode.Gaming, DesiredMode = MachineMode.Gaming });
        store.FailNextSave = true; // Injected failure during ModeOrchestrator.RecoverAsync's save.
        var system = SystemWithPlans();
        var invoker = new FakePowerActionInvoker();
        var orchestrator = new PowerOrchestrator(new ModeOrchestrator(store, system), store, invoker);

        var result = await orchestrator.ExecuteAsync(PowerAction.Restart);

        Assert.False(result.Succeeded);
        Assert.Equal(PowerResultStatus.RestorationFailed, result.Status);
        Assert.Empty(invoker.Invocations);
        Assert.Equal(MachineMode.RecoveryRequired, store.State.CurrentMode);
    }

    [Fact]
    public async Task RestorationFailureBeforeShutdown_ShutdownIsNeverInvoked()
    {
        var store = new MemoryStateStore(new ModeState { CurrentMode = MachineMode.Programming, DesiredMode = MachineMode.Programming });
        store.FailNextSave = true;
        var system = SystemWithPlans();
        var invoker = new FakePowerActionInvoker();
        var orchestrator = new PowerOrchestrator(new ModeOrchestrator(store, system), store, invoker);

        var result = await orchestrator.ExecuteAsync(PowerAction.Shutdown);

        Assert.False(result.Succeeded);
        Assert.Equal(PowerResultStatus.RestorationFailed, result.Status);
        Assert.Empty(invoker.Invocations);
    }

    [Fact]
    public async Task RestartWhileAlreadyNormal_DoesNotRecaptureOrMutateBaseline()
    {
        var store = new MemoryStateStore(new ModeState { CurrentMode = MachineMode.Normal, DesiredMode = MachineMode.Normal });
        var system = SystemWithPlans();
        var invoker = new FakePowerActionInvoker();
        var orchestrator = new PowerOrchestrator(new ModeOrchestrator(store, system), store, invoker);

        var result = await orchestrator.ExecuteAsync(PowerAction.Restart);

        Assert.True(result.Succeeded);
        Assert.Single(invoker.Invocations);
        // No restoration path was taken: the one-shot task machinery was never touched.
        Assert.Equal(0, system.TaskDeleteCount);
        Assert.Equal(0, system.WslShutdownCount);
    }

    [Fact]
    public async Task ShutdownWhileAlreadyNormal_DoesNotRecaptureOrMutateBaseline()
    {
        var store = new MemoryStateStore(new ModeState { CurrentMode = MachineMode.Normal, DesiredMode = MachineMode.Normal });
        var system = SystemWithPlans();
        var invoker = new FakePowerActionInvoker();
        var orchestrator = new PowerOrchestrator(new ModeOrchestrator(store, system), store, invoker);

        var result = await orchestrator.ExecuteAsync(PowerAction.Shutdown);

        Assert.True(result.Succeeded);
        Assert.Single(invoker.Invocations);
        Assert.Equal(0, system.TaskDeleteCount);
    }

    [Fact]
    public void InvalidHelperPowerCommand_IsRejected()
    {
        Assert.False(Core.Windows.HelperCommandValidator.IsValidPowerCommand(["reboot"]));
        Assert.False(Core.Windows.HelperCommandValidator.IsValidPowerCommand(["restart", "--force"]));
        Assert.False(Core.Windows.HelperCommandValidator.IsValidPowerCommand([]));
        Assert.False(Core.Windows.HelperCommandValidator.IsValidPowerCommand(["shutdown /s /f"]));
        Assert.True(Core.Windows.HelperCommandValidator.IsValidPowerCommand(["restart"]));
        Assert.True(Core.Windows.HelperCommandValidator.IsValidPowerCommand(["SHUTDOWN"]));
    }

    [Fact]
    public async Task PowerActionCannotExecuteBeforeRequiredNormalRestorationSucceeds()
    {
        // beforePowerRequest records whether restoration/verification already landed Normal by
        // the time it runs; the power invoker itself is asserted to run strictly after it.
        var store = new MemoryStateStore(new ModeState { CurrentMode = MachineMode.Gaming, DesiredMode = MachineMode.Gaming });
        var system = SystemWithPlans();
        var invoker = new FakePowerActionInvoker { CurrentModeAccessor = () => store.State.CurrentMode };
        var orchestrator = new PowerOrchestrator(new ModeOrchestrator(store, system), store, invoker);

        var callOrder = new List<string>();
        var result = await orchestrator.ExecuteAsync(PowerAction.Restart, beforePowerRequest: () =>
        {
            callOrder.Add($"message-shown:{store.State.CurrentMode}");
            return Task.CompletedTask;
        });

        Assert.True(result.Succeeded);
        callOrder.Add($"power-invoked:{invoker.Invocations[0].ModeAtInvocationTime}");
        Assert.Equal(["message-shown:Normal", "power-invoked:Normal"], callOrder);
    }

    [Fact]
    public async Task ExistingGamingProgrammingNormalBehavior_IsUnaffectedByPowerOrchestrator()
    {
        // PowerOrchestrator only ever calls the existing, unmodified ModeOrchestrator.RecoverAsync;
        // arming/applying modes directly is untouched and still behaves exactly as before.
        var store = new MemoryStateStore();
        var system = SystemWithPlans();
        system.Services["WSearch"] = true;
        var modeOrchestrator = new ModeOrchestrator(store, system);

        var armed = await modeOrchestrator.ArmAsync(MachineMode.Gaming, typeof(PowerOrchestratorTests).Assembly.Location);
        Assert.Equal(MachineMode.GamingArmed, armed.CurrentMode);
        Assert.True(armed.RebootRequired);
    }

    private static FakeSystem SystemWithPlans()
    {
        var system = new FakeSystem();
        system.Plans.Add(ModePolicy.GamingPowerPlan);
        system.Plans.Add(ModePolicy.AmdBalancedPowerPlan);
        system.Plans.Add(ModePolicy.WindowsBalancedPowerPlan);
        return system;
    }
}
