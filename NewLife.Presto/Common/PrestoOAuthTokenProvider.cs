using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace NewLife.Presto;

/// <summary>OAuth2 客户端凭据令牌提供者</summary>
/// <remarks>
/// 用 `client_credentials` 向令牌端点换取 Bearer 令牌，按 `expires_in` 提前刷新；
/// 令牌只在内存中保存，不写入日志或异常消息。
/// </remarks>
internal sealed class PrestoOAuthTokenProvider : IDisposable
{
    #region 字段
    private static readonly HttpClient _sharedClient = new() { Timeout = Timeout.InfiniteTimeSpan };

    private readonly PrestoConnectionOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HttpClient _client = _sharedClient;
    private readonly Boolean _ownsClient;
    private String? _token;
    private String _tokenType = "Bearer";
    private DateTime _expireTime = DateTime.MinValue;
    #endregion

    #region 构造
    /// <summary>实例化令牌提供者</summary>
    /// <param name="options">连接配置</param>
    /// <param name="handler">HTTP 处理器。测试或自定义传输时注入，为空时使用共享客户端</param>
    public PrestoOAuthTokenProvider(PrestoConnectionOptions options, HttpMessageHandler? handler = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (handler != null)
        {
            _client = new HttpClient(handler, false) { Timeout = Timeout.InfiniteTimeSpan };
            _ownsClient = true;
        }
    }
    #endregion

    #region 方法
    /// <summary>获取 Authorization 头值；令牌过期时自动刷新</summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>Authorization 头值</returns>
    public async Task<String?> GetAuthorizationAsync(CancellationToken cancellationToken)
    {
        if (IsValid) return BuildHeader();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // 并发请求可能已由其他调用方完成刷新
            if (IsValid) return BuildHeader();

            await AcquireAsync(cancellationToken).ConfigureAwait(false);
            return BuildHeader();
        }
        finally
        {
            _gate.Release();
        }
    }

    private Boolean IsValid => _token != null && DateTime.UtcNow < _expireTime;

    private String BuildHeader() => _tokenType + " " + _token;

    private async Task AcquireAsync(CancellationToken cancellationToken)
    {
        var pairs = new List<KeyValuePair<String, String>>(4)
        {
            new("grant_type", "client_credentials"),
            new("client_id", _options.ClientId),
        };
        if (!_options.ClientSecret.IsNullOrEmpty()) pairs.Add(new("client_secret", _options.ClientSecret));
        if (!_options.Scope.IsNullOrEmpty()) pairs.Add(new("scope", _options.Scope));

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(pairs),
        };
        request.Headers.TryAddWithoutValidation("Accept", "application/json");

        using var timeoutSource = new CancellationTokenSource(TimeSpan.FromSeconds(_options.ConnectionTimeout));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
        try
        {
            using var response = await _client.SendAsync(request, linked.Token).ConfigureAwait(false);
            var body = await ReadBodyAsync(response, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                throw new PrestoException(Localization.Translate("Failed to acquire an OAuth2 access token: HTTP {0}.", (Int32)response.StatusCode))
                {
                    HttpStatusCode = (Int32)response.StatusCode,
                };

            var dic = PrestoJson.ParseObject(body)
                ?? throw new PrestoException(Localization.Translate("The OAuth2 token endpoint returned an empty response."));

            var token = PrestoJson.GetString(dic, "access_token");
            if (token.IsNullOrEmpty()) throw new PrestoException(Localization.Translate("The OAuth2 token endpoint returned no access token."));

            var lifetime = PrestoJson.GetNullableInt64(dic, "expires_in") ?? 300;
            if (lifetime <= 0) lifetime = 300;

            // 提前刷新：取生命周期的 20%，最多 60 秒
            var margin = Math.Min(60, lifetime / 5);
            _token = token;
            _tokenType = PrestoJson.GetString(dic, "token_type") is { Length: > 0 } type ? type : "Bearer";
            _expireTime = DateTime.UtcNow.AddSeconds(lifetime - margin);
        }
        catch (OperationCanceledException ex) when (timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw PrestoException.ConnectionTimeout(_options.ConnectionTimeout, ex);
        }
    }

    private static async Task<String> ReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content == null) return String.Empty;

#if NET5_0_OR_GREATER
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
#else
        _ = cancellationToken;
        return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
#endif
    }
    #endregion

    #region 释放
    /// <summary>释放同步对象与自持客户端</summary>
    public void Dispose()
    {
        _gate.Dispose();
        if (_ownsClient) _client.Dispose();
    }
    #endregion
}
