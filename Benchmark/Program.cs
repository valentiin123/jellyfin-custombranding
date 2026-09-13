using System;
using System.IO;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

namespace Benchmark
{
    [MemoryDiagnoser]
    public class AssetResolutionBenchmark
    {
        private const string AssetRequest = "/web/assets/img/icon-transparent.png";
        private const string NonAssetRequest = "/Users/123/Items/456";

        [Benchmark]
        public bool Original_NonAsset()
        {
            return TryResolveAssetSourceOriginal(NonAssetRequest, out _, out _);
        }

        [Benchmark]
        public bool Optimized_NonAsset()
        {
            return TryResolveAssetSourceOptimized(NonAssetRequest, out _, out _);
        }

        [Benchmark]
        public bool Original_Asset()
        {
            return TryResolveAssetSourceOriginal(AssetRequest, out _, out _);
        }

        [Benchmark]
        public bool Optimized_Asset()
        {
            return TryResolveAssetSourceOptimized(AssetRequest, out _, out _);
        }

        private static bool TryResolveAssetSourceOriginal(string requestPath, out string source, out string fileName)
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

            // Dummy configuration check for benchmark
            bool hasConfig = true;
            if (!hasConfig) return false;

            if (fileName.StartsWith("favicon", StringComparison.Ordinal) ||
                fileName.StartsWith("apple-touch-icon", StringComparison.Ordinal))
            {
                source = "favicon_source";
                return true;
            }

            if (fileName.StartsWith("icon-transparent", StringComparison.Ordinal))
            {
                source = "icon_source";
                return true;
            }

            if (fileName.StartsWith("banner-light", StringComparison.Ordinal))
            {
                source = "banner_light_source";
                return true;
            }

            if (fileName.StartsWith("banner-dark", StringComparison.Ordinal))
            {
                source = "banner_dark_source";
                return true;
            }

            return false;
        }

        private static bool TryResolveAssetSourceOptimized(string requestPath, out string source, out string fileName)
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

            bool hasConfig = true;
            if (!hasConfig) return false;

            if (fileNameSpan.StartsWith("favicon", StringComparison.OrdinalIgnoreCase) ||
                fileNameSpan.StartsWith("apple-touch-icon", StringComparison.OrdinalIgnoreCase))
            {
                source = "favicon_source";
                fileName = fileNameSpan.ToString().ToLowerInvariant();
                return true;
            }

            if (fileNameSpan.StartsWith("icon-transparent", StringComparison.OrdinalIgnoreCase))
            {
                source = "icon_source";
                fileName = fileNameSpan.ToString().ToLowerInvariant();
                return true;
            }

            if (fileNameSpan.StartsWith("banner-light", StringComparison.OrdinalIgnoreCase))
            {
                source = "banner_light_source";
                fileName = fileNameSpan.ToString().ToLowerInvariant();
                return true;
            }

            if (fileNameSpan.StartsWith("banner-dark", StringComparison.OrdinalIgnoreCase))
            {
                source = "banner_dark_source";
                fileName = fileNameSpan.ToString().ToLowerInvariant();
                return true;
            }

            return false;
        }
    }

    public class Program
    {
        public static void Main(string[] args)
        {
            var summary = BenchmarkRunner.Run<AssetResolutionBenchmark>();
        }
    }
}
