using NewLife;
using NewLife.Presto;

namespace UnitTest;

/// <summary>连接字符串构建器测试。覆盖别名归一化、默认值与非法值</summary>
public class PrestoConnectionStringBuilderTests
{
    /// <summary>默认值</summary>
    [Fact(DisplayName = "连接字符串默认值")]
    public void Defaults()
    {
        var builder = new PrestoConnectionStringBuilder("Host=localhost");

        Assert.Equal("localhost", builder.Host);
        Assert.Equal(8080, builder.Port);
        Assert.Equal(PrestoProtocol.Presto, builder.Protocol);
        Assert.Equal("UTC", builder.TimeZone);
        Assert.Equal("NewLife.Presto", builder.Source);
        Assert.Equal(0, builder.CommandTimeout);
        Assert.Equal(100, builder.ConnectionTimeout);
        Assert.Equal(3, builder.MaxRetries);
        Assert.Equal(200, builder.RetryInterval);
        Assert.False(builder.UseSsl);
        Assert.False(builder.TrustServerCertificate);
        Assert.False(builder.TestConnection);
        Assert.True(builder.Compression);
    }

    /// <summary>XCode 写法归一化</summary>
    [Fact(DisplayName = "XCode 写法归一化")]
    public void XCodeAliasesAreNormalized()
    {
        var builder = new PrestoConnectionStringBuilder("Server=presto.local;Port=8081;Database=hive;Owner=default;User ID=test;Pwd=secret;UseSsl=true");

        Assert.Equal("presto.local", builder.Host);
        Assert.Equal(8081, builder.Port);
        Assert.Equal("hive", builder.Catalog);
        Assert.Equal("default", builder.Schema);
        Assert.Equal("test", builder.User);
        Assert.Equal("secret", builder.Password);
        Assert.True(builder.UseSsl);
        Assert.DoesNotContain("Database", builder.ConnectionString);
        Assert.DoesNotContain("Owner", builder.ConnectionString);
    }

    /// <summary>TrinoHeaders 归一化为协议</summary>
    [Fact(DisplayName = "TrinoHeaders 归一化为协议")]
    public void TrinoHeadersMapsToProtocol()
    {
        var builder = new PrestoConnectionStringBuilder("Host=localhost;TrinoHeaders=1");

        Assert.Equal(PrestoProtocol.Trino, builder.Protocol);
    }

    /// <summary>认证模式推断</summary>
    [Fact(DisplayName = "认证模式推断")]
    public void AuthIsInferred()
    {
        Assert.Equal(PrestoAuth.None, new PrestoConnectionStringBuilder("Host=localhost;User=test").Auth);
        Assert.Equal(PrestoAuth.Basic, new PrestoConnectionStringBuilder("Host=localhost;User=test;Password=secret").Auth);
        Assert.Equal(PrestoAuth.Jwt, new PrestoConnectionStringBuilder("Host=localhost;User=test;AccessToken=token").Auth);
    }

    /// <summary>会话属性与自定义头解析</summary>
    [Fact(DisplayName = "会话属性与自定义头解析")]
    public void ParsesPairs()
    {
        var builder = new PrestoConnectionStringBuilder("Host=localhost;SessionProperties=query_max_run_time=1h|task_concurrency=8;CustomHeaders=X-Tenant:team-a|X-Trace:on");

        Assert.Equal("1h", builder.SessionProperties["query_max_run_time"]);
        Assert.Equal("8", builder.SessionProperties["task_concurrency"]);
        Assert.Equal("team-a", builder.CustomHeaders["X-Tenant"]);
        Assert.Equal("on", builder.CustomHeaders["X-Trace"]);
    }

    /// <summary>受管请求头不得覆盖</summary>
    [Fact(DisplayName = "受管请求头不得覆盖")]
    public void ManagedHeadersAreRejected()
    {
        var builder = new PrestoConnectionStringBuilder("Host=localhost;CustomHeaders=X-Presto-User:evil");
        var ex = Assert.Throws<ArgumentException>(() => _ = builder.CustomHeaders);
        Assert.Contains("X-Presto-User", ex.Message);
    }

    /// <summary>非法值抛出异常</summary>
    [Theory(DisplayName = "非法值抛出异常")]
    [InlineData("Host=localhost;Port=0")]
    [InlineData("Host=localhost;Port=70000")]
    [InlineData("Host=localhost;CommandTimeout=-1")]
    [InlineData("Host=localhost;ConnectionTimeout=0")]
    [InlineData("Host=localhost;MaxRetries=-1")]
    [InlineData("Host=localhost;RetryInterval=-1")]
    [InlineData("Host=localhost;Auth=OAuth2")]
    [InlineData("Host=localhost;Protocol=Hive")]
    [InlineData("Host=localhost;SessionProperties=broken")]
    [InlineData("Host=localhost;CustomHeaders=broken")]
    public void InvalidValuesThrow(String connectionString)
    {
        var builder = new PrestoConnectionStringBuilder(connectionString);

        Assert.ThrowsAny<ArgumentException>(() => builder.Validate());
    }

    /// <summary>JWT 模式缺令牌时报错</summary>
    [Fact(DisplayName = "JWT 模式缺令牌时报错")]
    public void JwtRequiresToken()
    {
        var builder = new PrestoConnectionStringBuilder("Host=localhost;Auth=JWT");

        var ex = Assert.Throws<ArgumentException>(() => builder.Validate());
        Assert.Contains("AccessToken", ex.Message);
    }

    /// <summary>用户名缺省时回退</summary>
    [Fact(DisplayName = "用户名缺省时回退")]
    public void UserFallsBack()
    {
        var builder = new PrestoConnectionStringBuilder("Host=localhost");

        Assert.False(builder.User.IsNullOrEmpty());
    }

    /// <summary>主机别名归一化</summary>
    [Theory(DisplayName = "主机别名归一化")]
    [InlineData("Host=h1", "h1")]
    [InlineData("Server=h2", "h2")]
    [InlineData("Data Source=h3", "h3")]
    [InlineData("DataSource=h4", "h4")]
    public void HostAliases(String text, String expected)
    {
        var builder = new PrestoConnectionStringBuilder(text);

        Assert.Equal(expected, builder.Host);
        Assert.False(builder.ContainsKey("Server"));
        Assert.False(builder.ContainsKey("Data Source"));
        Assert.False(builder.ContainsKey("DataSource"));
    }

    /// <summary>Catalog 别名归一化</summary>
    [Theory(DisplayName = "Catalog 别名归一化")]
    [InlineData("Catalog=hive", "hive")]
    [InlineData("Database=hive", "hive")]
    [InlineData("Initial Catalog=hive", "hive")]
    public void CatalogAliases(String text, String expected)
    {
        var builder = new PrestoConnectionStringBuilder(text);

        Assert.Equal(expected, builder.Catalog);
        Assert.False(builder.ContainsKey("Database"));
        Assert.False(builder.ContainsKey("Initial Catalog"));
    }

    /// <summary>Schema 别名归一化</summary>
    [Theory(DisplayName = "Schema 别名归一化")]
    [InlineData("Schema=default", "default")]
    [InlineData("Owner=default", "default")]
    public void SchemaAliases(String text, String expected)
    {
        var builder = new PrestoConnectionStringBuilder(text);

        Assert.Equal(expected, builder.Schema);
        Assert.False(builder.ContainsKey("Owner"));
    }

    /// <summary>用户与密码别名归一化</summary>
    [Theory(DisplayName = "用户与密码别名归一化")]
    [InlineData("User=test", "test", "")]
    [InlineData("User ID=test", "test", "")]
    [InlineData("UserID=test", "test", "")]
    [InlineData("Username=test", "test", "")]
    [InlineData("UID=test", "test", "")]
    [InlineData("User=test;Pwd=secret", "test", "secret")]
    [InlineData("User=test;Password=secret", "test", "secret")]
    public void CredentialAliases(String text, String user, String password)
    {
        var builder = new PrestoConnectionStringBuilder(text);

        Assert.Equal(user, builder.User);
        Assert.Equal(password, builder.Password);
        Assert.False(builder.ContainsKey("Pwd"));
    }

    /// <summary>TLS 与令牌别名归一化</summary>
    [Theory(DisplayName = "TLS 与令牌别名归一化")]
    [InlineData("UseSsl=true")]
    [InlineData("UseSSL=true")]
    [InlineData("EnableSSL=true")]
    [InlineData("SSL=true")]
    public void SslAliases(String text)
    {
        var builder = new PrestoConnectionStringBuilder(text);

        Assert.True(builder.UseSsl);
    }

    /// <summary>证书校验别名归一化</summary>
    [Theory(DisplayName = "证书校验别名归一化")]
    [InlineData("TrustServerCertificate=true")]
    [InlineData("IgnoreSslErrors=true")]
    public void TrustAliases(String text)
    {
        var builder = new PrestoConnectionStringBuilder(text);

        Assert.True(builder.TrustServerCertificate);
    }

    /// <summary>令牌别名归一化</summary>
    [Theory(DisplayName = "令牌别名归一化")]
    [InlineData("AccessToken=abc")]
    [InlineData("Token=abc")]
    public void TokenAliases(String text)
    {
        var builder = new PrestoConnectionStringBuilder(text);

        Assert.Equal("abc", builder.AccessToken);
        Assert.False(builder.ContainsKey("Token"));
    }

    /// <summary>来源、路径与代理别名归一化</summary>
    [Fact(DisplayName = "来源、路径与代理别名归一化")]
    public void OtherAliases()
    {
        var builder = new PrestoConnectionStringBuilder("ApplicationName=bi;BasePath=/trino;HttpProxy=http://proxy:8080");

        Assert.Equal("bi", builder.Source);
        Assert.Equal("/trino", builder.Path);
        Assert.Equal("http://proxy:8080", builder.Proxy);
        Assert.False(builder.ContainsKey("ApplicationName"));
        Assert.False(builder.ContainsKey("BasePath"));
        Assert.False(builder.ContainsKey("HttpProxy"));
    }

    /// <summary>协议别名与 TrinoHeaders 归一化</summary>
    [Theory(DisplayName = "协议别名归一化")]
    [InlineData("Protocol=Presto", PrestoProtocol.Presto)]
    [InlineData("Protocol=PrestoDB", PrestoProtocol.Presto)]
    [InlineData("Protocol=PrestoDb", PrestoProtocol.Presto)]
    [InlineData("Protocol=Trino", PrestoProtocol.Trino)]
    [InlineData("TrinoHeaders=true", PrestoProtocol.Trino)]
    [InlineData("TrinoHeaders=1", PrestoProtocol.Trino)]
    [InlineData("TrinoHeaders=false", PrestoProtocol.Presto)]
    public void ProtocolAliases(String text, PrestoProtocol expected)
    {
        var builder = new PrestoConnectionStringBuilder(text);

        Assert.Equal(expected, builder.Protocol);
        Assert.False(builder.ContainsKey("TrinoHeaders"));
    }

    /// <summary>认证别名归一化</summary>
    [Theory(DisplayName = "认证别名归一化")]
    [InlineData("Auth=None", PrestoAuth.None)]
    [InlineData("Auth=Anonymous", PrestoAuth.None)]
    [InlineData("Auth=Basic", PrestoAuth.Basic)]
    [InlineData("Auth=JWT", PrestoAuth.Jwt)]
    [InlineData("Auth=Bearer", PrestoAuth.Jwt)]
    [InlineData("Auth=Token", PrestoAuth.Jwt)]
    public void AuthAliases(String text, PrestoAuth expected)
    {
        var builder = new PrestoConnectionStringBuilder(text);

        Assert.Equal(expected, builder.Auth);
    }

    /// <summary>属性读写往返</summary>
    [Fact(DisplayName = "属性读写往返")]
    public void PropertyRoundTrip()
    {
        var builder = new PrestoConnectionStringBuilder
        {
            Host = "presto.local",
            Port = 8443,
            Catalog = "hive",
            Schema = "default",
            User = "test",
            Password = "secret",
            UseSsl = true,
            TrustServerCertificate = true,
            Auth = PrestoAuth.Jwt,
            AccessToken = "token",
            Protocol = PrestoProtocol.Trino,
            TimeZone = "Asia/Shanghai",
            Language = "zh",
            Source = "bi",
            ClientTags = "a,b",
            ClientInfo = "info",
            ResourceGroup = "rg",
            TraceToken = "trace",
            Path = "/trino",
            Proxy = "http://proxy:8080",
            CommandTimeout = 10,
            ConnectionTimeout = 20,
            MaxRetries = 1,
            RetryInterval = 50,
            TestConnection = true,
            Compression = false,
        };

        Assert.Equal("presto.local", builder.Host);
        Assert.Equal(8443, builder.Port);
        Assert.Equal("hive", builder.Catalog);
        Assert.Equal("default", builder.Schema);
        Assert.Equal("test", builder.User);
        Assert.Equal("secret", builder.Password);
        Assert.True(builder.UseSsl);
        Assert.True(builder.TrustServerCertificate);
        Assert.Equal(PrestoAuth.Jwt, builder.Auth);
        Assert.Equal("token", builder.AccessToken);
        Assert.Equal(PrestoProtocol.Trino, builder.Protocol);
        Assert.Equal("Asia/Shanghai", builder.TimeZone);
        Assert.Equal("zh", builder.Language);
        Assert.Equal("bi", builder.Source);
        Assert.Equal("a,b", builder.ClientTags);
        Assert.Equal("info", builder.ClientInfo);
        Assert.Equal("rg", builder.ResourceGroup);
        Assert.Equal("trace", builder.TraceToken);
        Assert.Equal("/trino", builder.Path);
        Assert.Equal("http://proxy:8080", builder.Proxy);
        Assert.Equal(10, builder.CommandTimeout);
        Assert.Equal(20, builder.ConnectionTimeout);
        Assert.Equal(1, builder.MaxRetries);
        Assert.Equal(50, builder.RetryInterval);
        Assert.True(builder.TestConnection);
        Assert.False(builder.Compression);
    }

    /// <summary>端口边界值</summary>
    [Theory(DisplayName = "端口边界值")]
    [InlineData(1)]
    [InlineData(65535)]
    public void PortBoundaries(Int32 port)
    {
        var builder = new PrestoConnectionStringBuilder { Port = port };

        Assert.Equal(port, builder.Port);
    }

    /// <summary>超时与重试边界值</summary>
    [Fact(DisplayName = "超时与重试边界值")]
    public void TimeoutBoundaries()
    {
        var builder = new PrestoConnectionStringBuilder
        {
            CommandTimeout = 0,
            ConnectionTimeout = 1,
            MaxRetries = 0,
            RetryInterval = 0,
        };

        Assert.Equal(0, builder.CommandTimeout);
        Assert.Equal(1, builder.ConnectionTimeout);
        Assert.Equal(0, builder.MaxRetries);
        Assert.Equal(0, builder.RetryInterval);
    }

    /// <summary>会话属性去空白与空段</summary>
    [Fact(DisplayName = "会话属性去空白与空段")]
    public void SessionPropertiesAreTrimmed()
    {
        var builder = new PrestoConnectionStringBuilder("Host=localhost;SessionProperties= a = 1 || b=2 |");

        var properties = builder.SessionProperties;
        Assert.Equal("1", properties["a"]);
        Assert.Equal("2", properties["b"]);
        Assert.Equal(2, properties.Count);
    }

    /// <summary>自定义头去空白与空段</summary>
    [Fact(DisplayName = "自定义头去空白与空段")]
    public void CustomHeadersAreTrimmed()
    {
        var builder = new PrestoConnectionStringBuilder("Host=localhost;CustomHeaders= X-Tenant : team-a || X-Trace:on |");

        var headers = builder.CustomHeaders;
        Assert.Equal("team-a", headers["X-Tenant"]);
        Assert.Equal("on", headers["X-Trace"]);
        Assert.Equal(2, headers.Count);
    }

    /// <summary>受管请求头清单</summary>
    [Theory(DisplayName = "受管请求头清单")]
    [InlineData("Authorization")]
    [InlineData("User-Agent")]
    [InlineData("Content-Type")]
    [InlineData("Content-Length")]
    [InlineData("Accept")]
    [InlineData("Accept-Encoding")]
    [InlineData("Host")]
    [InlineData("X-Presto-User")]
    [InlineData("X-Trino-Session")]
    public void ManagedHeaderVariants(String name)
    {
        var builder = new PrestoConnectionStringBuilder($"Host=localhost;CustomHeaders={name}:value");

        var ex = Assert.Throws<ArgumentException>(() => _ = builder.CustomHeaders);
        Assert.Contains(name, ex.Message);
    }

    /// <summary>通过基类赋值仍然归一化</summary>
    [Fact(DisplayName = "通过基类赋值仍然归一化")]
    public void NormalizesWhenAssignedThroughBaseType()
    {
        System.Data.Common.DbConnectionStringBuilder baseBuilder = new PrestoConnectionStringBuilder();
        baseBuilder.ConnectionString = "Server=presto.local;Database=hive;Owner=default";

        var builder = (PrestoConnectionStringBuilder)baseBuilder;
        Assert.Equal("presto.local", builder.Host);
        Assert.Equal("hive", builder.Catalog);
        Assert.Equal("default", builder.Schema);
    }

    /// <summary>未知键保留</summary>
    [Fact(DisplayName = "未知键保留")]
    public void UnknownKeysArePreserved()
    {
        var builder = new PrestoConnectionStringBuilder("Host=localhost;Custom=1");

        Assert.Equal("1", Convert.ToString(builder["Custom"]));
    }

    /// <summary>压缩与探活开关</summary>
    [Theory(DisplayName = "压缩与探活开关")]
    [InlineData("Compression=true", true)]
    [InlineData("Compression=false", false)]
    [InlineData("TestConnection=true", true)]
    [InlineData("TestConnection=false", false)]
    public void BooleanSwitches(String text, Boolean expected)
    {
        var builder = new PrestoConnectionStringBuilder(text);

        var actual = text.StartsWith("Compression") ? builder.Compression : builder.TestConnection;
        Assert.Equal(expected, actual);
    }

    /// <summary>完整校验通过</summary>
    [Theory(DisplayName = "完整校验通过")]
    [InlineData("Host=localhost;User=test")]
    [InlineData("Host=localhost;User=test;Password=secret")]
    [InlineData("Host=localhost;User=test;Auth=JWT;AccessToken=token")]
    public void ValidateSucceeds(String connectionString)
    {
        var builder = new PrestoConnectionStringBuilder(connectionString);

        builder.Validate();
    }

    /// <summary>属性设置非法值抛出异常</summary>
    [Fact(DisplayName = "属性设置非法值抛出异常")]
    public void SettersValidate()
    {
        var builder = new PrestoConnectionStringBuilder();

        Assert.Throws<ArgumentException>(() => builder.Host = " ");
        Assert.Throws<ArgumentException>(() => builder.Port = 0);
        Assert.Throws<ArgumentException>(() => builder.Port = 65536);
        Assert.Throws<ArgumentException>(() => builder.CommandTimeout = -1);
        Assert.Throws<ArgumentException>(() => builder.ConnectionTimeout = 0);
        Assert.Throws<ArgumentException>(() => builder.MaxRetries = -1);
        Assert.Throws<ArgumentException>(() => builder.RetryInterval = -1);
    }
}
