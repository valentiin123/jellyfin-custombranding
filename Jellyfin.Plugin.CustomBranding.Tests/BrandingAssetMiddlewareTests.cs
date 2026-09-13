using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Xunit;
using Jellyfin.Plugin.CustomBranding;

namespace Jellyfin.Plugin.CustomBranding.Tests
{
    public class BrandingAssetMiddlewareTests
    {
        [Fact]
        public async Task TryWriteDataUriAsync_InvalidDataUri_ReturnsFalse()
        {
            var context = new DefaultHttpContext();
            var result = await BrandingAssetMiddleware.TryWriteDataUriAsync(context, "data:", "favicon.ico");
            Assert.False(result);
        }

        [Fact]
        public async Task TryWriteDataUriAsync_Base64Payload_SetsHeadersAndWritesBody()
        {
            var context = new DefaultHttpContext();
            var responseBody = new MemoryStream();
            context.Response.Body = responseBody;

            var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("test data"));
            var dataUri = $"data:text/plain;base64,{payload}";

            var result = await BrandingAssetMiddleware.TryWriteDataUriAsync(context, dataUri, "test.txt");

            Assert.True(result);
            Assert.Equal(200, context.Response.StatusCode);
            Assert.Equal("text/plain", context.Response.ContentType);
            Assert.Equal("test data", Encoding.UTF8.GetString(responseBody.ToArray()));
        }

        [Fact]
        public async Task TryWriteDataUriAsync_UrlEncodedPayload_SetsHeadersAndWritesBody()
        {
            var context = new DefaultHttpContext();
            var responseBody = new MemoryStream();
            context.Response.Body = responseBody;

            var dataUri = "data:text/plain,Hello%20World";

            var result = await BrandingAssetMiddleware.TryWriteDataUriAsync(context, dataUri, "test.txt");

            Assert.True(result);
            Assert.Equal(200, context.Response.StatusCode);
            Assert.Equal("text/plain", context.Response.ContentType);
            Assert.Equal("Hello World", Encoding.UTF8.GetString(responseBody.ToArray()));
        }

        [Fact]
        public async Task TryWriteDataUriAsync_EmptyContentType_GuessesContentType()
        {
            var context = new DefaultHttpContext();
            var responseBody = new MemoryStream();
            context.Response.Body = responseBody;

            var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("image data"));
            var dataUri = $"data:;base64,{payload}";

            var result = await BrandingAssetMiddleware.TryWriteDataUriAsync(context, dataUri, "favicon.ico");

            Assert.True(result);
            Assert.Equal(200, context.Response.StatusCode);
            Assert.Equal("image/x-icon", context.Response.ContentType);
            Assert.Equal("image data", Encoding.UTF8.GetString(responseBody.ToArray()));
        }

        [Fact]
        public async Task TryWriteDataUriAsync_HeadRequest_SetsHeadersWithoutWritingBody()
        {
            var context = new DefaultHttpContext();
            context.Request.Method = HttpMethods.Head;
            var responseBody = new MemoryStream();
            context.Response.Body = responseBody;

            var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("test data"));
            var dataUri = $"data:text/plain;base64,{payload}";

            var result = await BrandingAssetMiddleware.TryWriteDataUriAsync(context, dataUri, "test.txt");

            Assert.True(result);
            Assert.Equal(200, context.Response.StatusCode);
            Assert.Equal("text/plain", context.Response.ContentType);
            Assert.Equal(0, responseBody.Length);
        }
    }
}
