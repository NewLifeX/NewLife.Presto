using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NewLife.Presto;

/// <summary>认证头生成器</summary>
/// <remarks>
/// 只生成 Authorization 头，不记录密码或令牌。JWT 与 OAuth2 都支持每次请求前刷新：
/// 显式 <see cref="PrestoConnection.AccessTokenProvider"/> 优先级最高，其次才是内置 OAuth2 令牌缓存。
/// </remarks>
internal static class PrestoAuthProvider
{
    /// <summary>生成 Authorization 头</summary>
    /// <param name="options">连接配置</param>
    /// <param name="tokenProvider">令牌刷新回调，可为空</param>
    /// <param name="oauth">OAuth2 令牌提供者，可为空</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>Authorization 头值；无认证时为空</returns>
    public static Task<String?> GetAuthorizationAsync(PrestoConnectionOptions options, Func<String>? tokenProvider, PrestoOAuthTokenProvider? oauth, CancellationToken cancellationToken)
    {
        if (options == null) return _noAuth;

        switch (options.Auth)
        {
            case PrestoAuth.Basic:
                return Task.FromResult<String?>(BuildBasic(options));
            case PrestoAuth.Jwt:
                {
                    var token = tokenProvider?.Invoke();
                    if (token.IsNullOrEmpty()) token = options.AccessToken;
                    if (token.IsNullOrEmpty()) throw new PrestoException(Localization.Translate("AccessToken is required when Auth=JWT."));

                    return Task.FromResult<String?>("Bearer " + token);
                }
            case PrestoAuth.OAuth2:
                {
                    var token = tokenProvider?.Invoke();
                    if (!token.IsNullOrEmpty()) return Task.FromResult<String?>("Bearer " + token);

                    if (oauth == null) throw new PrestoException(Localization.Translate("OAuth2 authentication is not initialized."));

                    return oauth.GetAuthorizationAsync(cancellationToken);
                }
            default:
                return _noAuth;
        }
    }

    private static readonly Task<String?> _noAuth = Task.FromResult<String?>(null);

    /// <summary>同步生成认证头；OAuth2 需要异步换取令牌，不在本方法覆盖范围</summary>
    /// <param name="options">连接配置</param>
    /// <param name="tokenProvider">令牌刷新回调，可为空</param>
    /// <returns>Authorization 头值；无认证时为空</returns>
    public static String? GetAuthorization(PrestoConnectionOptions options, Func<String>? tokenProvider)
    {
        if (options == null) return null;

        switch (options.Auth)
        {
            case PrestoAuth.Basic:
                return BuildBasic(options);
            case PrestoAuth.Jwt:
                var token = tokenProvider?.Invoke();
                if (token.IsNullOrEmpty()) token = options.AccessToken;
                if (token.IsNullOrEmpty()) throw new PrestoException(Localization.Translate("AccessToken is required when Auth=JWT."));

                return "Bearer " + token;
            default:
                return null;
        }
    }

    /// <summary>生成 Basic 认证头值</summary>
    /// <param name="options">连接配置</param>
    /// <returns>Authorization 头值</returns>
    public static String BuildBasic(PrestoConnectionOptions options)
    {
        var text = options.User + ":" + options.Password;

        return "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
    }
}

/// <summary>Bearer 认证头缓存</summary>
/// <remarks>
/// 令牌通常是数百字节的 JWT，而一次查询要发 5~6 个请求；每请求重新拼接会产生数百字节到数 KB 的临时垃圾。
/// 令牌刷新（回调返回新令牌、OAuth2 重新换取）时按值比较自动失效。
/// </remarks>
internal sealed class PrestoBearerCache
{
    private String? _token;
    private Task<String?>? _header;

    /// <summary>按令牌值获取认证头任务；令牌未变时复用同一个任务实例</summary>
    /// <param name="token">访问令牌</param>
    /// <returns>认证头任务</returns>
    public Task<String?> Get(String token)
    {
        var cached = _header;
        if (cached != null && String.Equals(_token, token, StringComparison.Ordinal)) return cached;

        _token = token;

        return _header = Task.FromResult<String?>("Bearer " + token);
    }
}
