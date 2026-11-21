using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Net.Http;
using System.Threading;
using NewLife;
using NewLife.Log;
using NewLife.Presto;

namespace UnitTest;

/// <summary>传输、选项与辅助类型测试</summary>
public class PrestoInfrastructureTests
{
    [Fact(DisplayName = "无认证不生成 Authorization")]
    public async Task NoAuth()
    {
        var options = PrestoConnectionOptions.From(new PrestoConnectionStringBuilder("Host=localhost;Auth=None"));

        Assert.Null(await PrestoAuthProvider.GetAuthorizationAsync(options, null, null, CancellationToken.None));
        Assert.Null(await PrestoAuthProvider.GetAuthorizationAsync(null!, null, null, CancellationToken.None));
    }

    [Fact(DisplayName = "Basic 认证生成标准头")]
    public async Task BasicAuth()
    {
        var options = PrestoConnectionOptions.From(new PrestoConnectionStringBuilder("Host=localhost;User=u;Password=p"));

        var header = await PrestoAuthProvider.GetAuthorizationAsync(options, null, null, CancellationToken.None);

        Assert.Equal("Basic " + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("u:p")), header);
        Assert.Equal(header, PrestoAuthProvider.BuildBasic(options));
    }

    [Fact(DisplayName = "JWT 回调返回空时回退配置令牌")]
    public async Task JwtFallback()
    {
        var options = PrestoConnectionOptions.From(new PrestoConnectionStringBuilder("Host=localhost;Auth=JWT;AccessToken=fixed"));

        Assert.Equal("Bearer fixed", await PrestoAuthProvider.GetAuthorizationAsync(options, () => "", null, CancellationToken.None));
    }

    [Fact(DisplayName = "同步认证头覆盖 Basic/JWT/无认证")]
    public void SyncAuthorization()
    {
        var basic = PrestoConnectionOptions.From(new PrestoConnectionStringBuilder("Host=localhost;User=u;Password=p"));
        Assert.Equal(PrestoAuthProvider.BuildBasic(basic), PrestoAuthProvider.GetAuthorization(basic, null));

        var jwt = PrestoConnectionOptions.From(new PrestoConnectionStringBuilder("Host=localhost;Auth=JWT;AccessToken=fixed"));
        Assert.Equal("Bearer fixed", PrestoAuthProvider.GetAuthorization(jwt, () => ""));
        Assert.Equal("Bearer x", PrestoAuthProvider.GetAuthorization(jwt, () => "x"));

        Assert.Throws<PrestoException>(() => PrestoAuthProvider.GetAuthorization(new PrestoConnectionOptions { Auth = PrestoAuth.Jwt }, null));

        var none = PrestoConnectionOptions.From(new PrestoConnectionStringBuilder("Host=localhost;Auth=None"));
        Assert.Null(PrestoAuthProvider.GetAuthorization(none, null));
        Assert.Null(PrestoAuthProvider.GetAuthorization(null!, null));
    }

    [Fact(DisplayName = "连接字符串同名别名共存时以规范键为准")]
    public void AliasConflict()
    {
        var builder = new PrestoConnectionStringBuilder("Host=primary;Server=secondary");
        Assert.Equal("primary", builder.Host);
    }

    [Fact(DisplayName = "连接配置空参数校验")]
    public void OptionsValidation()
    {
        Assert.Throws<ArgumentNullException>(() => PrestoConnectionOptions.From(null!));
    }

    [Theory(DisplayName = "网关路径归一化")]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("trino", "/trino/")]
    [InlineData("/trino", "/trino/")]
    [InlineData("trino/", "/trino/")]
    [InlineData("/trino/", "/trino/")]
    public void NormalizePath(String? path, String expected)
    {
        Assert.Equal(expected, PrestoConnectionOptions.NormalizePath(path));
    }

    [Fact(DisplayName = "连接配置基地址")]
    public void BaseAddress()
    {
        var options = PrestoConnectionOptions.From(new PrestoConnectionStringBuilder("Host=presto.local;Port=8443;UseSsl=true;Path=trino"));

        Assert.Equal("https://presto.local:8443/trino/", options.BaseAddress.AbsoluteUri);
    }

    [Fact(DisplayName = "连接字符串 Role 属性")]
    public void RoleProperty()
    {
        var builder = new PrestoConnectionStringBuilder { Role = "admin" };
        Assert.Equal("admin", builder.Role);

        builder.Role = "analyst";
        Assert.Equal("analyst", builder.Role);
    }

    [Fact(DisplayName = "TrinoHeaders 不覆盖显式协议")]
    public void TrinoHeadersRespectProtocol()
    {
        var builder = new PrestoConnectionStringBuilder("Host=localhost;Protocol=Presto;TrinoHeaders=true");

        Assert.Equal(PrestoProtocol.Presto, builder.Protocol);
    }

    [Fact(DisplayName = "用户名为空回退环境用户")]
    public void UserFallback()
    {
        var builder = new PrestoConnectionStringBuilder("Host=localhost;User=   ");

        Assert.False(builder.User.IsNullOrEmpty());
    }

    [Fact(DisplayName = "HttpClient 缓存复用与配置")]
    public void HttpClientCache()
    {
        Assert.Throws<ArgumentNullException>(() => PrestoHttpClientCache.Get(null!));

        var options = PrestoConnectionOptions.From(new PrestoConnectionStringBuilder("Host=cache1.local;Port=18081;Compression=false"));
        var first = PrestoHttpClientCache.Get(options);
        var second = PrestoHttpClientCache.Get(options);
        Assert.Same(first, second);
        Assert.Equal(options.BaseAddress, first.BaseAddress);
        Assert.Equal(TimeSpan.FromSeconds(100), first.Timeout);

        var other = PrestoConnectionOptions.From(new PrestoConnectionStringBuilder("Host=cache1.local;Port=18082;Compression=false"));
        Assert.NotSame(first, PrestoHttpClientCache.Get(other));

        var slower = PrestoConnectionOptions.From(new PrestoConnectionStringBuilder("Host=cache1.local;Port=18081;Compression=false;ConnectionTimeout=30"));
        Assert.NotSame(first, PrestoHttpClientCache.Get(slower));

        var proxied = PrestoConnectionOptions.From(new PrestoConnectionStringBuilder("Host=cache2.local;Port=18083;Proxy=http://proxy:8080;TrustServerCertificate=true;Compression=true"));
        var client = PrestoHttpClientCache.Get(proxied);
        Assert.Equal(proxied.BaseAddress, client.BaseAddress);
    }

    [Fact(DisplayName = "放宽证书校验记录警告")]
    public void TrustCertificateWarning()
    {
        var connection = TestHelper.CreateConnection(new FakePrestoHandler(), ";TrustServerCertificate=true");
        var log = new TestLog();
        connection.Log = log;

        connection.Open();

        Assert.Contains(log.Messages, e => e.Contains("TrustServerCertificate"));
        connection.Close();
    }

    [Fact(DisplayName = "连接构造与命令创建")]
    public void ConnectionConstructors()
    {
        var connection = new PrestoConnection("Host=abc");
        Assert.Equal("abc", connection.Settings.Host);
        Assert.Contains("abc", connection.ConnectionString);

        var opened = TestHelper.CreateOpenConnection(new FakePrestoHandler());
        PrestoCommand typed = opened.CreateCommand();
        PrestoCommand text = opened.CreateCommand("select 1");
        DbCommand dbCommand = ((DbConnection)opened).CreateCommand();

        Assert.Same(opened, typed.Connection);
        Assert.Equal("select 1", text.CommandText);
        Assert.IsType<PrestoCommand>(dbCommand);
    }

    [Fact(DisplayName = "命令连接属性与构造")]
    public void CommandConnection()
    {
        var connection = TestHelper.CreateOpenConnection(new FakePrestoHandler());
        using var command = new PrestoCommand(connection);

        Assert.Same(connection, command.Connection);
        Assert.Same(connection, ((DbCommand)command).Connection);
        Assert.Equal(CommandType.Text, command.CommandType);

        using var existing = new PrestoCommand("select 1");
        ((DbCommand)existing).Connection = connection;
        Assert.Same(connection, existing.Connection);

        using var wrong = new FakeConnection();
        Assert.Throws<ArgumentException>(() => ((DbCommand)existing).Connection = wrong);
    }

    [Fact(DisplayName = "列复杂类型判定")]
    public void ComplexColumn()
    {
        Assert.True(new PrestoColumn { Type = "array(varchar)" }.IsComplex);
        Assert.False(new PrestoColumn { Type = "bigint" }.IsComplex);
    }

    [Theory(DisplayName = "DbType 映射")]
    [InlineData("boolean", DbType.Boolean)]
    [InlineData("tinyint", DbType.SByte)]
    [InlineData("smallint", DbType.Int16)]
    [InlineData("integer", DbType.Int32)]
    [InlineData("int", DbType.Int32)]
    [InlineData("bigint", DbType.Int64)]
    [InlineData("real", DbType.Single)]
    [InlineData("double", DbType.Double)]
    [InlineData("decimal(10,2)", DbType.Decimal)]
    [InlineData("varbinary", DbType.Binary)]
    [InlineData("geometry", DbType.Binary)]
    [InlineData("sphericalgeography", DbType.Binary)]
    [InlineData("date", DbType.Date)]
    [InlineData("time", DbType.Time)]
    [InlineData("timestamp(3)", DbType.DateTime)]
    [InlineData("timestamp(3) with time zone", DbType.DateTimeOffset)]
    [InlineData("time(3) with time zone", DbType.String)]
    [InlineData("uuid", DbType.Guid)]
    [InlineData("varchar(10)", DbType.String)]
    [InlineData("array(varchar)", DbType.String)]
    public void DbTypes(String type, DbType expected)
    {
        Assert.Equal(expected, PrestoTypeMapper.GetDbType(type));
    }

    [Fact(DisplayName = "空会话头不覆盖本地状态")]
    public void EmptySessionHeader()
    {
        var options = PrestoConnectionOptions.From(new PrestoConnectionStringBuilder("Host=localhost;Catalog=hive"));
        var session = new PrestoSessionState(options);
        using var response = new HttpResponseMessage();
        response.Headers.TryAddWithoutValidation("X-Presto-Set-Catalog", String.Empty);

        Assert.False(session.ApplyResponseHeaders(response));
        Assert.Equal("hive", session.Catalog);
    }

    [Fact(DisplayName = "空可选会话头不发送")]
    public void EmptyOptionalHeaders()
    {
        var options = new PrestoConnectionOptions
        {
            User = "u",
            Catalog = "",
            Schema = "",
            Source = "",
            TimeZone = "",
            Language = "",
            ClientTags = "",
            ClientInfo = "",
            ResourceGroup = "",
            TraceToken = "",
            Role = "",
        };
        var session = new PrestoSessionState(options);

        var headers = session.BuildHeaders();

        Assert.Single(headers);
        Assert.Equal("u", headers["X-Presto-User"]);
    }

    [Fact(DisplayName = "会话头空项跳过")]
    public void SessionHeaderEmptyItems()
    {
        var options = PrestoConnectionOptions.From(new PrestoConnectionStringBuilder("Host=localhost"));
        var session = new PrestoSessionState(options);
        using var response = new HttpResponseMessage();
        response.Headers.TryAddWithoutValidation("X-Presto-Set-Session", "x=1,,y=2");
        response.Headers.TryAddWithoutValidation("X-Presto-Set-Role", String.Empty);

        Assert.True(session.ApplyResponseHeaders(response));
        Assert.Equal("1", session.SessionProperties["x"]);
        Assert.Equal("2", session.SessionProperties["y"]);
    }

    [Fact(DisplayName = "连接超时异常工厂")]
    public void ConnectionTimeoutException()
    {
        var ex = PrestoException.ConnectionTimeout(3);

        Assert.Contains("3", ex.Message);
    }

    [Fact(DisplayName = "诊断跨度空地址与默认端口")]
    public void DiagnosticsEdges()
    {
        var activities = new List<System.Diagnostics.Activity>();
        using var listener = new System.Diagnostics.ActivityListener
        {
            ShouldListenTo = source => source.Name == PrestoDiagnostics.SourceName,
            Sample = (ref System.Diagnostics.ActivityCreationOptions<System.Diagnostics.ActivityContext> options) => System.Diagnostics.ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => activities.Add(activity),
        };
        System.Diagnostics.ActivitySource.AddActivityListener(listener);

        var options = PrestoConnectionOptions.From(new PrestoConnectionStringBuilder("Host=localhost"));
        using (var span = PrestoDiagnostics.StartOpen(options)) Assert.True(span.IsActive);
        using (var span = PrestoDiagnostics.StartFetch(options, null)) Assert.True(span.IsActive);

        var empty = default(PrestoActivity);
        empty.SetTag("x", 1);
        empty.SetError(new InvalidOperationException());
        empty.Dispose();
        Assert.False(empty.IsActive);

        Assert.Equal(2, activities.Count);
        Assert.All(activities, e => Assert.Null(e.GetTagItem("server.port")));
    }

    private sealed class TestLog : ILog
    {
        public List<String> Messages { get; } = [];

        public Boolean Enable { get; set; } = true;

        public LogLevel Level { get; set; } = LogLevel.Info;

        public void Write(LogLevel level, String format, params Object?[] args) => Messages.Add(format);

        public void Debug(String format, params Object?[] args) => Messages.Add(format);

        public void Info(String format, params Object?[] args) => Messages.Add(format);

        public void Warn(String format, params Object?[] args) => Messages.Add(format);

        public void Error(String format, params Object?[] args) => Messages.Add(format);

        public void Fatal(String format, params Object?[] args) => Messages.Add(format);
    }

    private sealed class FakeConnection : DbConnection
    {
        public override String ConnectionString { get; set; } = String.Empty;

        public override String Database => String.Empty;

        public override String DataSource => String.Empty;

        public override String ServerVersion => String.Empty;

        public override ConnectionState State => ConnectionState.Closed;

        public override void ChangeDatabase(String databaseName) { }

        public override void Close() { }

        public override void Open() { }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();

        protected override DbCommand CreateDbCommand() => throw new NotSupportedException();
    }
}
