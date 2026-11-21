using System.Collections.Generic;

namespace NewLife.Presto;

/// <summary>连接配置快照</summary>
/// <remarks>
/// 在 <see cref="PrestoConnection.Open"/> 时从连接字符串构建，避免连接字符串在会话期间变化影响请求。
/// 所有属性均为只读，可在命令、读取器与协议层之间安全传递。
/// </remarks>
internal sealed class PrestoConnectionOptions
{
    #region 属性
    /// <summary>协调器主机</summary>
    public String Host { get; init; } = "localhost";

    /// <summary>协调器端口</summary>
    public Int32 Port { get; init; } = 8080;

    /// <summary>是否使用 HTTPS</summary>
    public Boolean UseSsl { get; init; }

    /// <summary>网关路径前缀，已补齐首尾斜杠</summary>
    public String Path { get; init; } = "/";

    /// <summary>默认 Catalog</summary>
    public String Catalog { get; init; } = "";

    /// <summary>默认 Schema</summary>
    public String Schema { get; init; } = "";

    /// <summary>会话用户</summary>
    public String User { get; init; } = "anonymous";

    /// <summary>Basic 认证密码</summary>
    public String Password { get; init; } = "";

    /// <summary>认证模式</summary>
    public PrestoAuth Auth { get; init; }

    /// <summary>JWT/Bearer 令牌</summary>
    public String AccessToken { get; init; } = "";

    /// <summary>OAuth2 令牌端点</summary>
    public String TokenEndpoint { get; init; } = "";

    /// <summary>OAuth2 客户端标识</summary>
    public String ClientId { get; init; } = "";

    /// <summary>OAuth2 客户端密钥</summary>
    public String ClientSecret { get; init; } = "";

    /// <summary>OAuth2 授权范围</summary>
    public String Scope { get; init; } = "";

    /// <summary>协议类型</summary>
    public PrestoProtocol Protocol { get; init; }

    /// <summary>会话时区</summary>
    public String TimeZone { get; init; } = "UTC";

    /// <summary>请求头语言</summary>
    public String Language { get; init; } = "";

    /// <summary>客户端来源标识</summary>
    public String Source { get; init; } = "NewLife.Presto";

    /// <summary>客户端标签</summary>
    public String ClientTags { get; init; } = "";

    /// <summary>客户端信息</summary>
    public String ClientInfo { get; init; } = "";

    /// <summary>资源组</summary>
    public String ResourceGroup { get; init; } = "";

    /// <summary>链路令牌</summary>
    public String TraceToken { get; init; } = "";

    /// <summary>角色</summary>
    public String Role { get; init; } = "";

    /// <summary>会话属性</summary>
    public IDictionary<String, String> SessionProperties { get; init; } = new Dictionary<String, String>(StringComparer.OrdinalIgnoreCase);

    /// <summary>自定义请求头</summary>
    public IDictionary<String, String> CustomHeaders { get; init; } = new Dictionary<String, String>(StringComparer.OrdinalIgnoreCase);

    /// <summary>HTTP 代理地址</summary>
    public String Proxy { get; init; } = "";

    /// <summary>是否放宽服务端证书校验</summary>
    public Boolean TrustServerCertificate { get; init; }

    /// <summary>命令超时秒数，0 表示不限制</summary>
    public Int32 CommandTimeout { get; init; }

    /// <summary>单次 HTTP 请求超时秒数</summary>
    public Int32 ConnectionTimeout { get; init; } = 100;

    /// <summary>瞬态错误最大重试次数</summary>
    public Int32 MaxRetries { get; init; } = 3;

    /// <summary>初始重试间隔毫秒数</summary>
    public Int32 RetryInterval { get; init; } = 200;

    /// <summary>Open 时是否请求服务端信息验活</summary>
    public Boolean TestConnection { get; init; }

    /// <summary>是否请求并接受压缩响应</summary>
    public Boolean Compression { get; init; } = true;

    /// <summary>协调器基地址，始终以斜杠结尾</summary>
    public Uri BaseAddress { get; init; } = new("http://localhost:8080/");
    #endregion

    #region 方法
    /// <summary>从连接字符串构建配置快照</summary>
    /// <param name="builder">连接字符串构建器</param>
    /// <returns>配置快照</returns>
    public static PrestoConnectionOptions From(PrestoConnectionStringBuilder builder)
    {
        if (builder == null) throw new ArgumentNullException(nameof(builder));

        builder.Validate();

        return new PrestoConnectionOptions
        {
            Host = builder.Host,
            Port = builder.Port,
            UseSsl = builder.UseSsl,
            Path = NormalizePath(builder.Path),
            Catalog = builder.Catalog,
            Schema = builder.Schema,
            User = builder.User,
            Password = builder.Password,
            Auth = builder.Auth,
            AccessToken = builder.AccessToken,
            TokenEndpoint = builder.TokenEndpoint,
            ClientId = builder.ClientId,
            ClientSecret = builder.ClientSecret,
            Scope = builder.Scope,
            Protocol = builder.Protocol,
            TimeZone = builder.TimeZone,
            Language = builder.Language,
            Source = builder.Source,
            ClientTags = builder.ClientTags,
            ClientInfo = builder.ClientInfo,
            ResourceGroup = builder.ResourceGroup,
            TraceToken = builder.TraceToken,
            Role = builder.Role,
            SessionProperties = new Dictionary<String, String>(builder.SessionProperties, StringComparer.OrdinalIgnoreCase),
            CustomHeaders = new Dictionary<String, String>(builder.CustomHeaders, StringComparer.OrdinalIgnoreCase),
            Proxy = builder.Proxy,
            TrustServerCertificate = builder.TrustServerCertificate,
            CommandTimeout = builder.CommandTimeout,
            ConnectionTimeout = builder.ConnectionTimeout,
            MaxRetries = builder.MaxRetries,
            RetryInterval = builder.RetryInterval,
            TestConnection = builder.TestConnection,
            Compression = builder.Compression,
            BaseAddress = BuildBaseAddress(builder.Host, builder.Port, builder.UseSsl, NormalizePath(builder.Path)),
        };
    }

    /// <summary>构建协调器基地址</summary>
    /// <param name="host">主机</param>
    /// <param name="port">端口</param>
    /// <param name="useSsl">是否 HTTPS</param>
    /// <param name="path">已归一化的路径</param>
    /// <returns>基地址</returns>
    public static Uri BuildBaseAddress(String host, Int32 port, Boolean useSsl, String path)
    {
        var builder = new UriBuilder(useSsl ? Uri.UriSchemeHttps : Uri.UriSchemeHttp, host, port, path);
        return builder.Uri;
    }

    /// <summary>归一化网关路径，保证以斜杠开头和结尾</summary>
    /// <param name="path">原始路径</param>
    /// <returns>归一化路径</returns>
    public static String NormalizePath(String? path)
    {
        if (path.IsNullOrWhiteSpace()) return "/";

        var text = path.Trim();
        if (!text.StartsWith("/")) text = "/" + text;
        if (!text.EndsWith("/")) text += "/";

        return text;
    }
    #endregion
}
