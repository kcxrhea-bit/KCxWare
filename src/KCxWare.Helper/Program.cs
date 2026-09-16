using System.Diagnostics;
using System.Security.Principal;
using KCxWare.Core.Models;
using KCxWare.Core.Orchestration;
using KCxWare.Core.Persistence;
using KCxWare.Core.Windows;
using KCxWare.Core.Loading;

return await HelperProgram.RunAsync(args);

internal static class HelperProgram
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (!IsAdministrator())
        {
            Console.Error.WriteLine("KCxWare.Helper requires administrator rights.");
            return 5;
        }

        var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "KCxWare", "logs", "helper.log");

        try
        {
            if (args.Length == 0)
            {
                throw new ArgumentException("Expected apply-live, normalize-after-boot, recover, or cancel.");
            }

            var progressPipe = GetOption(args, "--progress");
            var progress = progressPipe is null ? null : new NamedPipeTransitionProgressReporter(progressPipe);
            var orchestrator = new ModeOrchestrator(new JsonStateStore(),
                new WindowsSystemController(new CommandRunner()), message => WriteLog(logPath, message), progress);
            switch (args[0].ToLowerInvariant())
            {
                case "apply-live" when args.Length >= 2 && Enum.TryParse<MachineMode>(args[1], true, out var mode):
                    var appliedLive = await orchestrator.ApplyLiveAsync(mode);
                    EnsureHealthy(appliedLive);
                    WriteLog(logPath, $"Applied {mode} mode live.");
                    break;

                case "apply-armed":
                case "normalize-after-boot":
                    var normalized = await orchestrator.NormalizeAfterBootAsync();
                    EnsureHealthy(normalized);
                    WriteLog(logPath, "Normalized temporary session mode after boot.");
                    break;

                case "recover":
                    var recovered = await orchestrator.RecoverAsync();
                    EnsureHealthy(recovered);
                    WriteLog(logPath, "Recovery completed.");
                    break;

                case "cancel":
                    await orchestrator.CancelArmedAsync();
                    WriteLog(logPath, "Armed transition cancelled.");
                    break;

                default:
                    throw new ArgumentException("Invalid helper command.");
            }

            return 0;
        }
        catch (Exception exception)
        {
            WriteLog(logPath, $"ERROR: {exception.GetType().Name}: {exception.Message}");
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static void EnsureHealthy(ModeState state)
    {
        if (state.CurrentMode == MachineMode.RecoveryRequired)
        {
            throw new InvalidOperationException(state.LastError ?? "The transition requires recovery.");
        }
    }

    private static void Restart(string reason)
    {
        Process.Start(new ProcessStartInfo("shutdown.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            ArgumentList = { "/r", "/t", "10", "/d", "p:0:0", "/c", reason }
        });
    }

    private static void WriteLog(string path, string message)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.AppendAllText(path, $"{DateTimeOffset.UtcNow:O} {message}{Environment.NewLine}");
    }

    private static string? GetOption(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
