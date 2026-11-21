namespace NewLife.Presto;

/// <summary>Presto 协议类型</summary>
/// <remarks>PrestoDB 与 Trino 同源，SQL 方言与客户端协议一致，仅请求头前缀不同</remarks>
public enum PrestoProtocol
{
    /// <summary>PrestoDB。使用 X-Presto-* 请求头</summary>
    Presto = 0,

    /// <summary>Trino。使用 X-Trino-* 请求头</summary>
    Trino = 1,
}

/// <summary>认证模式</summary>
public enum PrestoAuth
{
    /// <summary>无认证</summary>
    None = 0,

    /// <summary>HTTP Basic 认证</summary>
    Basic = 1,

    /// <summary>JWT/Bearer 认证</summary>
    Jwt = 2,

    /// <summary>OAuth2 客户端凭据。用 TokenEndpoint + ClientId + ClientSecret 换取 Bearer 令牌</summary>
    OAuth2 = 3,
}
