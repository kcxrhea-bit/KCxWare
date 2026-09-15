using System.Text.Json;
using KCxWare.Core.Abstractions;
using KCxWare.Core.Models;

namespace KCxWare.Core.Persistence;

public sealed class JsonStateStore : IStateStore
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _path;

    public JsonStateStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "KCxWare", "state.json");
    }

    public async Task<ModeState> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
            return new ModeState();
        }

        await using var stream = File.OpenRead(_path);
        var state = await JsonSerializer.DeserializeAsync<ModeState>(stream, Options, cancellationToken);
        if (state is null || state.SchemaVersion != ModeState.CurrentSchemaVersion)
        {
            return new ModeState { CurrentMode = MachineMode.RecoveryRequired, LastError = "Unsupported or invalid state file." };
        }

        if (state.Transaction is { Completed: false })
        {
            return state with { CurrentMode = MachineMode.RecoveryRequired, LastError = "An interrupted transition requires recovery." };
        }

        return state;
    }

    public async Task SaveAsync(ModeState state, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_path) ?? throw new InvalidOperationException("State path has no directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = _path + ".tmp";
        var backupPath = _path + ".bak";

        await using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None,
                         4096, FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(stream, state, Options, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        if (File.Exists(_path))
        {
            File.Replace(temporaryPath, _path, backupPath, true);
        }
        else
        {
            File.Move(temporaryPath, _path);
        }
    }
}
