namespace KCxWare.Core.Windows;

public interface IServiceStatusReader
{
    Task<uint?> QueryCurrentStateAsync(string name, CancellationToken cancellationToken = default);
}
