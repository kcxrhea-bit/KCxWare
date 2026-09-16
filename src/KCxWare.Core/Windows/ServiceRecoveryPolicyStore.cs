using System.Runtime.InteropServices;
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
    private const int ErrorInsufficientBuffer = 122;

    public Task<ServiceFailureActionsConfig> GetFailureActionsAsync(string serviceName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var scManager = OpenManager();
        using var service = OpenServiceHandle(scManager, serviceName, ServiceQueryConfig);

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

            return Task.FromResult(new ServiceFailureActionsConfig(raw.dwResetPeriod, raw.lpRebootMsg,
                raw.lpCommand, actions));
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public Task SetFailureActionsAsync(string serviceName, ServiceFailureActionsConfig config,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var scManager = OpenManager();
        using var service = OpenServiceHandle(scManager, serviceName, ServiceChangeConfig);

        var actionSize = Marshal.SizeOf<NativeMethods.SC_ACTION>();
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
                dwResetPeriod = config.ResetPeriodSeconds,
                lpRebootMsg = config.RebootMessage,
                lpCommand = config.Command,
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

            return Task.CompletedTask;
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
