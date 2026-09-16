namespace KCxWare.Core.Loading;

/// <summary>
/// Resolves the KCx loading-overlay parade video path relative to the running application's own
/// base directory. Never resolves against the repository root, current working directory, or any
/// hardcoded development/publish path - the file travels with whichever KCxWare.exe is running.
/// </summary>
public static class LoadingAssetLocator
{
    public const string VideoRelativePath = "Assets\\kcxparade.mp4";

    public static string ResolveVideoPath(string applicationBaseDirectory) =>
        Path.Combine(applicationBaseDirectory, "Assets", "kcxparade.mp4");
}
