using Xunit;
using Jellyfin.Plugin.CustomBranding;

namespace Jellyfin.Plugin.CustomBranding.Tests;

public class TryResolveAssetSourceTests
{
    private readonly PluginConfiguration _defaultConfig = new PluginConfiguration
    {
        Favicon = "https://example.com/favicon.ico",
        IconTransparent = "https://example.com/icon.png",
        BannerLight = "https://example.com/banner-light.png",
        BannerDark = "https://example.com/banner-dark.png"
    };

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void TryResolveAssetSource_NullOrWhitespaceRequestPath_ReturnsFalse(string? requestPath)
    {
        var result = BrandingAssetMiddleware.TryResolveAssetSource(requestPath, _defaultConfig, out var source, out var fileName);

        Assert.False(result);
        Assert.Equal(string.Empty, source);
        Assert.Equal(string.Empty, fileName);
    }

    [Fact]
    public void TryResolveAssetSource_EmptyFileName_ReturnsFalse()
    {
        var result = BrandingAssetMiddleware.TryResolveAssetSource("http://example.com/", _defaultConfig, out var source, out var fileName);

        Assert.False(result);
        Assert.Equal(string.Empty, source);
        Assert.Equal(string.Empty, fileName);
    }

    [Fact]
    public void TryResolveAssetSource_NullConfiguration_ReturnsFalse()
    {
        var result = BrandingAssetMiddleware.TryResolveAssetSource("/web/favicon.ico", null, out var source, out var fileName);

        Assert.False(result);
        Assert.Equal(string.Empty, source);
        Assert.Equal("favicon.ico", fileName);
    }

    [Theory]
    [InlineData("/web/favicon.ico", "https://example.com/favicon.ico", "favicon.ico")]
    [InlineData("/web/apple-touch-icon.png", "https://example.com/favicon.ico", "apple-touch-icon.png")]
    [InlineData("/web/icon-transparent.png", "https://example.com/icon.png", "icon-transparent.png")]
    [InlineData("/web/banner-light.png", "https://example.com/banner-light.png", "banner-light.png")]
    [InlineData("/web/banner-dark.png", "https://example.com/banner-dark.png", "banner-dark.png")]
    public void TryResolveAssetSource_ValidAsset_ReturnsTrueAndCorrectSource(string requestPath, string expectedSource, string expectedFileName)
    {
        var result = BrandingAssetMiddleware.TryResolveAssetSource(requestPath, _defaultConfig, out var source, out var fileName);

        Assert.True(result);
        Assert.Equal(expectedSource, source);
        Assert.Equal(expectedFileName, fileName);
    }

    [Theory]
    [InlineData("/web/favicon.ico", "favicon.ico")]
    [InlineData("/web/apple-touch-icon.png", "apple-touch-icon.png")]
    [InlineData("/web/icon-transparent.png", "icon-transparent.png")]
    [InlineData("/web/banner-light.png", "banner-light.png")]
    [InlineData("/web/banner-dark.png", "banner-dark.png")]
    public void TryResolveAssetSource_ValidAssetWithNullConfigProperties_ReturnsTrueAndEmptySource(string requestPath, string expectedFileName)
    {
        var nullPropsConfig = new PluginConfiguration
        {
            Favicon = null!,
            IconTransparent = null!,
            BannerLight = null!,
            BannerDark = null!
        };

        var result = BrandingAssetMiddleware.TryResolveAssetSource(requestPath, nullPropsConfig, out var source, out var fileName);

        Assert.True(result);
        Assert.Equal(string.Empty, source);
        Assert.Equal(expectedFileName, fileName);
    }

    [Fact]
    public void TryResolveAssetSource_UnrecognizedAsset_ReturnsFalse()
    {
        var result = BrandingAssetMiddleware.TryResolveAssetSource("/web/unknown.png", _defaultConfig, out var source, out var fileName);

        Assert.False(result);
        Assert.Equal(string.Empty, source);
        Assert.Equal("unknown.png", fileName);
    }
}