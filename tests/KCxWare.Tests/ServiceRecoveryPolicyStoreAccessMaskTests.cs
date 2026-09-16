using KCxWare.Core.Windows;

namespace KCxWare.Tests;

/// <summary>
/// Regression coverage for a real-machine failure observed in production: <c>ChangeServiceConfig2W</c>
/// returning <c>ERROR_ACCESS_DENIED</c> (Win32 error 5) when <c>ServiceRecoveryPolicyStore</c> attempted
/// to restore/suppress WSearch's failure actions.
///
/// A first investigation (see the now-superseded doc comment this replaced) concluded the mask was
/// already correct at <c>SERVICE_CHANGE_CONFIG | SERVICE_QUERY_CONFIG</c> (0x0003), since that covers
/// what <c>ChangeServiceConfig2W</c> itself and the post-write readback need. That investigation missed
/// a separate, narrower Win32 requirement documented for <c>ChangeServiceConfig2</c> /
/// <c>SERVICE_CONFIG_FAILURE_ACTIONS</c>: "If the service controller handles the SC_ACTION_RESTART
/// action, hService must have the SERVICE_START access right." WSearch's real captured/restored
/// configuration is 5x SC_ACTION_RESTART entries, and the restoration write puts that exact
/// configuration back - so the handle used for that write (the same handle/mask used for every
/// <c>SetFailureActionsAsync</c> call, suppression included) needs <c>SERVICE_START</c> (0x0010) as
/// well. 0x0003 does not carry that bit, so restoration was always missing the one right SCM actually
/// enforces for a payload containing SC_ACTION_RESTART - a distinct root cause from the one the first
/// investigation ruled out.
///
/// No P/Invoke-level fake exists to intercept the literal value passed to <c>OpenServiceW</c>, so these
/// tests instead pin the named, testable constants the production code paths actually use -
/// <see cref="ServiceRecoveryPolicyStore.QueryAccessMask"/> and
/// <see cref="ServiceRecoveryPolicyStore.ChangeAccessMask"/> - to their expected numeric values. If a
/// future edit narrows <c>ChangeAccessMask</c> back down (e.g. dropping <c>SERVICE_CHANGE_CONFIG</c>,
/// <c>SERVICE_QUERY_CONFIG</c>, or <c>SERVICE_START</c>), or broadens either mask to
/// <c>SERVICE_ALL_ACCESS</c>, this fails immediately.
/// </summary>
public sealed class ServiceRecoveryPolicyStoreAccessMaskTests
{
    private const uint ServiceQueryConfig = 0x0001;
    private const uint ServiceChangeConfig = 0x0002;
    private const uint ServiceStart = 0x0010;
    private const uint ServiceAllAccess = 0xF01FF;

    [Fact]
    public void QueryAccessMask_RequestsOnlyQueryConfig()
    {
        Assert.Equal(ServiceQueryConfig, ServiceRecoveryPolicyStore.QueryAccessMask);
    }

    [Fact]
    public void ChangeAccessMask_RequestsChangeConfigAndQueryConfigAndStart()
    {
        Assert.Equal(ServiceChangeConfig | ServiceQueryConfig | ServiceStart,
            ServiceRecoveryPolicyStore.ChangeAccessMask);

        // SERVICE_CHANGE_CONFIG is the specific right ChangeServiceConfig2W requires; this is the bit
        // whose absence would reproduce the real-machine ERROR_ACCESS_DENIED (Win32 error 5).
        Assert.True((ServiceRecoveryPolicyStore.ChangeAccessMask & ServiceChangeConfig) == ServiceChangeConfig,
            "ChangeAccessMask must include SERVICE_CHANGE_CONFIG or ChangeServiceConfig2W will fail with " +
            "ERROR_ACCESS_DENIED.");

        // SERVICE_QUERY_CONFIG is required for the fail-closed readback verification performed on the
        // same handle right after the write.
        Assert.True((ServiceRecoveryPolicyStore.ChangeAccessMask & ServiceQueryConfig) == ServiceQueryConfig,
            "ChangeAccessMask must include SERVICE_QUERY_CONFIG for the post-write readback verification.");

        // SERVICE_START is required per MSDN because the restoration write puts back WSearch's real
        // 5x SC_ACTION_RESTART configuration; per the documented ChangeServiceConfig2 contract, a
        // handle used to write SC_ACTION_RESTART entries must carry SERVICE_START.
        Assert.True((ServiceRecoveryPolicyStore.ChangeAccessMask & ServiceStart) == ServiceStart,
            "ChangeAccessMask must include SERVICE_START: the SCM requires it on the handle whenever " +
            "ChangeServiceConfig2W writes a SERVICE_CONFIG_FAILURE_ACTIONS payload containing " +
            "SC_ACTION_RESTART entries, which the restoration path always does.");
    }

    [Fact]
    public void NeitherAccessMask_EverRequestsServiceAllAccess()
    {
        Assert.NotEqual(ServiceAllAccess, ServiceRecoveryPolicyStore.QueryAccessMask);
        Assert.NotEqual(ServiceAllAccess, ServiceRecoveryPolicyStore.ChangeAccessMask);

        // Least privilege: neither mask should carry any bit outside SERVICE_QUERY_CONFIG |
        // SERVICE_CHANGE_CONFIG | SERVICE_START.
        const uint allowedBits = ServiceQueryConfig | ServiceChangeConfig | ServiceStart;
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
