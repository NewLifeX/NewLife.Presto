using NewLife;
using NewLife.Presto;

namespace UnitTest;

/// <summary>连接字符串构建器边界与分支测试。补充默认值、原始非法值、别名优先级与解析边界</summary>
public class PrestoConnectionStringBuilderEdgeTests
{
    /// <summary>未设置的字符串属性返回空串</summary>
    [Fact(DisplayName = "未设置的字符串属性返回空串")]
    public void UnsetStringsReturnEmpty()
    {
        var builder = new PrestoConnectionStringBuilder();

        Assert.Equal("localhost", builder.Host);
        Assert.Equal(String.Empty, builder.Catalog);
        Assert.Equal(String.Empty, builder.Schema);
        Assert.Equal(String.Empty, builder.Password);
        Assert.Equal(String.Empty, builder.AccessToken);
        Assert.Equal(String.Empty, builder.Language);
        Assert.Equal(String.Empty, builder.ClientTags);
        Assert.Equal(String.Empty, builder.ClientInfo);
        Assert.Equal(String.Empty, builder.ResourceGroup);
        Assert.Equal(String.Empty, builder.TraceToken);
        Assert.Equal(String.Empty, builder.Path);
        Assert.Equal(String.Empty, builder.Proxy);
    }

    /// <summary>连接字符串赋 null 或空串等价于空配置</summary>
    [Fact(DisplayName = "连接字符串赋 null 或空串等价于空配置")]
    public void NullConnectionStringIsEmpty()
    {
        var builder = new PrestoConnectionStringBuilder { ConnectionString = null! };

        Assert.Equal(String.Empty, builder.ConnectionString);
        Assert.False(builder.ContainsKey("Host"));

        builder.ConnectionString = "";
        Assert.Equal(String.Empty, builder.ConnectionString);
    }

    /// <summary>绕过属性设置器写入原始非法值，读取时校验</summary>
    [Fact(DisplayName = "绕过属性设置器写入原始非法值")]
    public void RawInvalidValuesAreValidatedOnRead()
    {
        var host = new PrestoConnectionStringBuilder();
        host["Host"] = "   ";
        Assert.Throws<ArgumentException>(() => _ = host.Host);

        foreach (var value in new[] { "abc", "0", "70000", "-1" })
        {
            var builder = new PrestoConnectionStringBuilder();
            builder["Port"] = value;
            Assert.Throws<ArgumentException>(() => _ = builder.Port);
        }

        foreach (var (key, read) in new (String, Func<PrestoConnectionStringBuilder, Int32>)[]
        {
            ("CommandTimeout", e => e.CommandTimeout),
            ("ConnectionTimeout", e => e.ConnectionTimeout),
            ("MaxRetries", e => e.MaxRetries),
            ("RetryInterval", e => e.RetryInterval),
        })
        {
            foreach (var value in new[] { "abc", "-1" })
            {
                var builder = new PrestoConnectionStringBuilder();
                builder[key] = value;
                Assert.Throws<ArgumentException>(() => read(builder));
            }
        }
    }

    /// <summary>原始空串数值键使用默认值</summary>
    [Fact(DisplayName = "原始空串数值键使用默认值")]
    public void EmptyNumericValuesUseDefaults()
    {
        var builder = new PrestoConnectionStringBuilder();
        builder["Port"] = "";
        builder["CommandTimeout"] = "";
        builder["ConnectionTimeout"] = "";
        builder["MaxRetries"] = "";
        builder["RetryInterval"] = "";

        Assert.Equal(8080, builder.Port);
        Assert.Equal(0, builder.CommandTimeout);
        Assert.Equal(100, builder.ConnectionTimeout);
        Assert.Equal(3, builder.MaxRetries);
        Assert.Equal(200, builder.RetryInterval);
    }

    /// <summary>规范键优先于别名，别名仍会被移除</summary>
    [Fact(DisplayName = "规范键优先于别名")]
    public void CanonicalKeysWinOverAliases()
    {
        var builder = new PrestoConnectionStringBuilder("Host=primary;Server=secondary;Catalog=hive;Database=other;Schema=a;Owner=b;User=u1;UserID=u2;Password=p1;Pwd=p2;UseSsl=false;SSL=true;AccessToken=t1;Token=t2;Source=s1;ApplicationName=s2;Path=/p1;BasePath=/p2;Proxy=x1;HttpProxy=x2");

        Assert.Equal("primary", builder.Host);
        Assert.Equal("hive", builder.Catalog);
        Assert.Equal("a", builder.Schema);
        Assert.Equal("u1", builder.User);
        Assert.Equal("p1", builder.Password);
        Assert.False(builder.UseSsl);
        Assert.Equal("t1", builder.AccessToken);
        Assert.Equal("s1", builder.Source);
        Assert.Equal("/p1", builder.Path);
        Assert.Equal("x1", builder.Proxy);

        foreach (var key in new[] { "Server", "Database", "Owner", "UserID", "Pwd", "SSL", "Token", "ApplicationName", "BasePath", "HttpProxy" })
        {
            Assert.False(builder.ContainsKey(key), key);
        }
    }

    /// <summary>别名大小写与首尾空白不敏感</summary>
    [Fact(DisplayName = "别名大小写与空白不敏感")]
    public void AliasesAreCaseInsensitiveAndTrimmed()
    {
        var builder = new PrestoConnectionStringBuilder(" server = presto.local ; DATABASE = hive ; owner = default ; user id = test ");

        Assert.Equal("presto.local", builder.Host);
        Assert.Equal("hive", builder.Catalog);
        Assert.Equal("default", builder.Schema);
        Assert.Equal("test", builder.User);
    }

    /// <summary>TrinoHeaders 不覆盖显式协议</summary>
    [Fact(DisplayName = "TrinoHeaders 不覆盖显式协议")]
    public void TrinoHeadersDoesNotOverrideProtocol()
    {
        var builder = new PrestoConnectionStringBuilder("Protocol=Presto;TrinoHeaders=true");

        Assert.Equal(PrestoProtocol.Presto, builder.Protocol);
        Assert.False(builder.ContainsKey("TrinoHeaders"));
    }

    /// <summary>TrinoHeaders 空值与假值不改变协议</summary>
    [Theory(DisplayName = "TrinoHeaders 空值与假值不改变协议")]
    [InlineData("")]
    [InlineData("false")]
    [InlineData("0")]
    public void TrinoHeadersFalseVariants(String value)
    {
        var builder = new PrestoConnectionStringBuilder($"TrinoHeaders={value}");

        Assert.Equal(PrestoProtocol.Presto, builder.Protocol);
    }

    /// <summary>会话属性缺失、空白与重复键</summary>
    [Fact(DisplayName = "会话属性缺失、空白与重复键")]
    public void SessionPropertyEdgeCases()
    {
        Assert.Empty(new PrestoConnectionStringBuilder().SessionProperties);

        var builder = new PrestoConnectionStringBuilder();
        builder["SessionProperties"] = " a = 1 | a = 2 || ";
        var properties = builder.SessionProperties;
        Assert.Equal("2", properties["a"]);
        Assert.Single(properties);

        var invalid = new PrestoConnectionStringBuilder();
        invalid["SessionProperties"] = "=value";
        Assert.Throws<ArgumentException>(() => _ = invalid.SessionProperties);
    }

    /// <summary>自定义头缺失、空白与重复键</summary>
    [Fact(DisplayName = "自定义头缺失、空白与重复键")]
    public void CustomHeaderEdgeCases()
    {
        Assert.Empty(new PrestoConnectionStringBuilder().CustomHeaders);

        var builder = new PrestoConnectionStringBuilder("CustomHeaders= X-Tenant : a |X-Tenant:b| X-Empty: ");
        var headers = builder.CustomHeaders;
        Assert.Equal("b", headers["X-Tenant"]);
        Assert.Equal(String.Empty, headers["X-Empty"]);

        var invalid = new PrestoConnectionStringBuilder("CustomHeaders=:value");
        Assert.Throws<ArgumentException>(() => _ = invalid.CustomHeaders);
    }

    /// <summary>显式认证模式不依赖密码或令牌</summary>
    [Fact(DisplayName = "显式认证模式不依赖密码或令牌")]
    public void ExplicitAuthModes()
    {
        var none = new PrestoConnectionStringBuilder("Auth=None;Password=secret;AccessToken=token");
        Assert.Equal(PrestoAuth.None, none.Auth);

        var basic = new PrestoConnectionStringBuilder("Auth=Basic");
        Assert.Equal(PrestoAuth.Basic, basic.Auth);
        basic.Validate();
    }

    /// <summary>AccessToken 为空时按密码推断认证</summary>
    [Fact(DisplayName = "AccessToken 为空时按密码推断认证")]
    public void EmptyTokenFallsBackToPassword()
    {
        var builder = new PrestoConnectionStringBuilder { AccessToken = "", Password = "secret" };

        Assert.Equal(PrestoAuth.Basic, builder.Auth);
    }

    /// <summary>时区空白回退 UTC，来源空白回退默认值</summary>
    [Fact(DisplayName = "空白时区与来源回退默认值")]
    public void BlankTimeZoneAndSourceFallback()
    {
        var builder = new PrestoConnectionStringBuilder { TimeZone = "  ", Source = " " };

        Assert.Equal("UTC", builder.TimeZone);
        Assert.Equal("NewLife.Presto", builder.Source);
    }

    /// <summary>字符串属性写入后自动去空白</summary>
    [Fact(DisplayName = "字符串属性写入后去空白")]
    public void StringPropertiesAreTrimmed()
    {
        var builder = new PrestoConnectionStringBuilder
        {
            Host = "  presto.local  ",
            Catalog = " hive ",
            Schema = " default ",
            User = " test ",
            TimeZone = " Asia/Shanghai ",
            Source = " bi ",
        };

        Assert.Equal("presto.local", builder.Host);
        Assert.Equal("hive", builder.Catalog);
        Assert.Equal("default", builder.Schema);
        Assert.Equal("test", builder.User);
        Assert.Equal("Asia/Shanghai", builder.TimeZone);
        Assert.Equal("bi", builder.Source);
    }

    /// <summary>布尔键支持 1/0 与大小写</summary>
    [Theory(DisplayName = "布尔键支持多种写法")]
    [InlineData("UseSsl=1", true)]
    [InlineData("UseSsl=0", false)]
    [InlineData("UseSsl=TRUE", true)]
    [InlineData("TrustServerCertificate=1", true)]
    [InlineData("TestConnection=1", true)]
    [InlineData("Compression=0", false)]
    public void BooleanVariants(String text, Boolean expected)
    {
        var builder = new PrestoConnectionStringBuilder(text);

        var actual = text.StartsWith("UseSsl") ? builder.UseSsl
            : text.StartsWith("TrustServerCertificate") ? builder.TrustServerCertificate
            : text.StartsWith("TestConnection") ? builder.TestConnection
            : builder.Compression;

        Assert.Equal(expected, actual);
    }
}
