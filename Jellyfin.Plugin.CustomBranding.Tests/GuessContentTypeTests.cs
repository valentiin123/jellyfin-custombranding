using Jellyfin.Plugin.CustomBranding;
using Xunit;

namespace Jellyfin.Plugin.CustomBranding.Tests;

public class GuessContentTypeTests
{
    [Theory]
    [InlineData("icon.ico", "image/x-icon")]
    [InlineData("image.svg", "image/svg+xml")]
    [InlineData("photo.jpg", "image/jpeg")]
    [InlineData("photo.jpeg", "image/jpeg")]
    [InlineData("animation.gif", "image/gif")]
    [InlineData("image.webp", "image/webp")]
    [InlineData("logo.png", "image/png")]
    [InlineData("unknown.ext", "image/png")]
    [InlineData("noextension", "image/png")]
    [InlineData("CAPITAL.JPG", "image/jpeg")]
    public void GuessContentType_ReturnsCorrectType(string fileName, string expectedContentType)
    {
        // Act
        var result = BrandingAssetMiddleware.GuessContentType(fileName);

        // Assert
        Assert.Equal(expectedContentType, result);
    }
}
