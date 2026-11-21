using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;

namespace NewLife.Presto;

/// <summary>Presto/Trino 连接字符串构建器</summary>
/// <remarks>
/// 兼容 XCode 与常见 ADO.NET 写法：Server/Data Source → Host，Database → Catalog，Owner → Schema。
/// 参数在构造或赋值 ConnectionString 时归一化，非法值在读取对应属性时抛出带本地化消息的异常。
/// </remarks>
public sealed class PrestoConnectionStringBuilder : DbConnectionStringBuilder
{
    #region 字段
    private static readonly String[] _hostKeys = ["Host", "Server", "Data Source", "DataSource"];
    private static readonly String[] _catalogKeys = ["Catalog", "Database", "Initial Catalog"];
    private static readonly String[] _schemaKeys = ["Schema", "Owner"];
    private static readonly String[] _userKeys = ["User", "User ID", "UserID", "Username", "UID"];
    private static readonly String[] _passwordKeys = ["Password", "Pwd"];
    private static readonly String[] _sslKeys = ["UseSsl", "UseSSL", "EnableSSL", "SSL"];
    private static readonly String[] _trustKeys = ["TrustServerCertificate", "IgnoreSslErrors"];
    private static readonly String[] _tokenKeys = ["AccessToken", "Token"];
    private static readonly String[] _tokenEndpointKeys = ["TokenEndpoint", "OAuthTokenEndpoint", "OAuthEndpoint", "TokenUrl", "OAuthTokenUrl"];
    private static readonly String[] _clientIdKeys = ["ClientId", "OAuthClientId", "AppKey", "AppId"];
    private static readonly String[] _clientSecretKeys = ["ClientSecret", "OAuthClientSecret", "AppSecret"];
    private static readonly String[] _scopeKeys = ["Scope", "OAuthScope"];
    private static readonly String[] _complexTypesKeys = ["ComplexTypes", "ComplexType", "StructuredTypes"];
    private static readonly String[] _spoolingKeys = ["Spooling", "SpoolingProtocol", "SpoolingEnabled"];
    private static readonly String[] _sourceKeys = ["ApplicationName"];
    private static readonly String[] _pathKeys = ["BasePath"];
    private static readonly String[] _proxyKeys = ["HttpProxy"];
    #endregion

    #region 构造
    /// <summary>实例化连接字符串构建器</summary>
    public PrestoConnectionStringBuilder() { }

    /// <summary>实例化连接字符串构建器</summary>
    /// <param name="connectionString">连接字符串</param>
    public PrestoConnectionStringBuilder(String connectionString) => ConnectionString = connectionString;
    #endregion

    #region 属性
    /// <summary>连接字符串。赋值后自动归一化别名</summary>
    public new String ConnectionString
    {
        get => base.ConnectionString;
        set
        {
            base.ConnectionString = value ?? String.Empty;
            Normalize();
        }
    }

    /// <summary>按键读写。写入新键后下次读取重新归一化别名</summary>
    /// <param name="keyword">键名</param>
    /// <returns>键值</returns>
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP
    [System.Diagnostics.CodeAnalysis.AllowNull]
#endif
    public override Object this[String keyword]
    {
        get => base[keyword];
        set
        {
            base[keyword] = value;
            _normalized = false;
        }
    }

    /// <summary>协调器主机。默认 localhost</summary>
    public String Host
    {
        get
        {
            var value = GetString("Host");
            if (value == null) return "localhost";
            if (value.IsNullOrWhiteSpace()) throw new ArgumentException(Localization.Translate("Host cannot be empty."), nameof(Host));

            return value.Trim();
        }
        set
        {
            if (value.IsNullOrWhiteSpace()) throw new ArgumentException(Localization.Translate("Host cannot be empty."), nameof(Host));

            this["Host"] = value.Trim();
        }
    }

    /// <summary>协调器端口。默认 8080</summary>
    public Int32 Port
    {
        get
        {
            var value = GetString("Port");
            if (value.IsNullOrEmpty()) return 8080;
            if (!Int32.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) || port is < 1 or > 65535)
                throw new ArgumentException(Localization.Translate("Port must be between 1 and 65535."), nameof(Port));

            return port;
        }
        set
        {
            if (value is < 1 or > 65535) throw new ArgumentException(Localization.Translate("Port must be between 1 and 65535."), nameof(Port));

            this["Port"] = value;
        }
    }

    /// <summary>默认 Catalog。XCode 的 Database 映射到此</summary>
    public String Catalog
    {
        get => GetString("Catalog")?.Trim() ?? String.Empty;
        set => this["Catalog"] = value;
    }

    /// <summary>默认 Schema。XCode 的 Owner 映射到此</summary>
    public String Schema
    {
        get => GetString("Schema")?.Trim() ?? String.Empty;
        set => this["Schema"] = value;
    }

    /// <summary>会话用户。缺省依次取环境用户名与 anonymous</summary>
    public String User
    {
        get
        {
            var value = GetString("User");
            if (!value.IsNullOrWhiteSpace()) return value.Trim();

            value = Environment.UserName;
            return value.IsNullOrWhiteSpace() ? "anonymous" : value;
        }
        set => this["User"] = value;
    }

    /// <summary>Basic 认证密码</summary>
    public String Password
    {
        get => GetString("Password") ?? String.Empty;
        set => this["Password"] = value;
    }

    /// <summary>是否使用 HTTPS</summary>
    public Boolean UseSsl
    {
        get => GetBoolean("UseSsl");
        set => this["UseSsl"] = value;
    }

    /// <summary>是否放宽服务端证书校验。仅用于自签名证书与内网环境</summary>
    public Boolean TrustServerCertificate
    {
        get => GetBoolean("TrustServerCertificate");
        set => this["TrustServerCertificate"] = value;
    }

    /// <summary>认证模式。未显式指定时按 AccessToken/Password 推断</summary>
    public PrestoAuth Auth
    {
        get
        {
            var value = GetString("Auth");
            if (value.IsNullOrEmpty())
            {
                if (!AccessToken.IsNullOrEmpty()) return PrestoAuth.Jwt;
                return Password.IsNullOrEmpty() ? PrestoAuth.None : PrestoAuth.Basic;
            }

            if (value.EqualIgnoreCase("None", "Anonymous")) return PrestoAuth.None;
            if (value.EqualIgnoreCase("Basic")) return PrestoAuth.Basic;
            if (value.EqualIgnoreCase("JWT", "Bearer", "Token")) return PrestoAuth.Jwt;
            if (value.EqualIgnoreCase("OAuth2", "OAuth", "ClientCredentials", "ClientCredentialsFlow")) return PrestoAuth.OAuth2;

            throw new ArgumentException(Localization.Translate("Unsupported authentication mode: {0}.", value), nameof(Auth));
        }
        set => this["Auth"] = value.ToString();
    }

    /// <summary>JWT/Bearer 令牌</summary>
    public String AccessToken
    {
        get => GetString("AccessToken") ?? String.Empty;
        set => this["AccessToken"] = value;
    }

    /// <summary>OAuth2 令牌端点。Auth=OAuth2 时必填</summary>
    public String TokenEndpoint
    {
        get => GetString("TokenEndpoint") ?? String.Empty;
        set => this["TokenEndpoint"] = value;
    }

    /// <summary>OAuth2 客户端标识。Auth=OAuth2 时必填</summary>
    public String ClientId
    {
        get => GetString("ClientId") ?? String.Empty;
        set => this["ClientId"] = value;
    }

    /// <summary>OAuth2 客户端密钥</summary>
    public String ClientSecret
    {
        get => GetString("ClientSecret") ?? String.Empty;
        set => this["ClientSecret"] = value;
    }

    /// <summary>OAuth2 授权范围，空格分隔</summary>
    public String Scope
    {
        get => GetString("Scope") ?? String.Empty;
        set => this["Scope"] = value;
    }

    /// <summary>复杂类型取值模式。默认 String，可选 Structured</summary>
    public PrestoComplexType ComplexTypes
    {
        get
        {
            var value = GetString("ComplexTypes");
            if (value.IsNullOrEmpty() || value.EqualIgnoreCase("String", "Text", "Raw")) return PrestoComplexType.String;
            if (value.EqualIgnoreCase("Structured", "Object", "Parse")) return PrestoComplexType.Structured;

            throw new ArgumentException(Localization.Translate("Unsupported complex type mode: {0}. Use String or Structured.", value), nameof(ComplexTypes));
        }
        set => this["ComplexTypes"] = value.ToString();
    }

    /// <summary>协议类型。默认 PrestoDB</summary>
    public PrestoProtocol Protocol
    {
        get
        {
            var value = GetString("Protocol");
            if (value.IsNullOrEmpty()) return PrestoProtocol.Presto;
            if (value.EqualIgnoreCase("Presto", "PrestoDB", "PrestoDb")) return PrestoProtocol.Presto;
            if (value.EqualIgnoreCase("Trino")) return PrestoProtocol.Trino;

            throw new ArgumentException(Localization.Translate("Unsupported protocol: {0}. Use Presto or Trino.", value), nameof(Protocol));
        }
        set => this["Protocol"] = value.ToString();
    }

    /// <summary>会话时区。默认 UTC</summary>
    public String TimeZone
    {
        get
        {
            var value = GetString("TimeZone");
            return value.IsNullOrWhiteSpace() ? "UTC" : value.Trim();
        }
        set => this["TimeZone"] = value;
    }

    /// <summary>请求头语言</summary>
    public String Language
    {
        get => GetString("Language") ?? String.Empty;
        set => this["Language"] = value;
    }

    /// <summary>客户端来源标识</summary>
    public String Source
    {
        get
        {
            var value = GetString("Source");
            return value.IsNullOrWhiteSpace() ? "NewLife.Presto" : value.Trim();
        }
        set => this["Source"] = value;
    }

    /// <summary>客户端标签，逗号分隔</summary>
    public String ClientTags
    {
        get => GetString("ClientTags") ?? String.Empty;
        set => this["ClientTags"] = value;
    }

    /// <summary>客户端信息</summary>
    public String ClientInfo
    {
        get => GetString("ClientInfo") ?? String.Empty;
        set => this["ClientInfo"] = value;
    }

    /// <summary>资源组</summary>
    public String ResourceGroup
    {
        get => GetString("ResourceGroup") ?? String.Empty;
        set => this["ResourceGroup"] = value;
    }

    /// <summary>链路令牌</summary>
    public String TraceToken
    {
        get => GetString("TraceToken") ?? String.Empty;
        set => this["TraceToken"] = value;
    }

    /// <summary>角色</summary>
    public String Role
    {
        get => GetString("Role") ?? String.Empty;
        set => this["Role"] = value;
    }

    /// <summary>网关路径前缀，如 /trino</summary>
    public String Path
    {
        get => GetString("Path") ?? String.Empty;
        set => this["Path"] = value;
    }

    /// <summary>HTTP 代理地址</summary>
    public String Proxy
    {
        get => GetString("Proxy") ?? String.Empty;
        set => this["Proxy"] = value;
    }

    /// <summary>命令超时秒数。0 表示不限制</summary>
    public Int32 CommandTimeout
    {
        get
        {
            var value = GetString("CommandTimeout");
            if (value.IsNullOrEmpty()) return 0;
            if (!Int32.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var timeout) || timeout < 0)
                throw new ArgumentException(Localization.Translate("Command timeout cannot be negative."), nameof(CommandTimeout));

            return timeout;
        }
        set
        {
            if (value < 0) throw new ArgumentException(Localization.Translate("Command timeout cannot be negative."), nameof(CommandTimeout));

            this["CommandTimeout"] = value;
        }
    }

    /// <summary>HTTP 请求超时秒数。默认 100</summary>
    public Int32 ConnectionTimeout
    {
        get
        {
            var value = GetString("ConnectionTimeout");
            if (value.IsNullOrEmpty()) return 100;
            if (!Int32.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var timeout) || timeout <= 0)
                throw new ArgumentException(Localization.Translate("Connection timeout must be greater than zero."), nameof(ConnectionTimeout));

            return timeout;
        }
        set
        {
            if (value <= 0) throw new ArgumentException(Localization.Translate("Connection timeout must be greater than zero."), nameof(ConnectionTimeout));

            this["ConnectionTimeout"] = value;
        }
    }

    /// <summary>瞬态错误最大重试次数。默认 3</summary>
    public Int32 MaxRetries
    {
        get
        {
            var value = GetString("MaxRetries");
            if (value.IsNullOrEmpty()) return 3;
            if (!Int32.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var retries) || retries < 0)
                throw new ArgumentException(Localization.Translate("Max retries cannot be negative."), nameof(MaxRetries));

            return retries;
        }
        set
        {
            if (value < 0) throw new ArgumentException(Localization.Translate("Max retries cannot be negative."), nameof(MaxRetries));

            this["MaxRetries"] = value;
        }
    }

    /// <summary>初始重试间隔毫秒数。默认 200</summary>
    public Int32 RetryInterval
    {
        get
        {
            var value = GetString("RetryInterval");
            if (value.IsNullOrEmpty()) return 200;
            if (!Int32.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var interval) || interval < 0)
                throw new ArgumentException(Localization.Translate("Retry interval cannot be negative."), nameof(RetryInterval));

            return interval;
        }
        set
        {
            if (value < 0) throw new ArgumentException(Localization.Translate("Retry interval cannot be negative."), nameof(RetryInterval));

            this["RetryInterval"] = value;
        }
    }

    /// <summary>Open 时是否请求服务端信息验活</summary>
    public Boolean TestConnection
    {
        get => GetBoolean("TestConnection");
        set => this["TestConnection"] = value;
    }

    /// <summary>是否请求并接受压缩响应。默认关闭</summary>
    /// <remarks>
    /// 压缩省带宽但要多花解压时间：回环/同区网络下，15000 行扫描约多 4–6 ms；
    /// 跨广域网、带宽受限时收益明显，可显式设置 <c>Compression=true</c>。
    /// </remarks>
    public Boolean Compression
    {
        get => !GetString("Compression").IsNullOrEmpty() && GetBoolean("Compression");
        set => this["Compression"] = value;
    }

    /// <summary>是否启用 Trino Spooling 协议。默认关闭，仅支持未压缩 JSON 编码</summary>
    public Boolean Spooling
    {
        get => GetBoolean("Spooling");
        set => this["Spooling"] = value;
    }

    /// <summary>会话属性。格式 key=value，多项以 | 分隔</summary>
    public IDictionary<String, String> SessionProperties => ParsePairs("SessionProperties", '=', "Invalid session property format: {0}. Expected key=value.");

    /// <summary>自定义请求头。格式 name:value，多项以 | 分隔</summary>
    public IDictionary<String, String> CustomHeaders
    {
        get
        {
            var headers = ParsePairs("CustomHeaders", ':', "Invalid custom header format: {0}. Expected name:value.");
            foreach (var item in headers)
            {
                if (PrestoHeaders.IsManaged(item.Key))
                    throw new ArgumentException(Localization.Translate("Custom header '{0}' is managed by the driver and cannot be overridden.", item.Key), nameof(CustomHeaders));
            }

            return headers;
        }
    }
    #endregion

    #region 方法
    /// <summary>校验连接字符串。读取全部强类型属性，非法值抛出异常</summary>
    public void Validate()
    {
        _ = Host;
        _ = Port;
        _ = Auth;
        _ = Protocol;
        _ = CommandTimeout;
        _ = ConnectionTimeout;
        _ = MaxRetries;
        _ = RetryInterval;
        _ = SessionProperties;
        _ = CustomHeaders;
        _ = ComplexTypes;
        _ = Spooling;

        if (Spooling && Protocol == PrestoProtocol.Presto)
            throw new ArgumentException(Localization.Translate("Spooling is only supported with the Trino protocol."), nameof(Spooling));

        if (Auth == PrestoAuth.Jwt && AccessToken.IsNullOrEmpty())
            throw new ArgumentException(Localization.Translate("AccessToken is required when Auth=JWT."), nameof(AccessToken));

        if (Auth == PrestoAuth.OAuth2)
        {
            if (TokenEndpoint.IsNullOrEmpty())
                throw new ArgumentException(Localization.Translate("OAuth2 authentication requires a token endpoint."), nameof(TokenEndpoint));
            if (ClientId.IsNullOrEmpty())
                throw new ArgumentException(Localization.Translate("OAuth2 authentication requires a client id."), nameof(ClientId));
        }
    }

    private void Normalize()
    {
        // 归一化可能经由任意属性访问触发，防止重入；一次赋值后只归一化一次
        if (_normalizing || _normalized) return;
        _normalizing = true;
        try
        {
            Move(_hostKeys, "Host");
            Move(_catalogKeys, "Catalog");
            Move(_schemaKeys, "Schema");
            Move(_userKeys, "User");
            Move(_passwordKeys, "Password");
            Move(_sslKeys, "UseSsl");
            Move(_trustKeys, "TrustServerCertificate");
            Move(_tokenKeys, "AccessToken");
            Move(_tokenEndpointKeys, "TokenEndpoint");
            Move(_clientIdKeys, "ClientId");
            Move(_clientSecretKeys, "ClientSecret");
            Move(_scopeKeys, "Scope");
            Move(_complexTypesKeys, "ComplexTypes");
            Move(_spoolingKeys, "Spooling");
            Move(_sourceKeys, "Source");
            Move(_pathKeys, "Path");
            Move(_proxyKeys, "Proxy");

            // TrinoHeaders 是常见写法，归一化为 Protocol
            if (ContainsKey("TrinoHeaders"))
            {
                var trino = PeekString("TrinoHeaders").ToBoolean();
                if (trino && !ContainsKey("Protocol")) this["Protocol"] = "Trino";
                Remove("TrinoHeaders");
            }
            _normalized = true;
        }
        finally
        {
            _normalizing = false;
        }
    }

    private void Move(String[] keys, String canonical)
    {
        foreach (var key in keys)
        {
            if (key.EqualIgnoreCase(canonical) || !ContainsKey(key)) continue;

            if (!ContainsKey(canonical) && TryGetValue(key, out var value) && value != null) this[canonical] = value;
            Remove(key);
        }
    }

    private String? GetString(String key)
    {
        Normalize();

        return PeekString(key);
    }

    private String? PeekString(String key) => TryGetValue(key, out var value) && value != null
        ? Convert.ToString(value, CultureInfo.InvariantCulture)
        : null;

    private Boolean _normalizing;
    private Boolean _normalized;

    private Boolean GetBoolean(String key) => GetString(key).ToBoolean();

    private IDictionary<String, String> ParsePairs(String key, Char separator, String errorTemplate)
    {
        var result = new Dictionary<String, String>(StringComparer.OrdinalIgnoreCase);
        var text = GetString(key);
        if (text.IsNullOrWhiteSpace()) return result;

        foreach (var item in text.Split('|'))
        {
            if (item.IsNullOrWhiteSpace()) continue;

            var index = item.IndexOf(separator);
            if (index <= 0) throw new ArgumentException(Localization.Translate(errorTemplate, item.Trim()), key);

            result[item[..index].Trim()] = item[(index + 1)..].Trim();
        }

        return result;
    }
    #endregion
}
