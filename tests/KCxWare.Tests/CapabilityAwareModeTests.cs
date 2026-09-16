using KCxWare.Core.Models;
using KCxWare.Core.Orchestration;
using KCxWare.Core.Policies;
using KCxWare.Core.Windows;

namespace KCxWare.Tests;

public sealed class CapabilityAwareModeTests
{
    [Fact]
    public async Task ApplyLive_FromNormalWithoutReadablePowerPlan_FailsBeforeMutation()
    {
        var store = new MemoryStateStore();
        var system = new FakeSystem { ActivePlan = null };
        var orchestrator = new ModeOrchestrator(store, system);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => orchestrator.ApplyLiveAsync(MachineMode.Gaming));

        Assert.Contains("safely reversible transition cannot begin", error.Message);
        Assert.Empty(system.StoppedServices);
        Assert.Empty(system.StoppedProcesses);
        Assert.Null(system.ActivePlan);
        Assert.Equal(MachineMode.Normal, (await store.LoadAsync()).CurrentMode);
    }

    [Theory]
    [InlineData("NVIDIA")]
    [InlineData("AMD")]
    [InlineData("Intel")]
    public async Task Gaming_IsVendorNeutral_AndUsesInstalledWindowsHighPerformance(string vendor)
    {
        var system = new FakeSystem
        {
            Capabilities = new MachineCapabilities([vendor], []),
            ActivePlan = "original-plan"
        };
        system.Plans.Add(ModePolicy.WindowsHighPerformancePowerPlan);

        var result = await new ModeOrchestrator(new MemoryStateStore(), system).ApplyLiveAsync(MachineMode.Gaming);

        Assert.Equal(MachineMode.Gaming, result.CurrentMode);
        Assert.Equal(ModePolicy.WindowsHighPerformancePowerPlan, system.ActivePlan);
        Assert.Equal(1, system.CapabilityDetectionCount);
        Assert.DoesNotContain(TimeSpan.FromSeconds(30), system.Delays);
        Assert.Contains(ModePolicy.GamingCleanVerificationDelay, system.Delays);
    }

    [Fact]
    public async Task Gaming_MissingOptionalCapabilitiesAndSupportedPlans_IsStillSupported()
    {
        var system = new FakeSystem { ActivePlan = "oem-plan", Capabilities = MachineCapabilities.Empty };

        var result = await new ModeOrchestrator(new MemoryStateStore(), system).ApplyLiveAsync(MachineMode.Gaming);

        Assert.Equal(MachineMode.Gaming, result.CurrentMode);
        Assert.Equal("oem-plan", system.ActivePlan);
        Assert.Empty(system.StoppedServices);
        Assert.Empty(system.StoppedProcesses);
    }

    [Fact]
    public async Task Programming_RecognizesCapabilitiesWithoutLaunchingApplications()
    {
        var capabilities = new MachineCapabilities(["Intel"],
            ["Git", "Git Bash", "Node.js", "npm", "WSL", "Docker", "Ollama", "Visual Studio"]);
        var system = new FakeSystem { Capabilities = capabilities };
        system.Plans.Add(ModePolicy.WindowsHighPerformancePowerPlan);
        var logs = new List<string>();

        var result = await new ModeOrchestrator(new MemoryStateStore(), system, logs.Add)
            .ApplyLiveAsync(MachineMode.Programming);

        Assert.Equal(MachineMode.Programming, result.CurrentMode);
        Assert.Empty(system.StoppedProcesses);
        Assert.Contains(logs, message => message.Contains("Git Bash") && message.Contains("Node.js") &&
            message.Contains("Docker") && message.Contains("Ollama"));
    }

    [Fact]
    public async Task GamingToNormal_RestoresExactReversibleBaselineFromThisPc()
    {
        const string originalPlan = "11111111-2222-3333-4444-555555555555";
        var originalRecovery = new ServiceFailureActionsConfig(86400, null, null,
            [new ServiceFailureAction(ServiceFailureActionType.RestartService, 30000)], true);
        var store = new MemoryStateStore();
        var system = new FakeSystem { ActivePlan = originalPlan };
        system.Plans.Add(originalPlan);
        system.Plans.Add(ModePolicy.WindowsHighPerformancePowerPlan);
        system.Services["WSearch"] = true;
        system.Services["DoSvc"] = false;
        system.FailureActionsConfigured["WSearch"] = originalRecovery;
        var orchestrator = new ModeOrchestrator(store, system);

        var gaming = await orchestrator.ApplyLiveAsync(MachineMode.Gaming);
        Assert.Equal(MachineMode.Gaming, gaming.CurrentMode);
        Assert.False(system.Services["WSearch"]);

        var normal = await orchestrator.ApplyLiveAsync(MachineMode.Normal);

        Assert.Equal(MachineMode.Normal, normal.CurrentMode);
        Assert.True(system.Services["WSearch"]);
        Assert.False(system.Services["DoSvc"]);
        Assert.Equal(originalPlan, system.ActivePlan);
        Assert.Equal(originalRecovery, system.FailureActionsConfigured["WSearch"]);
        Assert.Empty(normal.ChangedServices);
        Assert.Null(normal.PreviousPowerPlan);
    }

    [Theory]
    [InlineData("NVIDIA GeForce RTX 4080", "NVIDIA")]
    [InlineData("AMD Radeon RX 7900 XTX", "AMD")]
    [InlineData("Intel(R) Arc(TM) A770", "Intel")]
    public async Task CapabilityDetection_RecognizesCommonGraphicsAndDevelopmentTools(
        string adapterName, string expectedVendor)
    {
        var runner = new CapabilityRunner(adapterName,
            new HashSet<string>(["git.exe", "node.exe", "npm.cmd", "docker.exe", "ollama.exe", "dotnet.exe", "code.cmd"],
                StringComparer.OrdinalIgnoreCase));
        var capabilities = await new WindowsSystemController(runner).DetectCapabilitiesAsync();

        Assert.Contains(expectedVendor, capabilities.GraphicsVendors);
        Assert.Contains("Git", capabilities.DevelopmentTools);
        Assert.Contains("Node.js", capabilities.DevelopmentTools);
        Assert.Contains("npm", capabilities.DevelopmentTools);
        Assert.Contains("Docker", capabilities.DevelopmentTools);
        Assert.Contains("Ollama", capabilities.DevelopmentTools);
    }

    private sealed class CapabilityRunner(string adapterName, IReadOnlySet<string> availableCommands) : ICommandRunner
    {
        public Task<CommandResult> RunAsync(string fileName, IReadOnlyList<string> arguments,
            CancellationToken cancellationToken = default)
        {
            if (fileName.Equals("powershell.exe", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new CommandResult(0, adapterName, string.Empty));
            }

            var command = arguments.Single();
            return Task.FromResult(availableCommands.Contains(command)
                ? new CommandResult(0, $@"C:\Tools\{command}", string.Empty)
                : new CommandResult(1, string.Empty, "not found"));
        }
    }
}
