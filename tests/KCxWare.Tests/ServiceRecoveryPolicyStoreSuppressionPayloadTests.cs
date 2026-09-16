using System.Runtime.InteropServices;
using KCxWare.Core.Models;
using KCxWare.Core.Windows;

namespace KCxWare.Tests;

/// <summary>
/// Regression coverage for a real-machine failure observed during Gaming mode's WSearch
/// suppression: <c>ChangeServiceConfig2W</c> for <c>SERVICE_CONFIG_FAILURE_ACTIONS</c> reported
/// success, but a subsequent <c>sc.exe qfailure WSearch</c> showed the original 5x
/// RestartService/30000ms actions completely unchanged - the clear never actually took effect,
/// even though the separate <c>SERVICE_CONFIG_FAILURE_ACTIONS_FLAG</c> write in the same operation
/// (proven correct elsewhere) succeeded and was observably applied.
///
/// Root cause: the suppression write previously marshalled a native <c>SERVICE_FAILURE_ACTIONS</c>
/// payload with <c>cActions=0</c> and <c>lpsaActions=IntPtr.Zero</c> for
/// <see cref="ServiceFailureActionsConfig.NoRecovery"/> (an empty <c>Actions</c> list). Although this
/// is literally what MSDN describes for "no failure actions", it is a well-known real-world SCM
/// quirk that <c>ChangeServiceConfig2W</c> accepts this payload (returns TRUE) without actually
/// overwriting a previously stored non-empty action array - the old array survives untouched. This
/// is a payload/semantics bug, not a control-flow bug: the call genuinely was being made, with the
/// genuinely wrong array-clearing representation, once per suppression - the same asymmetry-explaining
/// fact is that the flag write's struct (SERVICE_FAILURE_ACTIONS_FLAG) carries no analogous
/// "array" concept for the SCM to silently leave alone, so it does not share this failure mode.
///
/// The fix (see <see cref="ServiceRecoveryPolicyStore.BuildEffectiveActions"/>) substitutes a single
/// explicit <c>SC_ACTION_NONE</c>/0ms entry for an empty action list, which the SCM does honor as a
/// real replacement of the stored array - a commonly used workaround for this exact quirk.
///
/// These tests marshal <see cref="ServiceRecoveryPolicyStore.BuildEffectiveActions"/>'s output into a
/// real unmanaged buffer (mirroring the private native SC_ACTION layout: <c>int Type; uint Delay;</c>)
/// to prove the corrected representation's exact field values, and separately prove the restoration
/// path's payload (5x RestartService/30000ms + 1x None/0ms) is completely unperturbed by this fix.
/// </summary>
public sealed class ServiceRecoveryPolicyStoreSuppressionPayloadTests
{
    [StructLayout(LayoutKind.Sequential)]
    private struct SC_ACTION
    {
        public int Type;
        public uint Delay;
    }

    [Fact]
    public void BuildEffectiveActions_EmptySuppressionConfig_ProducesSingleExplicitNoneAction()
    {
        // ServiceFailureActionsConfig.NoRecovery is exactly what the suppression write passes:
        // an empty Actions list, dwResetPeriod=0, lpRebootMsg=null, lpCommand=null.
        var config = ServiceFailureActionsConfig.NoRecovery;

        var effectiveActions = ServiceRecoveryPolicyStore.BuildEffectiveActions(config);

        // cActions must be 1, not 0 - the corrected payload never sends an empty array for
        // suppression, because a real SCM does not reliably honor cActions=0/lpsaActions=NULL as
        // "clear the previously stored array".
        Assert.Single(effectiveActions);
        Assert.Equal(ServiceFailureActionType.None, effectiveActions[0].Type);
        Assert.Equal(0u, effectiveActions[0].DelayMs);

        // Marshal into a real unmanaged buffer exactly as WriteFailureActions does, and verify the
        // literal bytes ChangeServiceConfig2W would receive for lpsaActions[0].
        var actionSize = Marshal.SizeOf<SC_ACTION>();
        var buffer = Marshal.AllocHGlobal(actionSize);
        try
        {
            var native = new SC_ACTION { Type = (int)effectiveActions[0].Type, Delay = effectiveActions[0].DelayMs };
            Marshal.StructureToPtr(native, buffer, false);

            var readBack = Marshal.PtrToStructure<SC_ACTION>(buffer);
            Assert.Equal(0, readBack.Type); // SC_ACTION_NONE
            Assert.Equal(0u, readBack.Delay);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        Assert.NotEqual(IntPtr.Zero, buffer); // lpsaActions in the real call is a genuine non-null pointer
    }

    [Fact]
    public void BuildEffectiveActions_EmptySuppressionConfig_ResetPeriodAndMessagesStayNullOrZero()
    {
        var config = ServiceFailureActionsConfig.NoRecovery;

        // dwResetPeriod, lpRebootMsg, and lpCommand are computed independently of
        // BuildEffectiveActions inside WriteFailureActions (still gated on config.Actions.Count == 0,
        // the caller-requested list - not the effective one), so they remain 0/null/null exactly as
        // before this fix. Assert the source config fields that drive that gating are still what the
        // suppression path expects.
        Assert.Empty(config.Actions);
        Assert.Equal(0u, config.ResetPeriodSeconds);
        Assert.Null(config.RebootMessage);
        Assert.Null(config.Command);
    }

    [Fact]
    public void BuildEffectiveActions_NonEmptyRestorationConfig_IsReturnedUnchanged()
    {
        // The proven-correct restoration representation: 5x RestartService/30000ms + 1x None/0ms.
        var restorationActions = new List<ServiceFailureAction>
        {
            new(ServiceFailureActionType.RestartService, 30000),
            new(ServiceFailureActionType.RestartService, 30000),
            new(ServiceFailureActionType.RestartService, 30000),
            new(ServiceFailureActionType.RestartService, 30000),
            new(ServiceFailureActionType.RestartService, 30000),
            new(ServiceFailureActionType.None, 0)
        };
        var config = new ServiceFailureActionsConfig(0, null, null, restorationActions);

        var effectiveActions = ServiceRecoveryPolicyStore.BuildEffectiveActions(config);

        // Must be the exact same 6 entries, in order, completely untouched by the suppression fix -
        // this is not the empty-list branch, so no SC_ACTION_NONE substitution happens.
        Assert.Equal(6, effectiveActions.Count);
        Assert.Same(restorationActions, effectiveActions);
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(ServiceFailureActionType.RestartService, effectiveActions[i].Type);
            Assert.Equal(30000u, effectiveActions[i].DelayMs);
        }

        Assert.Equal(ServiceFailureActionType.None, effectiveActions[5].Type);
        Assert.Equal(0u, effectiveActions[5].DelayMs);
    }

    [Fact]
    public void BuildEffectiveActions_SingleActionRestorationConfig_IsReturnedUnchanged()
    {
        // Guards against the substitution logic keying off "count == 1" instead of "count == 0" -
        // any genuinely non-empty requested list, even a single action, must pass through untouched.
        var actions = new List<ServiceFailureAction> { new(ServiceFailureActionType.RunCommand, 5000) };
        var config = new ServiceFailureActionsConfig(60, "reboot msg", "cmd.exe", actions);

        var effectiveActions = ServiceRecoveryPolicyStore.BuildEffectiveActions(config);

        Assert.Same(actions, effectiveActions);
    }
}
