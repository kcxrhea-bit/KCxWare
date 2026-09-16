using System.Diagnostics;
using System.Text.RegularExpressions;
using KCxWare.Core.Abstractions;
using KCxWare.Core.Models;
using KCxWare.Core.Policies;

namespace KCxWare.Core.Windows;

public sealed partial class WindowsSystemController(ICommandRunner runner,
    IServiceRecoveryPolicyStore? recoveryPolicyStore = null) : ISystemController
{
    private readonly IServiceRecoveryPolicyStore _recoveryPolicyStore =
        recoveryPolicyStore ?? new ServiceRecoveryPolicyStore();

    public Task<ServiceFailureActionsConfig> GetServiceFailureActionsAsync(string name,
        CancellationToken cancellationToken = default) =>
        _recoveryPolicyStore.GetFailureActionsAsync(name, cancellationToken);

    public Task SetServiceFailureActionsAsync(string name, ServiceFailureActionsConfig config,
        CancellationToken cancellationToken = default) =>
        _recoveryPolicyStore.SetFailureActionsAsync(name, config, cancellationToken);
    public string CurrentSessionId => Environment.TickCount64.ToString(System.Globalization.CultureInfo.InvariantCulture);
    public async Task<string?> GetActivePowerPlanAsync(CancellationToken cancellationToken = default)
    {
        var result = await runner.RunAsync("powercfg.exe", ["/getactivescheme"], cancellationToken);
        return result.ExitCode == 0 ? PowerPlanRegex().Match(result.StandardOutput).Value.ToLowerInvariant() : null;
    }

    public async Task<bool> PowerPlanExistsAsync(string planId, CancellationToken cancellationToken = default)
    {
        var result = await runner.RunAsync("powercfg.exe", ["/list"], cancellationToken);
        return result.ExitCode == 0 && result.StandardOutput.Contains(planId, StringComparison.OrdinalIgnoreCase);
    }

    public async Task SetPowerPlanAsync(string planId, CancellationToken cancellationToken = default) =>
        EnsureSuccess(await runner.RunAsync("powercfg.exe", ["/setactive", planId], cancellationToken), "select power plan");

    public async Task<bool?> IsServiceRunningAsync(string name, CancellationToken cancellationToken = default)
    {
        var result = await runner.RunAsync("sc.exe", ["query", name], cancellationToken);
        if (result.ExitCode == 1060 || result.StandardOutput.Contains("1060", StringComparison.Ordinal))
        {
            return null;
        }

        EnsureSuccess(result, $"query service {name}");
        return !result.StandardOutput.Contains("STOPPED", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<bool> CanStopServiceSafelyAsync(string name, CancellationToken cancellationToken = default)
    {
        EnsureNotProtected(name);
        if (!name.Equals("WinFsp.Launcher", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var result = await runner.RunAsync("sc.exe", ["enumdepend", name], cancellationToken);
        if (result.ExitCode != 0)
        {
            return false;
        }

        foreach (Match match in ServiceNameRegex().Matches(result.StandardOutput))
        {
            if (await IsServiceRunningAsync(match.Groups[1].Value, cancellationToken) == true)
            {
                return false;
            }
        }

        return true;
    }

    public async Task StopServiceAsync(string name, CancellationToken cancellationToken = default)
    {
        EnsureNotProtected(name);
        var result = await runner.RunAsync("sc.exe", ["stop", name], cancellationToken);
        if (result.ExitCode == 1062)
        {
            return;
        }

        EnsureSuccess(result, $"stop service {name}");
        var stopWait = Stopwatch.StartNew();
        while (await IsServiceRunningAsync(name, cancellationToken) == true)
        {
            if (stopWait.Elapsed >= ModePolicy.ServiceStopTimeout)
            {
                throw new InvalidOperationException(
                    $"Service {name} did not reach STOPPED within {ModePolicy.ServiceStopTimeout.TotalSeconds:0} seconds.");
            }

            await Task.Delay(ModePolicy.ServiceStopPollDelay, cancellationToken);
        }
    }

    public async Task StartServiceAsync(string name, CancellationToken cancellationToken = default)
    {
        var result = await runner.RunAsync("sc.exe", ["start", name], cancellationToken);
        if (result.ExitCode != 1056)
        {
            EnsureSuccess(result, $"start service {name}");
        }
    }

    public Task<bool> IsProcessRunningAsync(string name, bool backgroundOnly = false,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureNotProtectedProcess(name);
        var processes = GetTargetProcesses(name, backgroundOnly);
        var running = processes.Count != 0;
        foreach (var process in processes)
        {
            process.Dispose();
        }

        return Task.FromResult(running);
    }

    public async Task StopProcessAsync(string name, bool backgroundOnly = false,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureNotProtectedProcess(name);
        foreach (var process in GetTargetProcesses(name, backgroundOnly))
        {
            using (process)
            {
                try
                {
                    if (process.CloseMainWindow())
                    {
                        using var gracePeriod = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        gracePeriod.CancelAfter(TimeSpan.FromSeconds(3));
                        try
                        {
                            await process.WaitForExitAsync(gracePeriod.Token);
                        }
                        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                        {
                            process.Kill(true);
                        }
                    }
                    else
                    {
                        process.Kill(true);
                    }
                }
                catch (InvalidOperationException)
                {
                    // The process exited between enumeration and suppression.
                }
            }
        }
    }

    public async Task ShutdownWslAsync(CancellationToken cancellationToken = default)
    {
        var wslPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "wsl.exe");
        if (File.Exists(wslPath))
        {
            EnsureSuccess(await runner.RunAsync(wslPath, ["--shutdown"], cancellationToken), "shut down WSL");
        }
    }

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default) =>
        Task.Delay(delay, cancellationToken);

    public async Task ArmOneShotTaskAsync(string helperPath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(helperPath))
        {
            throw new FileNotFoundException("The elevated helper is missing.", helperPath);
        }

        var action = $"\"{helperPath}\" apply-armed";
        EnsureSuccess(await runner.RunAsync("schtasks.exe",
            ["/Create", "/F", "/SC", "ONLOGON", "/RL", "HIGHEST", "/TN", ModePolicy.TransitionTaskName, "/TR", action],
            cancellationToken), "create one-shot transition task");
    }

    public async Task<bool> IsOneShotTaskPresentAsync(CancellationToken cancellationToken = default)
    {
        var result = await runner.RunAsync("schtasks.exe", ["/Query", "/TN", ModePolicy.TransitionTaskName], cancellationToken);
        return result.ExitCode == 0;
    }

    public async Task DeleteOneShotTaskAsync(CancellationToken cancellationToken = default)
    {
        var result = await runner.RunAsync("schtasks.exe", ["/Delete", "/F", "/TN", ModePolicy.TransitionTaskName], cancellationToken);
        if (result.ExitCode != 0 && !result.StandardError.Contains("cannot find", StringComparison.OrdinalIgnoreCase))
        {
            EnsureSuccess(result, "delete one-shot transition task");
        }
    }

    private static void EnsureNotProtected(string name)
    {
        if (ModePolicy.ProtectedServices.Contains(name))
        {
            throw new InvalidOperationException($"Refusing to stop protected service {name}.");
        }
    }

    private static void EnsureNotProtectedProcess(string name)
    {
        if (ModePolicy.ProtectedProcesses.Contains(name))
        {
            throw new InvalidOperationException($"Refusing to stop protected process {name}.");
        }
    }

    private static IReadOnlyList<Process> GetTargetProcesses(string name, bool backgroundOnly)
    {
        var processes = Process.GetProcessesByName(name);
        var activeProcesses = new List<Process>(processes.Length);
        foreach (var process in processes)
        {
            try
            {
                if (!process.HasExited)
                {
                    activeProcesses.Add(process);
                }
                else
                {
                    process.Dispose();
                }
            }
            catch (InvalidOperationException)
            {
                process.Dispose();
            }
        }

        if (!backgroundOnly)
        {
            return activeProcesses;
        }

        try
        {
            if (activeProcesses.Any(process => process.MainWindowHandle != IntPtr.Zero))
            {
                foreach (var process in activeProcesses)
                {
                    process.Dispose();
                }

                return [];
            }

            return activeProcesses;
        }
        catch (InvalidOperationException)
        {
            foreach (var process in activeProcesses)
            {
                process.Dispose();
            }

            return [];
        }
    }

    private static void EnsureSuccess(CommandResult result, string operation)
    {
        if (result.ExitCode != 0)
        {
            var detail = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
            throw new InvalidOperationException($"Failed to {operation} (exit {result.ExitCode}): {detail.Trim()}");
        }
    }

    [GeneratedRegex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex PowerPlanRegex();

    [GeneratedRegex(@"SERVICE_NAME:\s*(\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex ServiceNameRegex();
}
