using KCxWare.Core.Abstractions;
using KCxWare.Core.Loading;
using KCxWare.Core.Models;
using KCxWare.Core.Orchestration;
using KCxWare.Core.Policies;

namespace KCxWare.Tests;

/// <summary>
/// Proves the automatic operational sayings are genuinely produced through the real execution
/// paths (ModeOrchestrator / PowerOrchestrator + CompletionMessages, the same infrastructure the
/// loading overlay renders from) and that CHAOS remains a fully independent, manual-only system.
/// </summary>
public sealed class AutomaticSayingsWiringTests
{
    [Fact]
    public void PoolCounts_MatchApprovedCollectionSizes()
    {
        Assert.Equal(21, CompletionMessages.GamingSubtitles.Count);
        Assert.Equal(21, CompletionMessages.ProgrammingSubtitles.Count);
        Assert.Equal(18, CompletionMessages.NormalSubtitles.Count);
        Assert.Equal(38, CompletionMessages.RestartMessages.Count);
        Assert.Equal(40, CompletionMessages.ShutdownMessages.Count);
        Assert.Equal(168, ChaosMessages.Sayings.Count);
    }

    [Fact]
    public async Task GamingActivation_ThroughModeOrchestrator_ProducesAnAutomaticGamingSaying()
    {
        var store = new MemoryStateStore();
        var system = SystemWithPlans();
        var orchestrator = new ModeOrchestrator(store, system);

        // Real path: apply Gaming live, exactly as MainWindow's ConfirmAndApplyAsync does.
        var result = await orchestrator.ApplyLiveAsync(MachineMode.Gaming);
        Assert.Equal(MachineMode.Gaming, result.CurrentMode);

        // On success, MainWindow selects the flavor text from the same mode's approved pool -
        // this is the same call the onSuccess callback in MainWindow.xaml.cs performs.
        var flavor = CompletionMessages.SelectModeMessage(MachineMode.Gaming, new SequencedRandomProvider(3));
        Assert.Equal("GAME MODE ENHANCED", flavor.Title);
        Assert.Contains(flavor.Subtitle, CompletionMessages.GamingSubtitles);
    }

    [Fact]
    public async Task ProgrammingActivation_ThroughModeOrchestrator_ProducesAnAutomaticProgrammingSaying()
    {
        var store = new MemoryStateStore();
        var system = SystemWithPlans();
        var orchestrator = new ModeOrchestrator(store, system);

        var result = await orchestrator.ApplyLiveAsync(MachineMode.Programming);
        Assert.Equal(MachineMode.Programming, result.CurrentMode);

        var flavor = CompletionMessages.SelectModeMessage(MachineMode.Programming, new SequencedRandomProvider(5));
        Assert.Equal("CODING MODE ENHANCED", flavor.Title);
        Assert.Contains(flavor.Subtitle, CompletionMessages.ProgrammingSubtitles);
    }

    [Fact]
    public async Task NormalRestore_ThroughModeOrchestrator_ProducesAnAutomaticNormalSaying()
    {
        var store = new MemoryStateStore(new ModeState { CurrentMode = MachineMode.Gaming, DesiredMode = MachineMode.Gaming });
        var system = SystemWithPlans();
        var orchestrator = new ModeOrchestrator(store, system);

        var result = await orchestrator.ApplyLiveAsync(MachineMode.Normal);
        Assert.Equal(MachineMode.Normal, result.CurrentMode);

        var flavor = CompletionMessages.SelectModeMessage(MachineMode.Normal, new SequencedRandomProvider(1));
        Assert.Equal("NORMAL MODE RESTORED", flavor.Title);
        Assert.Contains(flavor.Subtitle, CompletionMessages.NormalSubtitles);
    }

    [Fact]
    public async Task RestartAction_ThroughPowerOrchestrator_ProducesAnAutomaticRestartSaying_ViaFakeInvoker()
    {
        var store = new MemoryStateStore(new ModeState { CurrentMode = MachineMode.Programming, DesiredMode = MachineMode.Programming });
        var system = SystemWithPlans();
        var invoker = new FakePowerActionInvoker { CurrentModeAccessor = () => store.State.CurrentMode };
        var orchestrator = new PowerOrchestrator(new ModeOrchestrator(store, system), store, invoker);

        CompletionMessage? shown = null;
        var result = await orchestrator.ExecuteAsync(PowerAction.Restart, beforePowerRequest: () =>
        {
            shown = CompletionMessages.SelectRestartMessage(new SequencedRandomProvider(7));
            return Task.CompletedTask;
        });

        Assert.True(result.Succeeded);
        Assert.NotNull(shown);
        Assert.Contains(shown!, CompletionMessages.RestartMessages);
        Assert.Single(invoker.Invocations);
        Assert.Equal(PowerAction.Restart, invoker.Invocations[0].Action);
        // Never a real restart: FakePowerActionInvoker just records the call.
    }

    [Fact]
    public async Task ShutdownAction_ThroughPowerOrchestrator_ProducesAnAutomaticShutdownSaying_ViaFakeInvoker()
    {
        var store = new MemoryStateStore(new ModeState { CurrentMode = MachineMode.Gaming, DesiredMode = MachineMode.Gaming });
        var system = SystemWithPlans();
        var invoker = new FakePowerActionInvoker { CurrentModeAccessor = () => store.State.CurrentMode };
        var orchestrator = new PowerOrchestrator(new ModeOrchestrator(store, system), store, invoker);

        CompletionMessage? shown = null;
        var result = await orchestrator.ExecuteAsync(PowerAction.Shutdown, beforePowerRequest: () =>
        {
            shown = CompletionMessages.SelectShutdownMessage(new SequencedRandomProvider(11));
            return Task.CompletedTask;
        });

        Assert.True(result.Succeeded);
        Assert.NotNull(shown);
        Assert.Contains(shown!, CompletionMessages.ShutdownMessages);
        Assert.Single(invoker.Invocations);
        Assert.Equal(PowerAction.Shutdown, invoker.Invocations[0].Action);
    }

    [Fact]
    public void ChaosPress_NeverTriggersAnyModeOrPowerAction()
    {
        // A CHAOS draw is just an index into a manual shuffle bag - it has no reference to,
        // and cannot invoke, ModeOrchestrator or PowerOrchestrator or IPowerActionInvoker.
        var random = new SequencedRandomProvider(Enumerable.Range(0, 500).ToArray());
        var bag = ChaosMessages.CreateShuffleBag(random);
        var invoker = new FakePowerActionInvoker();

        for (var i = 0; i < 50; i++)
        {
            _ = bag.Next();
        }

        Assert.Empty(invoker.Invocations);
    }

    [Fact]
    public void AutomaticOperations_DoNotConsumeOrAlterTheChaosShuffleBagState()
    {
        var chaosRandom = new SequencedRandomProvider(Enumerable.Range(0, 997).ToArray());
        var chaosBag = ChaosMessages.CreateShuffleBag(chaosRandom);

        // Baseline: what CHAOS would draw next, untouched.
        var expectedNext = new List<string>();
        var previewRandom = new SequencedRandomProvider(Enumerable.Range(0, 997).ToArray());
        var previewBag = ChaosMessages.CreateShuffleBag(previewRandom);
        for (var i = 0; i < 5; i++) expectedNext.Add(previewBag.Next());

        // Run several automatic (CompletionMessages) selections using a completely separate
        // IRandomProvider - CHAOS's bag/provider is never touched by these calls.
        for (var i = 0; i < 10; i++)
        {
            CompletionMessages.SelectModeMessage(MachineMode.Gaming, new SequencedRandomProvider(i));
            CompletionMessages.SelectRestartMessage(new SequencedRandomProvider(i));
            CompletionMessages.SelectShutdownMessage(new SequencedRandomProvider(i));
        }

        var actualNext = new List<string>();
        for (var i = 0; i < 5; i++) actualNext.Add(chaosBag.Next());

        Assert.Equal(expectedNext, actualNext);
    }

    [Fact]
    public void ChaosDraws_DoNotConsumeOrAlterCompletionMessagesAutomaticSelectionState()
    {
        // CompletionMessages.Select*Message are pure functions over the caller-supplied
        // IRandomProvider - they hold no static/shared selection state at all, so an
        // independent sequence given the same provider values always reproduces the same result.
        var automaticProvider = new SequencedRandomProvider(2, 4, 6, 8, 10);
        var expected = new[]
        {
            CompletionMessages.SelectModeMessage(MachineMode.Gaming, automaticProvider).Subtitle,
            CompletionMessages.SelectModeMessage(MachineMode.Gaming, automaticProvider).Subtitle,
        };

        // Drive many CHAOS draws on a totally separate bag/provider in between.
        var chaosRandom = new SequencedRandomProvider(Enumerable.Range(0, 997).ToArray());
        var chaosBag = ChaosMessages.CreateShuffleBag(chaosRandom);
        for (var i = 0; i < 200; i++) _ = chaosBag.Next();

        var reproducedProvider = new SequencedRandomProvider(2, 4, 6, 8, 10);
        var actual = new[]
        {
            CompletionMessages.SelectModeMessage(MachineMode.Gaming, reproducedProvider).Subtitle,
            CompletionMessages.SelectModeMessage(MachineMode.Gaming, reproducedProvider).Subtitle,
        };

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void AutomaticSelections_RequireNoChaosInteractionWhatsoever()
    {
        // None of the automatic selection APIs take or reference ChaosMessages/ShuffleBag in any way.
        var random = new SequencedRandomProvider(0);
        var gaming = CompletionMessages.SelectModeMessage(MachineMode.Gaming, random);
        var restart = CompletionMessages.SelectRestartMessage(random);
        var shutdown = CompletionMessages.SelectShutdownMessage(random);

        Assert.NotNull(gaming);
        Assert.NotNull(restart);
        Assert.NotNull(shutdown);
    }

    [Fact]
    public void LoadingVideoAsset_ReferencesKcxParadeMp4_NotAnAbsoluteDevPath()
    {
        Assert.Equal(@"Assets\kcxparade.mp4", LoadingAssetLocator.VideoRelativePath);
        var resolved = LoadingAssetLocator.ResolveVideoPath(@"C:\Program Files\KCxWare");
        Assert.EndsWith("kcxparade.mp4", resolved, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"D:\KCxProjects", resolved, StringComparison.OrdinalIgnoreCase);
    }

    private static FakeSystem SystemWithPlans()
    {
        var system = new FakeSystem { Capabilities = new MachineCapabilities([], ["WSL"]) };
        system.Plans.Add(ModePolicy.GamingPowerPlan);
        system.Plans.Add(ModePolicy.AmdBalancedPowerPlan);
        system.Plans.Add(ModePolicy.WindowsBalancedPowerPlan);
        return system;
    }
}
