using KCxWare.Core.Windows;

namespace KCxWare.Tests.Loading;

public class HelperLocatorTests
{
    [Theory]
    [InlineData(@"C:\Program Files\KCxWare", @"C:\Program Files\KCxWare\KCxWare.Helper.exe")]
    [InlineData(@"D:\KCxProjects\KCxWare\publish\win-x64", @"D:\KCxProjects\KCxWare\publish\win-x64\KCxWare.Helper.exe")]
    [InlineData(@"C:\Some\Other\Install\Dir", @"C:\Some\Other\Install\Dir\KCxWare.Helper.exe")]
    public void ResolveHelperPath_IsAlwaysBesideTheGivenApplicationDirectory(string baseDirectory, string expected)
    {
        Assert.Equal(expected, HelperLocator.ResolveHelperPath(baseDirectory));
    }

    [Fact]
    public void ResolveHelperPath_NeverEmbedsARepositoryOrPublishPathOfItsOwn()
    {
        // The resolved path must be derived purely from whatever base directory is passed in -
        // it must never contain a hardcoded repository or publish path baked into the resolver.
        var installed = HelperLocator.ResolveHelperPath(@"C:\Program Files\KCxWare");

        Assert.DoesNotContain(@"D:\KCxProjects", installed, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("publish", installed, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveHelperPath_MatchesTheRunningApplicationDirectory_NotADifferentOne()
    {
        // Running from the dev publish folder must resolve the helper from that same folder...
        var devPublish = HelperLocator.ResolveHelperPath(@"D:\KCxProjects\KCxWare\publish\win-x64");
        Assert.StartsWith(@"D:\KCxProjects\KCxWare\publish\win-x64", devPublish);

        // ...while running the installed copy must resolve the helper from the installed folder,
        // never falling back to the development publish directory.
        var installed = HelperLocator.ResolveHelperPath(@"C:\Program Files\KCxWare");
        Assert.StartsWith(@"C:\Program Files\KCxWare", installed);
        Assert.DoesNotContain("publish", installed, StringComparison.OrdinalIgnoreCase);
    }
}
