using KCxWare.Core.Loading;

namespace KCxWare.Tests.Loading;

public class LoadingAssetLocatorTests
{
    [Theory]
    [InlineData(@"C:\Program Files\KCxWare", @"C:\Program Files\KCxWare\Assets\final.mp4")]
    [InlineData(@"D:\KCxProjects\KCxWare\publish\win-x64", @"D:\KCxProjects\KCxWare\publish\win-x64\Assets\final.mp4")]
    public void ResolveVideoPath_IsAssetsFinalMp4BesideTheApplicationDirectory(string baseDirectory, string expected)
    {
        Assert.Equal(expected, LoadingAssetLocator.ResolveVideoPath(baseDirectory));
    }

    [Fact]
    public void VideoRelativePath_IsExactlyAssetsFinalMp4()
    {
        Assert.Equal(@"Assets\final.mp4", LoadingAssetLocator.VideoRelativePath);
    }
}
