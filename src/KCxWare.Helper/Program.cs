using System.Diagnostics;
using System.Security.Principal;
using KCxWare.Core.Models;
using KCxWare.Core.Orchestration;
using KCxWare.Core.Persistence;
using KCxWare.Core.Windows;

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
                throw new ArgumentException("Expected arm, apply-armed, recover, or cancel.");
            }

            var orchestrator = new ModeOrchestrator(new JsonStateStore(),
                new WindowsSystemController(new CommandRunner()), message => WriteLog(logPath, message));
            switch (args[0].ToLowerInvariant())
            {
                case "arm" when args.Length >= 2 && Enum.TryParse<MachineMode>(args[1], true, out var mode):
                    var helperPath = Environment.ProcessPath ?? throw new InvalidOperationException("Helper path is unavailable.");
                    var armed = await orchestrator.ArmAsync(mode, helperPath);
                    EnsureHealthy(armed);
                    WriteLog(logPath, $"Armed {mode} transition.");
                    if (args.Contains("--reboot", StringComparer.OrdinalIgnoreCase))
                    {
                        Restart($"KCxWare is restarting Windows into {mode} mode.");
                    }
                    break;

                case "apply-armed":
                    var applied = await orchestrator.ApplyArmedAsync();
                    EnsureHealthy(applied);
                    WriteLog(logPath, $"Applied {applied.CurrentMode} mode.");
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
}
