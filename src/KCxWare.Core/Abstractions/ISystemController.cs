namespace KCxWare.Core.Abstractions;

public interface ISystemController
{
    Task<string?> GetActivePowerPlanAsync(CancellationToken cancellationToken = default);
    Task<bool> PowerPlanExistsAsync(string planId, CancellationToken cancellationToken = default);
    Task SetPowerPlanAsync(string planId, CancellationToken cancellationToken = default);
    Task<bool?> IsServiceRunningAsync(string name, CancellationToken cancellationToken = default);
    Task StopServiceAsync(string name, CancellationToken cancellationToken = default);
    Task StartServiceAsync(string name, CancellationToken cancellationToken = default);
    Task StopProcessAsync(string name, CancellationToken cancellationToken = default);
    Task ArmOneShotTaskAsync(string helperPath, CancellationToken cancellationToken = default);
    Task<bool> IsOneShotTaskPresentAsync(CancellationToken cancellationToken = default);
    Task DeleteOneShotTaskAsync(CancellationToken cancellationToken = default);
}
