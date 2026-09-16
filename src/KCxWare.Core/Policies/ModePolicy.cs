namespace KCxWare.Core.Policies;

public static class ModePolicy
{
    public const string GamingPowerPlan = "9935e61f-1661-40c5-ae2f-8495027d5d5d";
    public const string AmdBalancedPowerPlan = "9897998c-92de-4669-853f-b7cd3ecb2790";
    public const string WindowsBalancedPowerPlan = "381b4222-f694-41f0-9685-ff5bb260df2e";
    public const string TransitionTaskName = "KCxWare Apply Armed Mode";
    public static readonly TimeSpan GamingSettleDelay = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan GamingVerificationRetryDelay = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan GamingCleanVerificationDelay = TimeSpan.FromSeconds(35);
    public static readonly TimeSpan ServiceStopPollDelay = TimeSpan.FromMilliseconds(250);
    public static readonly TimeSpan ServiceStopTimeout = TimeSpan.FromSeconds(20);
    public const int GamingCleanupAttempts = 3;

    public static IReadOnlySet<string> ProtectedServices { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "RpcSs", "DcomLaunch", "RpcEptMapper", "WinDefend", "WdNisSvc", "mpssvc",
        "BFE", "Dhcp", "Dnscache", "NlaSvc", "WlanSvc", "AudioSrv", "AudioEndpointBuilder",
        "NVDisplay.ContainerLocalSystem", "NvContainerLocalSystem", "LGHUBUpdaterService",
        "logi_lamparray_service", "WavesTBSvc", "GameInputSvc", "GameInputRedistService",
        "GamingServices", "GamingServicesNet", "EasyAntiCheat", "EasyAntiCheat_EOS", "BEService"
    };

    public static IReadOnlyList<string> GamingSuppressibleServices { get; } =
    [
        "com.docker.service", "WslService", "LxssManager", "vmcompute", "OllamaService",
        "CoworkVMService", "RustDesk", "WSearch", "DoSvc", "MacriumService", "SaladBowl", "FvSvc",
        "SignalRgb.Service", "WinFsp.Launcher", "SamsungUpdateService"
    ];

    public static IReadOnlyList<string> GamingSuppressibleProcesses { get; } =
    [
        "Kudu", "Docker Desktop", "com.docker.backend", "com.docker.build", "ollama", "ollama app", "LM Studio",
        "OneDrive", "OneDrive.Sync.Service", "GoogleDriveFS",
        "ms-teams", "Teams", "Copilot",
        "SignalRgb", "SignalRgbService", "SignalRgbLauncher", "OpenRGB", "Widgets", "rustdesk",
        "Salad", "Salad.Bootstrapper", "Salad.Bowl.Service", "ReflectUI", "MacriumReflect",
        "SnippingTool"
    ];

    public static IReadOnlyList<string> GamingBestEffortProcesses { get; } =
    [
        "PhoneExperienceHost", "CrossDeviceService", "CrossDeviceResume"
    ];

    /// <summary>
    /// Services in <see cref="GamingSuppressibleServices"/> whose SCM failure/recovery-action
    /// configuration KCxWare must temporarily suppress before stopping them, because Windows
    /// Service Control Manager otherwise auto-restarts them mid-cleanup-verification regardless of
    /// how cleanly KCxWare stopped the service. WSearch ships with 5x RESTART recovery actions.
    /// </summary>
    public static IReadOnlyList<string> RecoverySuppressedServices { get; } = ["WSearch"];

    public static IReadOnlyList<string> GamingSuppressibleBackgroundProcesses { get; } = ["msedge"];
    public static IReadOnlyList<string> GamingShutdownVerifiedProcesses { get; } = ["vmmemWSL"];

    public static IReadOnlySet<string> ProtectedProcesses { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "MsMpEng", "NisSrv", "explorer", "ShellExperienceHost", "StartMenuExperienceHost", "dwm",
        "svchost", "audiodg", "WavesSvc64", "NVDisplay.Container", "nvcontainer", "lghub", "lghub_agent",
        "lghub_updater", "GamingServices", "GamingServicesNet", "GameInputSvc", "EpicGamesLauncher",
        "FortniteClient-Win64-Shipping", "FortniteClient-Win64-Shipping_BE",
        "FortniteClient-Win64-Shipping_EAC", "EasyAntiCheat", "EasyAntiCheat_EOS", "BEService",
        "chrome", "chrome-native-host", "NVIDIA Share", "NVIDIA Overlay", "PresentMon", "PresentMon_x64",
        "PresentMonService", "nvsphelper64", "nvrla", "nvfvsdksvc_x64", "FrameView"
    };

    public static void AssertSafe()
    {
        var overlap = GamingSuppressibleServices.Where(ProtectedServices.Contains).ToArray();
        if (overlap.Length != 0)
        {
            throw new InvalidOperationException($"Protected services appear in the suppressible policy: {string.Join(", ", overlap)}");
        }

        var recoveryOverlap = RecoverySuppressedServices.Where(ProtectedServices.Contains).ToArray();
        if (recoveryOverlap.Length != 0)
        {
            throw new InvalidOperationException($"Protected services appear in the recovery-suppression policy: {string.Join(", ", recoveryOverlap)}");
        }

        var recoveryNotSuppressible = RecoverySuppressedServices.Where(service => !GamingSuppressibleServices.Contains(service)).ToArray();
        if (recoveryNotSuppressible.Length != 0)
        {
            throw new InvalidOperationException($"Recovery-suppression policy references services outside the suppressible policy: {string.Join(", ", recoveryNotSuppressible)}");
        }

        var processOverlap = GamingSuppressibleProcesses
            .Concat(GamingBestEffortProcesses)
            .Concat(GamingSuppressibleBackgroundProcesses)
            .Concat(GamingShutdownVerifiedProcesses)
            .Where(ProtectedProcesses.Contains)
            .ToArray();
        if (processOverlap.Length != 0)
        {
            throw new InvalidOperationException($"Protected processes appear in the suppressible policy: {string.Join(", ", processOverlap)}");
        }
    }
}
