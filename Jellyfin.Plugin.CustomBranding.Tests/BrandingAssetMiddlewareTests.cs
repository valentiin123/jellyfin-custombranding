using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Jellyfin.Plugin.CustomBranding;
using System.Reflection;

namespace Jellyfin.Plugin.CustomBranding.Tests
{
    public class BrandingAssetMiddlewareTests
    {
        [Fact]
        public async Task TryWriteConfiguredAssetAsync_WhenTryWriteDataUriThrows_ReturnsFalse()
        {
            // Arrange
            var next = new RequestDelegate(ctx => Task.CompletedTask);
            var loggerMock = new Mock<ILogger<BrandingAssetMiddleware>>();
            var middleware = new BrandingAssetMiddleware(next, loggerMock.Object);

            var contextMock = new DefaultHttpContext();
            var fileName = "favicon.ico";
            // Invalid data URI structure, missing payload after base64, throws FormatException on Convert.FromBase64String
            var source = "data:image/x-icon;base64,invalid-base64";

            // Act
            var method = typeof(BrandingAssetMiddleware).GetMethod("TryWriteConfiguredAssetAsync", BindingFlags.NonPublic | BindingFlags.Instance);
            var result = await (Task<bool>)method!.Invoke(middleware, new object[] { contextMock, source, fileName })!;

            // Assert
            Assert.False(result);

            // Verify that a warning was logged
            loggerMock.Verify(
                x => x.Log(
                    It.Is<LogLevel>(l => l == LogLevel.Warning),
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => true),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once());
        }
    }
}
