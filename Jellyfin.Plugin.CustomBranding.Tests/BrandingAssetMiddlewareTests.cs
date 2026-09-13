using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.CustomBranding;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.CustomBranding.Tests
{
    public class BrandingAssetMiddlewareTests
    {
        private readonly Mock<RequestDelegate> _nextMock;
        private readonly Mock<ILogger<BrandingAssetMiddleware>> _loggerMock;
        private readonly Mock<IApplicationPaths> _appPathsMock;
        private readonly Mock<IXmlSerializer> _xmlSerializerMock;
        private readonly BrandingAssetMiddleware _middleware;
        private readonly CustomBrandingPlugin _plugin;

        public BrandingAssetMiddlewareTests()
        {
            _nextMock = new Mock<RequestDelegate>();
            _loggerMock = new Mock<ILogger<BrandingAssetMiddleware>>();
            _appPathsMock = new Mock<IApplicationPaths>();
            _xmlSerializerMock = new Mock<IXmlSerializer>();

            // Setup CustomBrandingPlugin to initialize static Instance
            _appPathsMock.Setup(p => p.PluginsPath).Returns(Path.GetTempPath());
            _appPathsMock.Setup(p => p.PluginConfigurationsPath).Returns(Path.GetTempPath());
            _appPathsMock.Setup(p => p.ConfigurationDirectoryPath).Returns(Path.GetTempPath());
            _appPathsMock.Setup(p => p.DataPath).Returns(Path.GetTempPath());

            _plugin = new CustomBrandingPlugin(_appPathsMock.Object, _xmlSerializerMock.Object);
            // set empty config
            _xmlSerializerMock.Setup(x => x.DeserializeFromFile(typeof(PluginConfiguration), It.IsAny<string>()))
                .Returns(new PluginConfiguration());

            _middleware = new BrandingAssetMiddleware(_nextMock.Object, _loggerMock.Object);
        }

        private DefaultHttpContext CreateHttpContext(string method, string path)
        {
            var context = new DefaultHttpContext();
            context.Request.Method = method;
            context.Request.Path = path;
            context.Response.Body = new MemoryStream();
            return context;
        }

        [Fact]
        public async Task InvokeAsync_NonGetOrHeadRequest_CallsNext()
        {
            // Arrange
            var context = CreateHttpContext("POST", "/favicon.ico");

            // Act
            await _middleware.InvokeAsync(context);

            // Assert
            _nextMock.Verify(next => next(context), Times.Once);
        }

        [Fact]
        public async Task InvokeAsync_UnmappedPath_CallsNext()
        {
            // Arrange
            var context = CreateHttpContext("GET", "/unrelated.txt");

            // Act
            await _middleware.InvokeAsync(context);

            // Assert
            _nextMock.Verify(next => next(context), Times.Once);
        }

        [Fact]
        public async Task InvokeAsync_MappedPathButEmptyConfig_CallsNext()
        {
            // Arrange
            var context = CreateHttpContext("GET", "/favicon.ico");
            // By default, _plugin.Configuration will have empty strings

            // Act
            await _middleware.InvokeAsync(context);

            // Assert
            _nextMock.Verify(next => next(context), Times.Once);
        }

        [Fact]
        public async Task InvokeAsync_MappedPathWithValidDataUri_WritesToResponseAndDoesNotCallNext()
        {
            // Arrange
            var context = CreateHttpContext("GET", "/favicon.ico");
            var base64Payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("testdata"));
            _plugin.UpdateConfiguration(new PluginConfiguration { Favicon = $"data:image/x-icon;base64,{base64Payload}" });

            // Act
            await _middleware.InvokeAsync(context);

            // Assert
            _nextMock.Verify(next => next(context), Times.Never);
            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
            Assert.Equal("image/x-icon", context.Response.ContentType);

            context.Response.Body.Seek(0, SeekOrigin.Begin);
            using var reader = new StreamReader(context.Response.Body);
            var responseText = await reader.ReadToEndAsync();
            Assert.Equal("testdata", responseText);
        }
    }
}
