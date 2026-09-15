using System.Diagnostics;
using System.Text;

namespace KCxWare.Core.Windows;

public sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError);

public interface ICommandRunner
{
    Task<CommandResult> RunAsync(string fileName, IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default);
}

public sealed class CommandRunner : ICommandRunner
{
    public async Task<CommandResult> RunAsync(string fileName, IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        var output = new StringBuilder();
        var error = new StringBuilder();
        process.OutputDataReceived += (_, eventArgs) => { if (eventArgs.Data is not null) output.AppendLine(eventArgs.Data); };
        process.ErrorDataReceived += (_, eventArgs) => { if (eventArgs.Data is not null) error.AppendLine(eventArgs.Data); };

        if (!process.Start())
        {
            throw new InvalidOperationException($"Unable to start {fileName}.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken);
        return new CommandResult(process.ExitCode, output.ToString(), error.ToString());
    }
}
