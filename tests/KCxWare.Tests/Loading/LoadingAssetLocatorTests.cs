using KCxWare.Core.Loading;

namespace KCxWare.Tests.Loading;

public class LoadingAssetLocatorTests
{
    [Theory]
    [InlineData(@"C:\Program Files\KCxWare", @"C:\Program Files\KCxWare\Assets\kcxparade.mp4")]
    [InlineData(@"D:\KCxProjects\KCxWare\publish\win-x64", @"D:\KCxProjects\KCxWare\publish\win-x64\Assets\kcxparade.mp4")]
    public void ResolveVideoPath_IsAssetsKcxParadeMp4BesideTheApplicationDirectory(string baseDirectory, string expected)
    {
        Assert.Equal(expected, LoadingAssetLocator.ResolveVideoPath(baseDirectory));
    }

    [Fact]
    public void VideoRelativePath_IsExactlyAssetsKcxParadeMp4()
    {
        Assert.Equal(@"Assets\kcxparade.mp4", LoadingAssetLocator.VideoRelativePath);
    }

    [Fact]
    public void ResolveVideoPath_NeverDependsOnAnAbsoluteDevRepositoryPath()
    {
        // Guards against accidentally hardcoding the developer machine's checkout location
        // (e.g. D:\KCxProjects\...) instead of resolving relative to the running app.
        var resolved = LoadingAssetLocator.ResolveVideoPath(@"C:\Program Files\KCxWare");
        Assert.DoesNotContain(@"D:\KCxProjects", resolved, StringComparison.OrdinalIgnoreCase);
    }
}
