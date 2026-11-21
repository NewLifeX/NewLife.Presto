using System.Text;

namespace NewLife.Presto;

/// <summary>认证头生成器</summary>
/// <remarks>只生成 Authorization 头，不记录密码或令牌；JWT 支持每次请求前通过回调刷新。</remarks>
internal static class PrestoAuthProvider
{
    /// <summary>生成 Authorization 头</summary>
    /// <param name="options">连接配置</param>
    /// <param name="tokenProvider">令牌刷新回调，可为空</param>
    /// <returns>Authorization 头值；无认证时为空</returns>
    public static String? GetAuthorization(PrestoConnectionOptions options, Func<String>? tokenProvider)
    {
        if (options == null) return null;

        switch (options.Auth)
        {
            case PrestoAuth.Basic:
                {
                    var text = options.User + ":" + options.Password;
                    return "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
                }
            case PrestoAuth.Jwt:
                {
                    var token = tokenProvider?.Invoke();
                    if (token.IsNullOrEmpty()) token = options.AccessToken;
                    if (token.IsNullOrEmpty()) throw new PrestoException(Localization.Translate("AccessToken is required when Auth=JWT."));

                    return "Bearer " + token;
                }
            default:
                return null;
        }
    }
}
