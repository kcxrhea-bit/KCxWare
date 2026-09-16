using System.IO.Pipes;
using System.Text.Json;

namespace KCxWare.Core.Loading;

public sealed class NamedPipeTransitionProgressReporter(string pipeName) : ITransitionProgressReporter
{
    private readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web);

    public void Report(TransitionProgress progress)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
            pipe.Connect(250);
            using var writer = new StreamWriter(pipe) { AutoFlush = true };
            writer.WriteLine(JsonSerializer.Serialize(progress, _options));
        }
        catch (IOException) { }
        catch (TimeoutException) { }
    }
}
