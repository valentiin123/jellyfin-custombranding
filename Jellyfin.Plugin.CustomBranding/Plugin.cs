using System.Net;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.CustomBranding
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        public string Favicon { get; set; } = string.Empty;
        public string IconTransparent { get; set; } = string.Empty;
        public string BannerLight { get; set; } = string.Empty;
        public string BannerDark { get; set; } = string.Empty;
    }

    public class CustomBrandingPlugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        public static CustomBrandingPlugin? Instance { get; private set; }

        public override string Name => "Custom Branding";

        public override Guid Id => Guid.Parse("A1B2C3D4-E5F6-4789-8901-23456789ABCD");

        public override string Description => "Remplace les assets de branding Jellyfin 12 (favicon, logo, bannières).";

        public CustomBrandingPlugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
        }

        public IEnumerable<PluginPageInfo> GetPages()
        {
            return new[]
            {
                new PluginPageInfo
                {
                    Name = Name,
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.configPage.html"
                }
            };
        }
    }

    public class PluginServiceRegistrator : IPluginServiceRegistrator
    {
        public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
        {
            serviceCollection.AddTransient<IStartupFilter, BrandingAssetStartupFilter>();
        }
    }

    public class BrandingAssetStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            return app =>
            {
                app.UseMiddleware<BrandingAssetMiddleware>();
                next(app);
            };
        }
    }

    public class BrandingAssetMiddleware
    {
        private static readonly HttpClient HttpClient = new();

        private readonly RequestDelegate _next;
        private readonly ILogger<BrandingAssetMiddleware> _logger;
        private readonly IMemoryCache _memoryCache;

        public BrandingAssetMiddleware(RequestDelegate next, ILogger<BrandingAssetMiddleware> logger, IMemoryCache memoryCache)
        {
            _next = next;
            _logger = logger;
            _memoryCache = memoryCache;
        }

        private class CachedAsset
        {
            public string ContentType { get; set; } = string.Empty;
            public byte[] Bytes { get; set; } = Array.Empty<byte>();
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
            {
                await _next(context);
                return;
            }

            if (!TryResolveAssetSource(context.Request.Path.Value, out var source, out var fileName))
            {
                await _next(context);
                return;
            }

            if (string.IsNullOrWhiteSpace(source))
            {
                await _next(context);
                return;
            }

            var served = await TryWriteConfiguredAssetAsync(context, source, fileName);
            if (!served)
            {
                await _next(context);
            }
        }

        private static bool TryResolveAssetSource(string? requestPath, out string source, out string fileName)
        {
            source = string.Empty;
            fileName = string.Empty;

            if (string.IsNullOrWhiteSpace(requestPath))
            {
                return false;
            }

            fileName = Path.GetFileName(requestPath).ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return false;
            }

            var configuration = CustomBrandingPlugin.Instance?.Configuration;
            if (configuration == null)
            {
                return false;
            }

            if (fileName.StartsWith("favicon", StringComparison.Ordinal) ||
                fileName.StartsWith("apple-touch-icon", StringComparison.Ordinal))
            {
                source = configuration.Favicon ?? string.Empty;
                return true;
            }

            if (fileName.StartsWith("icon-transparent", StringComparison.Ordinal))
            {
                source = configuration.IconTransparent ?? string.Empty;
                return true;
            }

            if (fileName.StartsWith("banner-light", StringComparison.Ordinal))
            {
                source = configuration.BannerLight ?? string.Empty;
                return true;
            }

            if (fileName.StartsWith("banner-dark", StringComparison.Ordinal))
            {
                source = configuration.BannerDark ?? string.Empty;
                return true;
            }

            return false;
        }

        private async Task<bool> TryWriteConfiguredAssetAsync(HttpContext context, string source, string fileName)
        {
            try
            {
                if (source.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    return await TryWriteDataUriAsync(context, source, fileName);
                }

                if (Uri.TryCreate(source, UriKind.Absolute, out var uri) &&
                    (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                {
                    return await TryWriteRemoteUrlAsync(context, uri, fileName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Custom Branding: impossible de servir l'asset {FileName}", fileName);
            }

            return false;
        }

        private static async Task<bool> TryWriteDataUriAsync(HttpContext context, string dataUri, string fileName)
        {
            var commaIndex = dataUri.IndexOf(',', StringComparison.Ordinal);
            if (commaIndex <= 5)
            {
                return false;
            }

            var metadata = dataUri[5..commaIndex];
            var payload = dataUri[(commaIndex + 1)..];
            var isBase64 = metadata.EndsWith(";base64", StringComparison.OrdinalIgnoreCase);

            string contentType;
            if (isBase64)
            {
                contentType = metadata[..^";base64".Length];
            }
            else
            {
                contentType = metadata;
            }

            if (string.IsNullOrWhiteSpace(contentType))
            {
                contentType = GuessContentType(fileName);
            }

            byte[] bytes;
            if (isBase64)
            {
                bytes = Convert.FromBase64String(payload);
            }
            else
            {
                bytes = System.Text.Encoding.UTF8.GetBytes(WebUtility.UrlDecode(payload));
            }

            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = contentType;
            context.Response.ContentLength = bytes.LongLength;
            if (!HttpMethods.IsHead(context.Request.Method))
            {
                await context.Response.Body.WriteAsync(bytes, context.RequestAborted);
            }

            return true;
        }

        private async Task<bool> TryWriteRemoteUrlAsync(HttpContext context, Uri uri, string fileName)
        {
            var cacheKey = $"CustomBranding_RemoteAsset_{uri}";
            if (!_memoryCache.TryGetValue<CachedAsset>(cacheKey, out var cachedAsset) || cachedAsset == null)
            {
                using var response = await HttpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);
                if (!response.IsSuccessStatusCode)
                {
                    return false;
                }

                var contentType = response.Content.Headers.ContentType?.ToString();
                if (string.IsNullOrWhiteSpace(contentType))
                {
                    contentType = GuessContentType(fileName);
                }

                var bytes = await response.Content.ReadAsByteArrayAsync(context.RequestAborted);
                cachedAsset = new CachedAsset { ContentType = contentType, Bytes = bytes };

                _memoryCache.Set(cacheKey, cachedAsset, TimeSpan.FromHours(24));
            }

            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = cachedAsset.ContentType;
            context.Response.ContentLength = cachedAsset.Bytes.LongLength;

            if (!HttpMethods.IsHead(context.Request.Method))
            {
                await context.Response.Body.WriteAsync(cachedAsset.Bytes, context.RequestAborted);
            }

            return true;
        }

        private static string GuessContentType(string fileName)
        {
            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            return extension switch
            {
                ".ico" => "image/x-icon",
                ".svg" => "image/svg+xml",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                _ => "image/png"
            };
        }
    }
}
