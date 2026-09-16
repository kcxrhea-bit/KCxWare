using KCxWare.Core.Models;

namespace KCxWare.Core.Abstractions;

/// <summary>
/// Boundary around a Windows service's SCM-level failure/recovery-action configuration (queried and
/// changed via advapi32 QueryServiceConfig2/ChangeServiceConfig2 - the typed Win32 APIs that
/// <c>sc.exe qfailure</c>/<c>sc.exe failure</c> wrap). Kept as a minimal seam, mirroring
/// <see cref="IPowerActionInvoker"/>, so orchestration code never touches raw Win32 service handles
/// directly and tests can verify capture/suppress/restore ordering with a fake.
/// </summary>
public interface IServiceRecoveryPolicyStore
{
    Task<ServiceFailureActionsConfig> GetFailureActionsAsync(string serviceName,
        CancellationToken cancellationToken = default);

    Task SetFailureActionsAsync(string serviceName, ServiceFailureActionsConfig config,
        CancellationToken cancellationToken = default);
}
