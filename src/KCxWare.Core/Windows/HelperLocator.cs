namespace KCxWare.Core.Windows;

/// <summary>
/// Resolves the path to the elevated KCxWare.Helper.exe. The helper must always be launched from
/// beside whichever KCxWare.exe is currently running - the dev publish folder when testing a
/// published build, or the installed folder (e.g. C:\Program Files\KCxWare) once installed. This
/// type never hardcodes a repository, publish, or install path; callers must pass the running
/// application's own base directory (typically AppContext.BaseDirectory).
/// </summary>
public static class HelperLocator
{
    public const string HelperExecutableName = "KCxWare.Helper.exe";

    public static string ResolveHelperPath(string applicationBaseDirectory) =>
        Path.Combine(applicationBaseDirectory, HelperExecutableName);
}
