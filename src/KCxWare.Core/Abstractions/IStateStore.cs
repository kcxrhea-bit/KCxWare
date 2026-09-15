using KCxWare.Core.Models;

namespace KCxWare.Core.Abstractions;

public interface IStateStore
{
    Task<ModeState> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(ModeState state, CancellationToken cancellationToken = default);
}
