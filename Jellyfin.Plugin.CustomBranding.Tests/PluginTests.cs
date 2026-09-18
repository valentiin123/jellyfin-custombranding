using System;
using System.Linq;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Serialization;
using Moq;
using Xunit;
using Jellyfin.Plugin.CustomBranding;

namespace Jellyfin.Plugin.CustomBranding.Tests
{
    public class PluginTests
    {
        [Fact]
        public void PluginConfiguration_HasCorrectDefaults()
        {
            var config = new PluginConfiguration();

            Assert.Equal(string.Empty, config.Favicon);
            Assert.Equal(string.Empty, config.IconTransparent);
            Assert.Equal(string.Empty, config.BannerLight);
            Assert.Equal(string.Empty, config.BannerDark);
        }

        private Mock<IApplicationPaths> GetApplicationPathsMock()
        {
            var mock = new Mock<IApplicationPaths>();
            mock.SetupGet(m => m.PluginConfigurationsPath).Returns("dummy_path");
            mock.SetupGet(m => m.PluginsPath).Returns("dummy_path");
            mock.SetupGet(m => m.DataPath).Returns("dummy_path");
            return mock;
        }

        [Fact]
        public void CustomBrandingPlugin_HasCorrectProperties()
        {
            var applicationPathsMock = GetApplicationPathsMock();
            var xmlSerializerMock = new Mock<IXmlSerializer>();

            var plugin = new CustomBrandingPlugin(applicationPathsMock.Object, xmlSerializerMock.Object);

            Assert.Equal("Custom Branding", plugin.Name);
            Assert.Equal(Guid.Parse("A1B2C3D4-E5F6-4789-8901-23456789ABCD"), plugin.Id);
            Assert.Equal("Remplace les assets de branding Jellyfin 12 (favicon, logo, bannières).", plugin.Description);
            Assert.Same(plugin, CustomBrandingPlugin.Instance);
        }

        [Fact]
        public void CustomBrandingPlugin_GetPages_ReturnsExpectedConfigurationPage()
        {
            var applicationPathsMock = GetApplicationPathsMock();
            var xmlSerializerMock = new Mock<IXmlSerializer>();

            var plugin = new CustomBrandingPlugin(applicationPathsMock.Object, xmlSerializerMock.Object);
            var pages = plugin.GetPages().ToList();

            Assert.Single(pages);
            var page = pages.First();
            Assert.Equal(plugin.Name, page.Name);
            Assert.Equal("Jellyfin.Plugin.CustomBranding.Configuration.configPage.html", page.EmbeddedResourcePath);
        }
    }
}
