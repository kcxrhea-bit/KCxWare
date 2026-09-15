namespace KCxWare.Core.Policies;

public static class ModePolicy
{
    public const string GamingPowerPlan = "9935e61f-1661-40c5-ae2f-8495027d5d5d";
    public const string AmdBalancedPowerPlan = "9897998c-92de-4669-853f-b7cd3ecb2790";
    public const string WindowsBalancedPowerPlan = "381b4222-f694-41f0-9685-ff5bb260df2e";
    public const string TransitionTaskName = "KCxWare Apply Armed Mode";

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
        "CoworkVMService", "RustDesk", "WSearch", "DoSvc", "MacriumService", "SaladBowl", "FvSvc"
    ];

    public static IReadOnlyList<string> GamingSuppressibleProcesses { get; } =
    [
        "Docker Desktop", "ollama app", "LM Studio", "OneDrive", "OneDrive.Sync.Service", "GoogleDriveFS",
        "PhoneExperienceHost", "CrossDeviceService", "CrossDeviceResume", "ms-teams", "Copilot",
        "SignalRgb", "SignalRgbService", "OpenRGB", "Widgets", "WidgetService", "rustdesk",
        "Salad.Bootstrapper", "Salad.Bowl.Service", "NVIDIA Share", "NVIDIA Overlay", "PresentMon",
        "PresentMon_x64", "FrameView"
    ];

    public static void AssertSafe()
    {
        var overlap = GamingSuppressibleServices.Where(ProtectedServices.Contains).ToArray();
        if (overlap.Length != 0)
        {
            throw new InvalidOperationException($"Protected services appear in the suppressible policy: {string.Join(", ", overlap)}");
        }
    }
}
