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
    private const uint ServiceConfigFailureActions = 2;
    private const uint ServiceConfigFailureActionsFlag = 4;
    private const int ErrorInsufficientBuffer = 122;

    public async Task<ServiceFailureActionsConfig> GetFailureActionsAsync(string serviceName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var scManager = OpenManager();
        using var service = OpenServiceHandle(scManager, serviceName, ServiceQueryConfig);

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
        using var service = OpenServiceHandle(scManager, serviceName, ServiceChangeConfig | ServiceQueryConfig);

        WriteFailureActions(service, serviceName, config);
        WriteFailureActionsFlag(service, serviceName, config.ActionsOnNonCrashFailures);

        // Fail closed: never trust that ChangeServiceConfig2W returning TRUE means the SCM actually
        // applied the requested configuration. Read the live config straight back and compare against
        // what was requested before letting the caller proceed (e.g. before stopping the service).
        var readBack = ReadFailureActions(service, serviceName);
        if (readBack.Actions.Count != config.Actions.Count ||
            !readBack.Actions.SequenceEqual(config.Actions))
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

        var buffer = Marshal.AllocHGlobal((int)needed);
        try
        {
            if (!NativeMethods.QueryServiceConfig2W(service.DangerousGetHandle(), ServiceConfigFailureActionsFlag,
                    buffer, needed, out _))
            {
                throw Failure($"query failure actions flag for service {serviceName}");
            }

            var raw = Marshal.PtrToStructure<NativeMethods.SERVICE_FAILURE_ACTIONS_FLAG>(buffer);
            return raw.fFailureActionsOnNonCrashFailures;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static void WriteFailureActions(SafeScHandle service, string serviceName,
        ServiceFailureActionsConfig config)
    {
        var actionSize = Marshal.SizeOf<NativeMethods.SC_ACTION>();
        // Per MSDN (ChangeServiceConfig2W / SERVICE_FAILURE_ACTIONS): to clear/disable recovery
        // actions, cActions must be 0 and lpsaActions must be NULL - not a non-null pointer to a
        // zero-length or "no-op" array. A managed zero-length array still marshals to a non-null
        // pointer, so this must stay an explicit IntPtr.Zero for the suppression case.
        var actionsBuffer = config.Actions.Count == 0
            ? IntPtr.Zero
            : Marshal.AllocHGlobal(actionSize * config.Actions.Count);
        var infoBuffer = IntPtr.Zero;
        try
        {
            for (var index = 0; index < config.Actions.Count; index++)
            {
                var action = config.Actions[index];
                var native = new NativeMethods.SC_ACTION { Type = (int)action.Type, Delay = action.DelayMs };
                Marshal.StructureToPtr(native, IntPtr.Add(actionsBuffer, index * actionSize), false);
            }

            var info = new NativeMethods.SERVICE_FAILURE_ACTIONS
            {
                dwResetPeriod = config.Actions.Count == 0 ? 0 : config.ResetPeriodSeconds,
                lpRebootMsg = config.Actions.Count == 0 ? null : config.RebootMessage,
                lpCommand = config.Actions.Count == 0 ? null : config.Command,
                cActions = (uint)config.Actions.Count,
                lpsaActions = actionsBuffer
            };
            infoBuffer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMethods.SERVICE_FAILURE_ACTIONS>());
            Marshal.StructureToPtr(info, infoBuffer, false);

            if (!NativeMethods.ChangeServiceConfig2W(service.DangerousGetHandle(), ServiceConfigFailureActions,
                    infoBuffer))
            {
                throw Failure($"set failure actions for service {serviceName}");
            }
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
