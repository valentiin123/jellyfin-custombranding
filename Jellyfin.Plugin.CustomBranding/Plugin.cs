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
        public string Banner { get; set; } = string.Empty;

        // Web App Manifest Configuration
        public string ManifestName { get; set; } = "Jellyfin";
        public string ManifestShortName { get; set; } = "Jellyfin";
        public string ManifestDescription { get; set; } = "The Free Software Media System";
        public string ManifestLang { get; set; } = "en-US";
        public string ManifestThemeColor { get; set; } = "#101010";
        public bool ManifestThemeColorTransparent { get; set; } = false;
        public string ManifestBackgroundColor { get; set; } = "#101010";
        public bool ManifestBackgroundColorTransparent { get; set; } = false;
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

    public class BrandingAssetMiddleware : IDisposable
    {
        private static readonly HttpClient HttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(30),
            MaxResponseContentBufferSize = 10 * 1024 * 1024 // 10 MB Limit
        };

        private readonly RequestDelegate _next;
        private readonly ILogger<BrandingAssetMiddleware> _logger;
        private readonly IMemoryCache _memoryCache;
        private byte[]? _cachedManifestBytes;
        private readonly object _manifestCacheLock = new();
        private bool _disposed;

        public BrandingAssetMiddleware(RequestDelegate next, ILogger<BrandingAssetMiddleware> logger, IMemoryCache memoryCache)
        {
            _next = next;
            _logger = logger;
            _memoryCache = memoryCache;

            if (CustomBrandingPlugin.Instance != null)
            {
                CustomBrandingPlugin.Instance.ConfigurationChanged += OnConfigurationChanged;
            }
        }

        private CancellationTokenSource _cacheCancellationTokenSource = new();

        private void OnConfigurationChanged(object? sender, BasePluginConfiguration e)
        {
            lock (_manifestCacheLock)
            {
                _cachedManifestBytes = null;
            }

            var oldCts = Interlocked.Exchange(ref _cacheCancellationTokenSource, new CancellationTokenSource());
            oldCts.Cancel();
            oldCts.Dispose();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            if (CustomBrandingPlugin.Instance != null)
            {
                CustomBrandingPlugin.Instance.ConfigurationChanged -= OnConfigurationChanged;
            }

            _disposed = true;
            GC.SuppressFinalize(this);
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

            var requestPath = context.Request.Path.Value;
            if (string.IsNullOrEmpty(requestPath))
            {
                await _next(context);
                return;
            }

            if (Path.GetFileName(requestPath.AsSpan()).Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
            {
                if (await TryWriteManifestAsync(context))
                {
                    return;
                }
            }

            if (requestPath.EndsWith("/index.html", StringComparison.OrdinalIgnoreCase))
            {
                await HandleIndexHtmlAsync(context);
                return;
            }

            if (!TryResolveAssetSource(requestPath, out var source, out var fileName))
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

        private async Task HandleIndexHtmlAsync(HttpContext context)
        {
            var config = CustomBrandingPlugin.Instance?.Configuration;
            if (config == null || string.IsNullOrWhiteSpace(config.Banner))
            {
                await _next(context);
                return;
            }

            var originalBodyStream = context.Response.Body;
            using var responseBody = new MemoryStream();
            context.Response.Body = responseBody;

            try
            {
                await _next(context);

                var contentType = context.Response.ContentType ?? string.Empty;
                if (context.Response.StatusCode == 200 && contentType.Contains("text/html", StringComparison.OrdinalIgnoreCase))
                {
                    var cssToInject = @"<style>
/* Pour la version PC/Tablette (le header) */
header.MuiAppBar-root .MuiToolbar-root .MuiStack-root .MuiButton-text[href=""#/""] {
    content: url('custom-branding-banner.png');
    height: 37px;
}
/* Pour l'écran de connexion / splash screen (Legacy & Modern) */
.splashLogo {
    background-image: url('custom-branding-banner.png') !important;
}
</style>
</head>";

                    responseBody.Seek(0, SeekOrigin.Begin);
                    // Use leaveOpen: true so we don't dispose responseBody early
                    using var reader = new StreamReader(responseBody, leaveOpen: true);
                    var html = await reader.ReadToEndAsync();

                    html = html.Replace("</head>", cssToInject, StringComparison.OrdinalIgnoreCase);

                    context.Response.ContentLength = System.Text.Encoding.UTF8.GetByteCount(html);
                    responseBody.SetLength(0);

                    await using var writer = new StreamWriter(responseBody, leaveOpen: true);
                    await writer.WriteAsync(html);
                    await writer.FlushAsync();
                }

                responseBody.Seek(0, SeekOrigin.Begin);
                await responseBody.CopyToAsync(originalBodyStream);
            }
            finally
            {
                context.Response.Body = originalBodyStream;
            }
        }

        private async Task<bool> TryWriteManifestAsync(HttpContext context)
        {
            var config = CustomBrandingPlugin.Instance?.Configuration;
            if (config == null)
            {
                return false;
            }

            byte[]? manifestBytes = _cachedManifestBytes;
            if (manifestBytes == null)
            {
                var manifestObj = new
                {
                    name = config.ManifestName,
                    description = config.ManifestDescription,
                    lang = config.ManifestLang,
                    short_name = config.ManifestShortName,
                    start_url = "index.html#/home",
                    theme_color = config.ManifestThemeColorTransparent ? "transparent" : config.ManifestThemeColor,
                    background_color = config.ManifestBackgroundColorTransparent ? "transparent" : config.ManifestBackgroundColor,
                    display = "standalone",
                    icons = new[]
                    {
                        new
                        {
                            sizes = "512x512",
                            src = "favicons/touchicon512.png",
                            type = "image/png"
                        },
                        new
                        {
                            sizes = "1024x1024",
                            src = "favicons/touchicon1024.png", // WebApp logic fetches touchicon[SIZE].png so we can map it to our custom icon
                            type = "image/png"
                        }
                    }
                };

                var json = System.Text.Json.JsonSerializer.Serialize(manifestObj, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                manifestBytes = System.Text.Encoding.UTF8.GetBytes(json);

                lock (_manifestCacheLock)
                {
                    _cachedManifestBytes = manifestBytes;
                }
            }

            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "application/json";
            context.Response.ContentLength = manifestBytes.LongLength;

            if (!HttpMethods.IsHead(context.Request.Method))
            {
                await context.Response.Body.WriteAsync(manifestBytes, context.RequestAborted);
            }

            return true;
        }

        private static bool TryResolveAssetSource(string? requestPath, out string source, out string fileName)
        {
            source = string.Empty;
            fileName = string.Empty;

            if (string.IsNullOrWhiteSpace(requestPath))
            {
                return false;
            }

            var fileNameSpan = Path.GetFileName(requestPath.AsSpan());
            if (fileNameSpan.IsWhiteSpace())
            {
                return false;
            }

            var configuration = CustomBrandingPlugin.Instance?.Configuration;
            if (configuration == null)
            {
                return false;
            }

            if (fileNameSpan.StartsWith("favicon", StringComparison.OrdinalIgnoreCase) ||
                fileNameSpan.StartsWith("apple-touch-icon", StringComparison.OrdinalIgnoreCase) ||
                fileNameSpan.StartsWith("touchicon", StringComparison.OrdinalIgnoreCase))
            {
                source = configuration.Favicon ?? string.Empty;
                fileName = fileNameSpan.ToString().ToLowerInvariant();
                return true;
            }

            if (fileNameSpan.StartsWith("icon-transparent", StringComparison.OrdinalIgnoreCase))
            {
                source = !string.IsNullOrWhiteSpace(configuration.IconTransparent)
                    ? configuration.IconTransparent
                    : configuration.Banner ?? string.Empty;
                fileName = fileNameSpan.ToString().ToLowerInvariant();
                return true;
            }

            if (fileNameSpan.StartsWith("custom-branding-banner", StringComparison.OrdinalIgnoreCase) ||
                fileNameSpan.StartsWith("banner-light", StringComparison.OrdinalIgnoreCase) ||
                fileNameSpan.StartsWith("banner-dark", StringComparison.OrdinalIgnoreCase))
            {
                source = configuration.Banner ?? string.Empty;
                fileName = fileNameSpan.ToString().ToLowerInvariant();
                return true;
            }

            return false;
        }

        private async Task<bool> TryWriteConfiguredAssetAsync(HttpContext context, string source, string fileName)
        {
            try
            {
                var trimmedSource = source.Trim();
                if (trimmedSource.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    return await TryWriteDataUriAsync(context, trimmedSource, fileName);
                }

                if (Uri.TryCreate(trimmedSource, UriKind.Absolute, out var uri) &&
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

        private async Task<bool> TryWriteDataUriAsync(HttpContext context, string dataUri, string fileName)
        {
            var cacheKey = $"CustomBranding_DataUri_{dataUri.GetHashCode()}";
            if (!_memoryCache.TryGetValue<CachedAsset>(cacheKey, out var cachedAsset) || cachedAsset == null)
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

                if (!contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                byte[] bytes;
                if (isBase64)
                {
                    var rentedArray = System.Buffers.ArrayPool<char>.Shared.Rent(payload.Length);
                    try
                    {
                        var cleanLength = RemoveWhitespace(payload, rentedArray);
                        var cleanSpan = rentedArray.AsSpan(0, cleanLength);

                        bytes = new byte[(cleanLength * 3) / 4];
                        if (Convert.TryFromBase64Chars(cleanSpan, bytes, out var bytesWritten))
                        {
                            if (bytesWritten != bytes.Length)
                            {
                                Array.Resize(ref bytes, bytesWritten);
                            }
                        }
                        else
                        {
                            return false;
                        }
                    }
                    finally
                    {
                        System.Buffers.ArrayPool<char>.Shared.Return(rentedArray);
                    }
                }
                else
                {
                    var rentedArray = System.Buffers.ArrayPool<char>.Shared.Rent(payload.Length);
                    try
                    {
                        var cleanLength = RemoveWhitespace(payload, rentedArray);
                        var cleanSpan = rentedArray.AsSpan(0, cleanLength);
                        var decodedString = Uri.UnescapeDataString(cleanSpan.ToString());
                        bytes = System.Text.Encoding.UTF8.GetBytes(decodedString);
                    }
                    finally
                    {
                        System.Buffers.ArrayPool<char>.Shared.Return(rentedArray);
                    }
                }

                if (IsSvgBytes(bytes))
                {
                    contentType = "image/svg+xml";
                }

                cachedAsset = new CachedAsset
                {
                    ContentType = contentType,
                    Bytes = bytes
                };

                var cacheEntryOptions = new MemoryCacheEntryOptions()
                    .SetAbsoluteExpiration(TimeSpan.FromHours(1))
                    .AddExpirationToken(new Microsoft.Extensions.Primitives.CancellationChangeToken(_cacheCancellationTokenSource.Token));

                _memoryCache.Set(cacheKey, cachedAsset, cacheEntryOptions);
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

        private static int RemoveWhitespace(string input, Span<char> output)
        {
            var len = input.Length;
            var src = input.AsSpan();
            var j = 0;
            for (var i = 0; i < len; i++)
            {
                var ch = src[i];
                if (!char.IsWhiteSpace(ch))
                {
                    output[j++] = ch;
                }
            }
            return j;
        }

        private static bool IsSvgBytes(ReadOnlySpan<byte> bytes)
        {
            // Handle UTF-8 BOM
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                bytes = bytes.Slice(3);
            }

            // Simple check for SVG by looking for <svg or <?xml after any potential leading whitespace in UTF-8
            int index = 0;
            while (index < bytes.Length && (bytes[index] == ' ' || bytes[index] == '\t' || bytes[index] == '\n' || bytes[index] == '\r'))
            {
                index++;
            }

            if (index >= bytes.Length)
            {
                return false;
            }

            var start = bytes.Slice(index);

            // <svg
            var svgBytes = "<svg"u8;
            if (start.Length >= svgBytes.Length && start.StartsWith(svgBytes))
            {
                return true;
            }

            // <?xml
            var xmlBytes = "<?xml"u8;
            if (start.Length >= xmlBytes.Length && start.StartsWith(xmlBytes))
            {
                return true;
            }

            return false;
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

                var contentLength = response.Content.Headers.ContentLength;
                if (contentLength > 10 * 1024 * 1024)
                {
                    _logger.LogWarning("Custom Branding: L'asset {Uri} est trop volumineux ({Size} octets). Limite fixée à 10MB.", uri, contentLength);
                    return false;
                }

                var contentType = response.Content.Headers.ContentType?.MediaType;
                if (string.IsNullOrWhiteSpace(contentType) || contentType == "application/octet-stream")
                {
                    contentType = GuessContentType(uri.LocalPath);
                    if (contentType == "image/png")
                    {
                        contentType = GuessContentType(fileName);
                    }
                }

                if (uri.LocalPath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
                {
                    contentType = "image/svg+xml";
                }

                if (!contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("Custom Branding: Le contenu de {Uri} n'est pas une image ({ContentType}).", uri, contentType);
                    return false;
                }

                var bytes = await response.Content.ReadAsByteArrayAsync(context.RequestAborted);
                cachedAsset = new CachedAsset
                {
                    ContentType = contentType ?? "application/octet-stream",
                    Bytes = bytes
                };

                var cacheEntryOptions = new MemoryCacheEntryOptions()
                    .SetAbsoluteExpiration(TimeSpan.FromHours(1))
                    .AddExpirationToken(new Microsoft.Extensions.Primitives.CancellationChangeToken(_cacheCancellationTokenSource.Token));
                _memoryCache.Set(cacheKey, cachedAsset, cacheEntryOptions);
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
