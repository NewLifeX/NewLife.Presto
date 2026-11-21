using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Threading;

namespace NewLife.Presto;

/// <summary>共享 HttpClient 缓存</summary>
/// <remarks>
/// 缓存键为「scheme + host + port + path + proxy + 证书策略 + 压缩」；认证头按连接附加，不进入缓存键。
/// HttpClient 由缓存长期持有，连接关闭时不释放，避免频繁重建连接池。
/// </remarks>
internal static class PrestoHttpClientCache
{
    private static readonly ConcurrentDictionary<String, HttpClient> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>获取共享 HttpClient</summary>
    /// <param name="options">连接配置</param>
    /// <returns>HttpClient</returns>
    public static HttpClient Get(PrestoConnectionOptions options)
    {
        if (options == null) throw new ArgumentNullException(nameof(options));

        var key = BuildKey(options);
        return _cache.GetOrAdd(key, _ => Create(options));
    }

    private static String BuildKey(PrestoConnectionOptions options)
        => $"{options.BaseAddress.Scheme}://{options.BaseAddress.Host}:{options.BaseAddress.Port}{options.BaseAddress.AbsolutePath}|{options.Proxy}|{options.TrustServerCertificate}|{options.Compression}";

    private static HttpClient Create(PrestoConnectionOptions options)
    {
#if NETFRAMEWORK
        // net45/net461 默认 TLS 版本较旧，显式启用 TLS 1.2
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
#endif

        var handler = new HttpClientHandler
        {
            UseCookies = false,
            AllowAutoRedirect = true,
            AutomaticDecompression = options.Compression ? DecompressionMethods.GZip | DecompressionMethods.Deflate : DecompressionMethods.None,
        };

        if (!options.Proxy.IsNullOrEmpty())
        {
            handler.Proxy = new WebProxy(options.Proxy);
            handler.UseProxy = true;
        }

        if (options.TrustServerCertificate)
        {
#if NETFRAMEWORK
            // 旧框架没有按处理器设置证书回调的入口，只能使用全局回调；仅在内网/自签名场景显式开启
            ServicePointManager.ServerCertificateValidationCallback = (sender, certificate, chain, errors) => true;
#else
            handler.ServerCertificateCustomValidationCallback = (message, certificate, chain, errors) => true;
#endif
        }

        var client = new HttpClient(handler, true)
        {
            BaseAddress = options.BaseAddress,
            Timeout = Timeout.InfiniteTimeSpan,
        };

        return client;
    }
}
