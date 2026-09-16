using System.Runtime.InteropServices;
using System.Linq;
using KCxWare.Core.Abstractions;
using KCxWare.Core.Models;

namespace KCxWare.Core.Windows;

/// <summary>
/// Typed access to a Windows service's SCM-level failure/recovery-action configuration via the
/// advapi32 QueryServiceConfig2/ChangeServiceConfig2 Win32 APIs - the same APIs
/// <c>sc.exe qfailure</c>/<c>sc.exe failure</c> wrap, used here directly instead of shelling out to
/// <c>sc.exe</c> and parsing its (locale-dependent) text output. Fails closed: every failure path
/// throws rather than returning a default/guessed configuration.
/// </summary>
public sealed class ServiceRecoveryPolicyStore : IServiceRecoveryPolicyStore
{
    private const uint ScManagerConnect = 0x0001;
    private const uint ServiceQueryConfig = 0x0001;
    private const uint ServiceChangeConfig = 0x0002;
    private const uint ServiceStart = 0x0010;
    private const uint ServiceConfigFailureActions = 2;
    private const uint ServiceConfigFailureActionsFlag = 4;
    private const int ErrorInsufficientBuffer = 122;

    /// <summary>
    /// Access mask used to open the service handle for the read-only query path
    /// (<see cref="GetFailureActionsAsync"/>). Requests only <c>SERVICE_QUERY_CONFIG</c> (0x0001) -
    /// least privilege, since this path never mutates the service configuration.
    /// Exposed as a named, testable constant (rather than an inline literal) so tests can assert
    /// the exact numeric value passed to <c>OpenServiceW</c> without needing to intercept the
    /// native P/Invoke call itself.
    /// </summary>
    public const uint QueryAccessMask = ServiceQueryConfig;

    /// <summary>
    /// Access mask used to open the service handle for the mutating path
    /// (<see cref="SetFailureActionsAsync"/>). Requests
    /// <c>SERVICE_CHANGE_CONFIG | SERVICE_QUERY_CONFIG | SERVICE_START</c> (0x0013) -
    /// <c>SERVICE_CHANGE_CONFIG</c> is required by <c>ChangeServiceConfig2W</c>,
    /// <c>SERVICE_QUERY_CONFIG</c> is required for the fail-closed readback verification
    /// (<c>QueryServiceConfig2W</c>) performed on the same handle immediately after the write, and
    /// <c>SERVICE_START</c> is required per MSDN (ChangeServiceConfig2W /
    /// SERVICE_CONFIG_FAILURE_ACTIONS): "If the service controller handles the SC_ACTION_RESTART
    /// action, hService must have the SERVICE_START access right." This handle is reused for every
    /// call this store makes to <c>ChangeServiceConfig2W</c> with SERVICE_CONFIG_FAILURE_ACTIONS -
    /// both the suppression write (0 actions, no SC_ACTION_RESTART) and the restoration write (which
    /// writes back the captured SC_ACTION_RESTART entries) - so the mask must cover the restoration
    /// case's requirement even though the suppression write's own payload does not literally contain
    /// SC_ACTION_RESTART.
    /// Deliberately not <c>SERVICE_ALL_ACCESS</c> (0xF01FF) - least privilege, nothing broader than
    /// this path actually needs.
    /// </summary>
    public const uint ChangeAccessMask = ServiceChangeConfig | ServiceQueryConfig | ServiceStart;

    public async Task<ServiceFailureActionsConfig> GetFailureActionsAsync(string serviceName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var scManager = OpenManager();
        using var service = OpenServiceHandle(scManager, serviceName, QueryAccessMask);

        var actions = ReadFailureActions(service, serviceName);
        var flag = ReadFailureActionsFlag(service, serviceName);
        await Task.CompletedTask;
        return actions with { ActionsOnNonCrashFailures = flag };
    }

    public async Task SetFailureActionsAsync(string serviceName, ServiceFailureActionsConfig config,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var scManager = OpenManager();
        using var service = OpenServiceHandle(scManager, serviceName, ChangeAccessMask);

        var effectiveActions = WriteFailureActions(service, serviceName, config);
        WriteFailureActionsFlag(service, serviceName, config.ActionsOnNonCrashFailures);

        // Fail closed: never trust that ChangeServiceConfig2W returning TRUE means the SCM actually
        // applied the requested configuration. Read the live config straight back and compare against
        // what was actually sent to ChangeServiceConfig2W (see WriteFailureActions - the suppression
        // case substitutes a single explicit SC_ACTION_NONE entry for an empty action list, because a
        // real SCM does not reliably clear a previously stored action array via cActions=0/
        // lpsaActions=NULL alone) before letting the caller proceed (e.g. before stopping the service).
        var readBack = ReadFailureActions(service, serviceName);
        if (readBack.Actions.Count != effectiveActions.Count ||
            !readBack.Actions.SequenceEqual(effectiveActions))
        {
            throw new InvalidOperationException(
                $"Failed to verify failure-action configuration for service {serviceName} after " +
                "ChangeServiceConfig2W reported success: the SCM's stored configuration does not match " +
                "what was requested.");
        }

        await Task.CompletedTask;
    }

    private static ServiceFailureActionsConfig ReadFailureActions(SafeScHandle service, string serviceName)
    {
        if (!NativeMethods.QueryServiceConfig2W(service.DangerousGetHandle(), ServiceConfigFailureActions,
                IntPtr.Zero, 0, out var needed) && Marshal.GetLastWin32Error() != ErrorInsufficientBuffer)
        {
            throw Failure($"query failure actions for service {serviceName}");
        }

        var buffer = Marshal.AllocHGlobal((int)needed);
        try
        {
            if (!NativeMethods.QueryServiceConfig2W(service.DangerousGetHandle(), ServiceConfigFailureActions,
                    buffer, needed, out _))
            {
                throw Failure($"query failure actions for service {serviceName}");
            }

            var raw = Marshal.PtrToStructure<NativeMethods.SERVICE_FAILURE_ACTIONS>(buffer);
            var actions = new List<ServiceFailureAction>((int)raw.cActions);
            var actionSize = Marshal.SizeOf<NativeMethods.SC_ACTION>();
            for (var index = 0; index < raw.cActions; index++)
            {
                var actionPtr = IntPtr.Add(raw.lpsaActions, index * actionSize);
                var action = Marshal.PtrToStructure<NativeMethods.SC_ACTION>(actionPtr);
                actions.Add(new ServiceFailureAction((ServiceFailureActionType)action.Type, action.Delay));
            }

            return new ServiceFailureActionsConfig(raw.dwResetPeriod, raw.lpRebootMsg, raw.lpCommand, actions);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static bool ReadFailureActionsFlag(SafeScHandle service, string serviceName)
    {
        if (!NativeMethods.QueryServiceConfig2W(service.DangerousGetHandle(), ServiceConfigFailureActionsFlag,
                IntPtr.Zero, 0, out var needed) && Marshal.GetLastWin32Error() != ErrorInsufficientBuffer)
        {
            throw Failure($"query failure actions flag for service {serviceName}");
        }

        // SERVICE_FAILURE_ACTIONS_FLAG is a fixed-size struct containing a single 4-byte BOOL. Unlike
        // the variable-length SERVICE_FAILURE_ACTIONS query above (where the SCM-reported
        // pcbBytesNeeded is always far larger than any degenerate value could be, so this never
        // matters there), a probe call for this small fixed struct can report a bytesNeeded smaller
        // than sizeof(SERVICE_FAILURE_ACTIONS_FLAG) - trusting that value verbatim would allocate an
        // undersized buffer and/or pass an undersized bufferSize to the real call, and
        // Marshal.PtrToStructure would then read past/under the actual native write, silently
        // producing a wrong boolean (observed in practice as TRUE being read back as FALSE) instead of
        // failing loudly. Floor the buffer size at the struct's real managed/native size so the
        // allocation and the second QueryServiceConfig2W call are always big enough to hold what
        // Marshal.PtrToStructure is about to read.
        var flagStructSize = (uint)Marshal.SizeOf<NativeMethods.SERVICE_FAILURE_ACTIONS_FLAG>();
        var bufferSize = Math.Max(needed, flagStructSize);

        var buffer = Marshal.AllocHGlobal((int)bufferSize);
        try
        {
            if (!NativeMethods.QueryServiceConfig2W(service.DangerousGetHandle(), ServiceConfigFailureActionsFlag,
                    buffer, bufferSize, out _))
            {
                throw Failure($"query failure actions flag for service {serviceName}");
            }

            return ParseFailureActionsFlag(buffer);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>
    /// Parses a native <c>SERVICE_FAILURE_ACTIONS_FLAG</c> buffer already populated by
    /// <c>QueryServiceConfig2W</c> (or, in tests, by <c>Marshal.StructureToPtr</c> simulating that
    /// call). Extracted from <see cref="ReadFailureActionsFlag"/> so the parsing logic itself -
    /// independent of the live P/Invoke call and buffer-sizing dance above it - is directly testable
    /// against a real unmanaged buffer.
    /// </summary>
    public static bool ParseFailureActionsFlag(IntPtr buffer) =>
        Marshal.PtrToStructure<NativeMethods.SERVICE_FAILURE_ACTIONS_FLAG>(buffer).fFailureActionsOnNonCrashFailures;

    /// <summary>
    /// Builds the exact list of <see cref="ServiceFailureAction"/> entries that
    /// <see cref="WriteFailureActions"/> marshals into the native <c>SERVICE_FAILURE_ACTIONS</c>
    /// payload for a given requested <paramref name="config"/>.
    /// <para>
    /// For a non-empty requested action list (the restoration path), this is exactly
    /// <c>config.Actions</c> - unchanged from prior behavior.
    /// </para>
    /// <para>
    /// For an empty requested action list (the suppression path, e.g. <see cref="ServiceFailureActionsConfig.NoRecovery"/>),
    /// this is a single explicit <c>SC_ACTION_NONE</c>/0ms entry rather than zero entries. This is a
    /// documented real-world Win32 SCM quirk: passing <c>cActions=0</c> with <c>lpsaActions=NULL</c>
    /// to <c>ChangeServiceConfig2W</c>/<c>SERVICE_CONFIG_FAILURE_ACTIONS</c> is accepted (the call
    /// reports success) but does not reliably overwrite a previously stored non-empty action array on
    /// a real SCM - the old array is left in place. The commonly used workaround (matching what
    /// <c>sc.exe failure</c> and other production tooling do to reliably clear recovery actions) is to
    /// write a single explicit "do nothing" action (<c>SC_ACTION_NONE</c>, delay 0) instead of an
    /// empty array, which the SCM does honor as a real replacement of the stored array.
    /// </para>
    /// </summary>
    public static IReadOnlyList<ServiceFailureAction> BuildEffectiveActions(ServiceFailureActionsConfig config) =>
        config.Actions.Count == 0
            ? [new ServiceFailureAction(ServiceFailureActionType.None, 0)]
            : config.Actions;

    private static IReadOnlyList<ServiceFailureAction> WriteFailureActions(SafeScHandle service,
        string serviceName, ServiceFailureActionsConfig config)
    {
        var effectiveActions = BuildEffectiveActions(config);
        var actionSize = Marshal.SizeOf<NativeMethods.SC_ACTION>();
        // effectiveActions is never empty (see BuildEffectiveActions), so lpsaActions is always a
        // real, non-null pointer to at least one SC_ACTION entry - including for the suppression case,
        // where it points at a single explicit SC_ACTION_NONE entry rather than being NULL. See
        // BuildEffectiveActions for why a bare cActions=0/lpsaActions=NULL clear is not used here.
        var actionsBuffer = Marshal.AllocHGlobal(actionSize * effectiveActions.Count);
        var infoBuffer = IntPtr.Zero;
        try
        {
            for (var index = 0; index < effectiveActions.Count; index++)
            {
                var action = effectiveActions[index];
                var native = new NativeMethods.SC_ACTION { Type = (int)action.Type, Delay = action.DelayMs };
                Marshal.StructureToPtr(native, IntPtr.Add(actionsBuffer, index * actionSize), false);
            }

            var info = new NativeMethods.SERVICE_FAILURE_ACTIONS
            {
                dwResetPeriod = config.Actions.Count == 0 ? 0 : config.ResetPeriodSeconds,
                lpRebootMsg = config.Actions.Count == 0 ? null : config.RebootMessage,
                lpCommand = config.Actions.Count == 0 ? null : config.Command,
                cActions = (uint)effectiveActions.Count,
                lpsaActions = actionsBuffer
            };
            infoBuffer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMethods.SERVICE_FAILURE_ACTIONS>());
            Marshal.StructureToPtr(info, infoBuffer, false);

            if (!NativeMethods.ChangeServiceConfig2W(service.DangerousGetHandle(), ServiceConfigFailureActions,
                    infoBuffer))
            {
                throw Failure($"set failure actions for service {serviceName}");
            }

            return effectiveActions;
        }
        finally
        {
            if (infoBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(infoBuffer);
            }

            if (actionsBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(actionsBuffer);
            }
        }
    }

    private static void WriteFailureActionsFlag(SafeScHandle service, string serviceName, bool onNonCrashFailures)
    {
        var flag = new NativeMethods.SERVICE_FAILURE_ACTIONS_FLAG { fFailureActionsOnNonCrashFailures = onNonCrashFailures };
        var buffer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMethods.SERVICE_FAILURE_ACTIONS_FLAG>());
        try
        {
            Marshal.StructureToPtr(flag, buffer, false);
            if (!NativeMethods.ChangeServiceConfig2W(service.DangerousGetHandle(), ServiceConfigFailureActionsFlag,
                    buffer))
            {
                throw Failure($"set failure actions flag for service {serviceName}");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static SafeScHandle OpenManager()
    {
        var handle = NativeMethods.OpenSCManagerW(null, null, ScManagerConnect);
        if (handle == IntPtr.Zero)
        {
            throw Failure("open the Service Control Manager");
        }

        return new SafeScHandle(handle);
    }

    private static SafeScHandle OpenServiceHandle(SafeScHandle scManager, string serviceName, uint access)
    {
        var handle = NativeMethods.OpenServiceW(scManager.DangerousGetHandle(), serviceName, access);
        if (handle == IntPtr.Zero)
        {
            throw Failure($"open service {serviceName}");
        }

        return new SafeScHandle(handle);
    }

    private static InvalidOperationException Failure(string operation) =>
        new($"Failed to {operation} (Win32 error {Marshal.GetLastWin32Error()}).");

    private sealed class SafeScHandle : SafeHandle
    {
        public SafeScHandle(IntPtr handle) : base(IntPtr.Zero, true) => SetHandle(handle);

        public override bool IsInvalid => handle == IntPtr.Zero;

        protected override bool ReleaseHandle() => NativeMethods.CloseServiceHandle(handle);
    }

    private static class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct SC_ACTION
        {
            public int Type;
            public uint Delay;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct SERVICE_FAILURE_ACTIONS
        {
            public uint dwResetPeriod;
            public string? lpRebootMsg;
            public string? lpCommand;
            public uint cActions;
            public IntPtr lpsaActions;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SERVICE_FAILURE_ACTIONS_FLAG
        {
            [MarshalAs(UnmanagedType.Bool)]
            public bool fFailureActionsOnNonCrashFailures;
        }

        [DllImport("advapi32.dll", EntryPoint = "OpenSCManagerW", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr OpenSCManagerW(string? machineName, string? databaseName, uint desiredAccess);

        [DllImport("advapi32.dll", EntryPoint = "OpenServiceW", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr OpenServiceW(IntPtr scManager, string serviceName, uint desiredAccess);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseServiceHandle(IntPtr scObject);

        [DllImport("advapi32.dll", EntryPoint = "QueryServiceConfig2W", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool QueryServiceConfig2W(IntPtr service, uint infoLevel, IntPtr buffer,
            uint bufferSize, out uint bytesNeeded);

        [DllImport("advapi32.dll", EntryPoint = "ChangeServiceConfig2W", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ChangeServiceConfig2W(IntPtr service, uint infoLevel, IntPtr info);
    }
}
