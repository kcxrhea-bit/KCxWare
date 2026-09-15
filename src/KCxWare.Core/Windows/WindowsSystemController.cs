using System.Diagnostics;
using System.Text.RegularExpressions;
using KCxWare.Core.Abstractions;
using KCxWare.Core.Policies;

namespace KCxWare.Core.Windows;

public sealed partial class WindowsSystemController(ICommandRunner runner) : ISystemController
{
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
        return result.StandardOutput.Contains("RUNNING", StringComparison.OrdinalIgnoreCase);
    }

    public async Task StopServiceAsync(string name, CancellationToken cancellationToken = default)
    {
        EnsureNotProtected(name);
        var result = await runner.RunAsync("sc.exe", ["stop", name], cancellationToken);
        if (result.ExitCode != 1062)
        {
            EnsureSuccess(result, $"stop service {name}");
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

    public async Task StopProcessAsync(string name, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var process in Process.GetProcessesByName(name))
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
}
