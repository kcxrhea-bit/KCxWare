using System.Reflection;

namespace KCxWare.Tests;

/// <summary>
/// Guards the production composition roots (KCxWare.Helper and the tray app's MainViewModel) so the
/// real Win32-backed <c>ServiceRecoveryPolicyStore</c> is always explicitly wired into
/// <c>WindowsSystemController</c> on the actual Gaming path, instead of silently relying on that
/// constructor's optional default parameter. A real DI/reflection-based test isn't practical here
/// (KCxWare.Helper's Program.cs is a top-level statements entry point with no test-visible seam and
/// no elevated SCM access is available in CI), so this asserts against the composition roots' own
/// source text - which is exactly the "silently skipped" failure mode this regression guards against:
/// if a future edit reverts to the bare `new WindowsSystemController(new CommandRunner())` call that
/// quietly falls back to a fresh, un-audited default store, this test fails immediately.
/// </summary>
public sealed class ProductionWiringTests
{
    [Theory]
    [InlineData("src/KCxWare.Helper/Program.cs")]
    [InlineData("src/KCxWare/ViewModels/MainViewModel.cs")]
    public void CompositionRoot_ExplicitlyWiresRealServiceRecoveryPolicyStore(string relativePath)
    {
        var repoRoot = FindRepoRoot();
        var path = Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"Expected composition root file at {path}.");

        var source = File.ReadAllText(path);
        Assert.Contains("new WindowsSystemController(", source, StringComparison.Ordinal);
        Assert.Contains("new ServiceRecoveryPolicyStore()", source, StringComparison.Ordinal);

        // The ServiceRecoveryPolicyStore construction must appear as an argument to the
        // WindowsSystemController construction it feeds - not merely exist somewhere else in the file.
        var controllerCallIndex = source.IndexOf("new WindowsSystemController(", StringComparison.Ordinal);
        var statementEndIndex = source.IndexOf(';', controllerCallIndex);
        Assert.True(statementEndIndex > controllerCallIndex);
        var callSpan = source[controllerCallIndex..statementEndIndex];
        Assert.Contains("new ServiceRecoveryPolicyStore()", callSpan, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "KCxWare.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
