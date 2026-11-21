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
        if (options == null) return Task.FromResult<String?>(null);

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
                return Task.FromResult<String?>(null);
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
