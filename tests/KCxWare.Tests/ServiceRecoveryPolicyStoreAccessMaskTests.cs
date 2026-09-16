using KCxWare.Core.Windows;

namespace KCxWare.Tests;

/// <summary>
/// Regression coverage for a real-machine failure observed in production: <c>ChangeServiceConfig2W</c>
/// returning <c>ERROR_ACCESS_DENIED</c> (Win32 error 5) when <c>ServiceRecoveryPolicyStore</c> attempted
/// to restore/suppress WSearch's failure actions. The leading hypothesis was that the service handle
/// used for the mutating (<c>Set</c>) path was opened without <c>SERVICE_CHANGE_CONFIG</c> - e.g. reusing
/// a handle opened only for <c>SERVICE_QUERY_CONFIG</c>, or some other combination missing the one right
/// <c>ChangeServiceConfig2W</c> actually needs.
///
/// Investigation of <see cref="ServiceRecoveryPolicyStore"/> found the access mask was already correct:
/// the mutating path opens its handle with <c>QueryAccessMask | ChangeAccessMask</c>-equivalent rights
/// (<see cref="ServiceRecoveryPolicyStore.ChangeAccessMask"/> = <c>SERVICE_CHANGE_CONFIG | SERVICE_QUERY_CONFIG</c>
/// = 0x0003), which includes <c>SERVICE_CHANGE_CONFIG</c> (required by <c>ChangeServiceConfig2W</c>) and
/// <c>SERVICE_QUERY_CONFIG</c> (required by the fail-closed readback verification performed on the same
/// handle immediately after the write). No P/Invoke-level fake exists to intercept the literal value
/// passed to <c>OpenServiceW</c>, so these tests instead pin the named, testable constants the production
/// code paths actually use - <see cref="ServiceRecoveryPolicyStore.QueryAccessMask"/> and
/// <see cref="ServiceRecoveryPolicyStore.ChangeAccessMask"/> - to their expected numeric values. If a
/// future edit narrows <c>ChangeAccessMask</c> back down (e.g. dropping <c>SERVICE_CHANGE_CONFIG</c> or
/// <c>SERVICE_QUERY_CONFIG</c>), or broadens either mask to <c>SERVICE_ALL_ACCESS</c>, this fails
/// immediately.
/// </summary>
public sealed class ServiceRecoveryPolicyStoreAccessMaskTests
{
    private const uint ServiceQueryConfig = 0x0001;
    private const uint ServiceChangeConfig = 0x0002;
    private const uint ServiceAllAccess = 0xF01FF;

    [Fact]
    public void QueryAccessMask_RequestsOnlyQueryConfig()
    {
        Assert.Equal(ServiceQueryConfig, ServiceRecoveryPolicyStore.QueryAccessMask);
    }

    [Fact]
    public void ChangeAccessMask_RequestsChangeConfigAndQueryConfig()
    {
        Assert.Equal(ServiceChangeConfig | ServiceQueryConfig, ServiceRecoveryPolicyStore.ChangeAccessMask);

        // SERVICE_CHANGE_CONFIG is the specific right ChangeServiceConfig2W requires; this is the bit
        // whose absence would reproduce the real-machine ERROR_ACCESS_DENIED (Win32 error 5).
        Assert.True((ServiceRecoveryPolicyStore.ChangeAccessMask & ServiceChangeConfig) == ServiceChangeConfig,
            "ChangeAccessMask must include SERVICE_CHANGE_CONFIG or ChangeServiceConfig2W will fail with " +
            "ERROR_ACCESS_DENIED.");

        // SERVICE_QUERY_CONFIG is required for the fail-closed readback verification performed on the
        // same handle right after the write.
        Assert.True((ServiceRecoveryPolicyStore.ChangeAccessMask & ServiceQueryConfig) == ServiceQueryConfig,
            "ChangeAccessMask must include SERVICE_QUERY_CONFIG for the post-write readback verification.");
    }

    [Fact]
    public void NeitherAccessMask_EverRequestsServiceAllAccess()
    {
        Assert.NotEqual(ServiceAllAccess, ServiceRecoveryPolicyStore.QueryAccessMask);
        Assert.NotEqual(ServiceAllAccess, ServiceRecoveryPolicyStore.ChangeAccessMask);

        // Least privilege: neither mask should carry any bit outside SERVICE_QUERY_CONFIG |
        // SERVICE_CHANGE_CONFIG.
        const uint allowedBits = ServiceQueryConfig | ServiceChangeConfig;
        Assert.Equal(0u, ServiceRecoveryPolicyStore.QueryAccessMask & ~allowedBits);
        Assert.Equal(0u, ServiceRecoveryPolicyStore.ChangeAccessMask & ~allowedBits);
    }

    [Fact]
    public void ChangeAccessMask_IsStrictSupersetOfQueryAccessMask()
    {
        // The mutating path's handle must be at least as capable as the read-only path's, since it
        // performs a query (readback) in addition to the write.
        Assert.Equal(ServiceRecoveryPolicyStore.QueryAccessMask,
            ServiceRecoveryPolicyStore.ChangeAccessMask & ServiceRecoveryPolicyStore.QueryAccessMask);
    }
}
