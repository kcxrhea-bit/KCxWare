using System.Runtime.InteropServices;
using KCxWare.Core.Windows;

namespace KCxWare.Tests;

/// <summary>
/// Regression coverage for a real-machine failure observed in production: before Gaming mode ever
/// ran, real <c>sc.exe qfailureflag WSearch</c> reported <c>FAILURE_ACTIONS_ON_NONCRASH_FAILURES:
/// TRUE</c>, but KCxWare's captured baseline recorded <c>ActionsOnNonCrashFailures = false</c> - the
/// SCM-reported TRUE flag was silently read back as FALSE during capture, with no exception raised
/// anywhere in the pipeline.
///
/// Root cause: <see cref="ServiceRecoveryPolicyStore"/>'s private <c>ReadFailureActionsFlag</c> used
/// the SCM-reported <c>pcbBytesNeeded</c> from the probe call verbatim to size the buffer for the real
/// <c>QueryServiceConfig2W</c> call. For the variable-length <c>SERVICE_FAILURE_ACTIONS</c> query this
/// value is always far larger than <c>SERVICE_FAILURE_ACTIONS_FLAG</c>'s fixed 4-byte size, so it never
/// mattered there - but for this fixed, single-BOOL struct, a probe-reported size smaller than the
/// struct's real size let <c>Marshal.PtrToStructure</c> read a buffer that was never fully populated by
/// the second call, silently yielding <c>false</c> instead of throwing. The fix floors the buffer size
/// (and the size passed to the second <c>QueryServiceConfig2W</c> call) at
/// <c>Marshal.SizeOf&lt;SERVICE_FAILURE_ACTIONS_FLAG&gt;()</c>.
///
/// These tests exercise <see cref="ServiceRecoveryPolicyStore.ParseFailureActionsFlag"/> - the parsing
/// step extracted out of the live P/Invoke call specifically so it is testable against a real
/// unmanaged buffer, mirroring how <c>QueryAccessMask</c>/<c>ChangeAccessMask</c> were exposed as
/// testable constants in the access-mask investigation.
/// </summary>
public sealed class ServiceRecoveryPolicyStoreFailureActionsFlagTests
{
    [Fact]
    public void ParseFailureActionsFlag_NonZeroNativeBool_ReturnsTrue()
    {
        // A native SERVICE_FAILURE_ACTIONS_FLAG's single field is a 4-byte Win32 BOOL; TRUE is
        // conventionally the literal value 1, written directly as the raw bytes QueryServiceConfig2W
        // would have produced.
        var buffer = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            Marshal.WriteInt32(buffer, 1);

            var result = ServiceRecoveryPolicyStore.ParseFailureActionsFlag(buffer);

            Assert.True(result);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [Fact]
    public void ParseFailureActionsFlag_ZeroNativeBool_ReturnsFalse()
    {
        var buffer = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            Marshal.WriteInt32(buffer, 0);

            var result = ServiceRecoveryPolicyStore.ParseFailureActionsFlag(buffer);

            Assert.False(result);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
