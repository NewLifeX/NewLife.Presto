using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NewLife.Presto;

namespace UnitTest;

/// <summary>语句客户端与协议测试</summary>
public class PrestoStatementClientTests
{
    private static (PrestoStatementClient Client, FakePrestoHandler Handler, PrestoSessionState Session) CreateClient(String extra = "")
    {
        var handler = new FakePrestoHandler();
        var builder = new PrestoConnectionStringBuilder("Host=localhost;Port=8080;Catalog=hive;Schema=default;User=test;Password=secret;ConnectionTimeout=5;MaxRetries=2;RetryInterval=0" + extra);
        var options = PrestoConnectionOptions.From(builder);
        var session = new PrestoSessionState(options);
        var http = new HttpClient(handler) { BaseAddress = options.BaseAddress, Timeout = TimeSpan.FromSeconds(options.ConnectionTimeout) };

        return (new PrestoStatementClient(http, options, session), handler, session);
    }

    [Fact(DisplayName = "同步请求超时映射为连接超时")]
    public void SyncConnectionTimeout()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueDelay(TimeSpan.FromSeconds(5), """{ "id": "q1", "columns": [], "data": [] }""");
        using var connection = new PrestoConnection("Host=localhost;Port=8080;User=test;ConnectionTimeout=1;MaxRetries=0", handler);
        connection.Open();
        using var command = connection.CreateCommand("select 1");

        var ex = Assert.Throws<PrestoException>(() => command.ExecuteScalar());

        Assert.Equal(NewLife.Localization.Translate("Connection timeout of {0} seconds expired.", 1), ex.Message);
    }

    [Fact(DisplayName = "自定义处理器不支持同步时回退异步通道")]
    public void SyncFallbackToAsyncHandler()
    {
        var handler = new AsyncOnlyHandler((request, token) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{ "columns": [{ "name": "n", "type": "integer" }], "data": [[1]] }""", Encoding.UTF8, "application/json"),
        }));
        using var connection = new PrestoConnection("Host=localhost;Port=8080;User=test;MaxRetries=0", handler);
        connection.Open();
        using var command = connection.CreateCommand("select 1");

        Assert.Equal(1, Convert.ToInt32(command.ExecuteScalar()));
    }

    [Fact(DisplayName = "自定义处理器不支持同步轮询时回退异步通道")]
    public void FetchFallbackToAsyncHandler()
    {
        var handler = new AsyncOnlyHandler((request, token) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{ "id": "q1", "columns": [{ "name": "n", "type": "integer" }], "data": [[1]] }""", Encoding.UTF8, "application/json"),
        }));
        var options = PrestoConnectionOptions.From(new PrestoConnectionStringBuilder("Host=localhost;Port=8080;User=test;Auth=None;MaxRetries=0"));
        var session = new PrestoSessionState(options);
        var http = new HttpClient(handler) { BaseAddress = options.BaseAddress };
        var client = new PrestoStatementClient(http, options, session);

        var results = client.Fetch("http://localhost:8080/v1/statement/q1/1", CancellationToken.None);

        Assert.Single(results.Data);
    }

    private sealed class AsyncOnlyHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }

    [Fact(DisplayName = "同步提交返回错误页抛出驱动异常")]
    public void SubmitErrorSync()
    {
        var (client, handler, _) = CreateClient();
        handler.EnqueueJson("""{ "id": "q1", "error": { "message": "bad", "errorCode": 1 } }""");

        var ex = Assert.Throws<PrestoException>(() => client.Submit("select 1", CancellationToken.None));

        Assert.Contains("bad", ex.Message);
    }

    [Fact(DisplayName = "同步轮询覆盖 HTTP 错误与错误页")]
    public void FetchErrorSync()
    {
        var (client, handler, _) = CreateClient();
        handler.Enqueue((request, token) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("boom", Encoding.UTF8, "text/plain"),
        }));
        var ex = Assert.Throws<PrestoException>(() => client.Fetch("http://localhost:8080/v1/statement/q1/1", CancellationToken.None));
        Assert.Contains("boom", ex.Message);

        var (client2, handler2, _) = CreateClient();
        handler2.EnqueueJson("""{ "id": "q1", "error": { "message": "bad", "errorCode": 2 } }""");
        Assert.Throws<PrestoException>(() => client2.Fetch("http://localhost:8080/v1/statement/q1/1", CancellationToken.None));
    }

    [Fact(DisplayName = "同步重试使用非零退避并记录日志")]
    public void RetryTransientSyncWithDelay()
    {
        var handler = new FakePrestoHandler();
        var options = PrestoConnectionOptions.From(new PrestoConnectionStringBuilder("Host=localhost;Port=8080;User=test;Password=secret;MaxRetries=1;RetryInterval=1"));
        var session = new PrestoSessionState(options);
        var http = new HttpClient(handler) { BaseAddress = options.BaseAddress };
        var client = new PrestoStatementClient(http, options, session, NewLife.Log.XTrace.Log);
        handler.Enqueue((request, token) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        handler.EnqueueJson("""{ "id": "q1", "columns": [{ "name": "a" }], "data": [["1"]] }""");

        var results = client.Fetch("http://localhost:8080/v1/statement/q1/1", CancellationToken.None);

        Assert.Single(results.Data);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact(DisplayName = "同步轮询遇到 503 后重试成功")]
    public void RetryTransientSync()
    {
        var (client, handler, _) = CreateClient(";MaxRetries=2;RetryInterval=0");
        handler.Enqueue((request, token) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        handler.EnqueueJson("""{ "id": "q1", "columns": [{ "name": "a" }], "data": [["1"]] }""");

        var results = client.Fetch("http://localhost:8080/v1/statement/q1/1", CancellationToken.None);

        Assert.Single(results.Data);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact(DisplayName = "同步提交覆盖 JWT 与无认证请求头")]
    public void SubmitAuthVariantsSync()
    {
        var (jwtClient, jwtHandler, _) = CreateClient(";Auth=JWT;AccessToken=fixed");
        jwtHandler.EnqueueJson("""{ "id": "q1", "columns": [], "data": [] }""");
        jwtClient.Submit("select 1", CancellationToken.None);
        Assert.Equal("Bearer fixed", jwtHandler.Requests[0].Headers.GetValues("Authorization").Single());

        var handler = new FakePrestoHandler();
        var options = PrestoConnectionOptions.From(new PrestoConnectionStringBuilder("Host=localhost;Port=8080;User=test;Auth=None"));
        var session = new PrestoSessionState(options);
        var http = new HttpClient(handler) { BaseAddress = options.BaseAddress };
        var client = new PrestoStatementClient(http, options, session);
        handler.EnqueueJson("""{ "id": "q1", "columns": [], "data": [] }""");

        client.Submit("select 1", CancellationToken.None);

        Assert.False(handler.Requests[0].Headers.Contains("Authorization"));
    }

    [Fact(DisplayName = "提交请求携带全部会话头")]
    public async Task SubmitHeaders()
    {
        var (client, handler, _) = CreateClient(";Compression=true;SessionProperties=a=1|b=2;CustomHeaders=X-Tenant:team;Role=admin;Language=en;ClientTags=t1,t2;ClientInfo=ci;ResourceGroup=rg;TraceToken=tt;Source=src");
        handler.EnqueueJson("""{ "id": "q1", "columns": [], "data": [] }""");

        var results = await client.SubmitAsync("select 1", CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("http://localhost:8080/v1/statement", request.RequestUri!.AbsoluteUri);
        Assert.Equal("select 1", handler.LastBody);
        Assert.StartsWith("text/plain", request.Content!.Headers.ContentType!.ToString());

        Assert.Equal("test", request.Headers.GetValues("X-Presto-User").Single());
        Assert.Equal("hive", request.Headers.GetValues("X-Presto-Catalog").Single());
        Assert.Equal("default", request.Headers.GetValues("X-Presto-Schema").Single());
        Assert.Equal("src", request.Headers.GetValues("X-Presto-Source").Single());
        Assert.Equal("UTC", request.Headers.GetValues("X-Presto-Time-Zone").Single());
        Assert.Equal("en", request.Headers.GetValues("X-Presto-Language").Single());
        Assert.Equal("t1,t2", request.Headers.GetValues("X-Presto-Client-Tags").Single());
        Assert.Equal("ci", request.Headers.GetValues("X-Presto-Client-Info").Single());
        Assert.Equal("rg", request.Headers.GetValues("X-Presto-Resource-Group").Single());
        Assert.Equal("tt", request.Headers.GetValues("X-Presto-Trace-Token").Single());
        Assert.Equal("admin", request.Headers.GetValues("X-Presto-Role").Single());
        Assert.Equal("a=1,b=2", request.Headers.GetValues("X-Presto-Session").Single());
        Assert.Equal("team", request.Headers.GetValues("X-Tenant").Single());
        Assert.Equal("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("test:secret")), request.Headers.GetValues("Authorization").Single());
        Assert.StartsWith("NewLife.Presto/", request.Headers.UserAgent.ToString());
        Assert.Contains("application/json", request.Headers.Accept.ToString());
        Assert.Contains("gzip", request.Headers.AcceptEncoding.ToString());

        Assert.Equal("q1", results.Id);
        Assert.Equal("q1", client.QueryId);
        Assert.True(client.IsCompleted);
    }

    [Fact(DisplayName = "Trino 头与 JWT 刷新回调")]
    public async Task TrinoJwtHeaders()
    {
        var (client, handler, _) = CreateClient(";Protocol=Trino;Auth=JWT;AccessToken=old");
        client.AccessTokenProvider = () => "new-token";
        handler.EnqueueJson("""{ "id": "q1" }""");

        await client.SubmitAsync("select 1", CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("test", request.Headers.GetValues("X-Trino-User").Single());
        Assert.Equal("Bearer new-token", request.Headers.GetValues("Authorization").Single());
    }

    [Fact(DisplayName = "JWT 回退连接串令牌")]
    public async Task JwtFallbackToken()
    {
        var (client, handler, _) = CreateClient(";Auth=JWT;AccessToken=fixed");
        handler.EnqueueJson("""{ "id": "q1" }""");

        await client.SubmitAsync("select 1", CancellationToken.None);

        Assert.Equal("Bearer fixed", Assert.Single(handler.Requests).Headers.GetValues("Authorization").Single());
    }

    [Fact(DisplayName = "缺少 JWT 令牌时连接配置校验失败")]
    public void JwtMissingTokenThrows()
    {
        Assert.Throws<ArgumentException>(() => CreateClient(";Auth=JWT"));
    }

    [Fact(DisplayName = "默认与显式关闭压缩时都不发送 Accept-Encoding")]
    public async Task NoCompressionHeader()
    {
        foreach (var suffix in new[] { "", ";Compression=false" })
        {
            var (client, handler, _) = CreateClient(suffix);
            handler.EnqueueJson("""{ "id": "q1" }""");

            await client.SubmitAsync("select 1", CancellationToken.None);

            Assert.False(Assert.Single(handler.Requests).Headers.AcceptEncoding.Any());
        }
    }

    [Fact(DisplayName = "轮询相对 nextUri")]
    public async Task PollRelativeNextUri()
    {
        var (client, handler, _) = CreateClient();
        handler.EnqueueJson("""{ "id": "q1", "columns": [], "data": [], "nextUri": "/v1/statement/q1/1" }""");
        handler.EnqueueJson("""{ "id": "q1", "columns": [], "data": [] }""");

        var first = await client.SubmitAsync("select 1", CancellationToken.None);
        Assert.False(client.IsCompleted);

        var second = await client.FetchAsync(first.NextUri!, CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(HttpMethod.Get, handler.Requests[1].Method);
        Assert.Equal("http://localhost:8080/v1/statement/q1/1", handler.Requests[1].RequestUri!.AbsoluteUri);
        Assert.True(client.IsCompleted);
        Assert.Null(second.NextUri);
    }

    [Fact(DisplayName = "轮询瞬态错误后重试成功")]
    public async Task RetryTransient()
    {
        var (client, handler, _) = CreateClient(";MaxRetries=2;RetryInterval=0");
        using var first = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent("busy", Encoding.UTF8, "text/plain"),
        };
        first.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
        handler.Enqueue((request, token) => Task.FromResult(first));
        handler.EnqueueJson("""{ "id": "q1", "data": [] }""");

        var results = await client.FetchAsync("http://localhost:8080/v1/statement/q1/1", CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Null(results.Error);
    }

    [Fact(DisplayName = "轮询请求错误映射为异常")]
    public async Task FetchHttpError()
    {
        var (client, handler, _) = CreateClient();
        handler.EnqueueText("bad", HttpStatusCode.BadRequest);

        var ex = await Assert.ThrowsAsync<PrestoException>(() => client.FetchAsync("http://localhost:8080/v1/statement/q1/1", CancellationToken.None));

        Assert.Equal(400, ex.HttpStatusCode);
    }

    [Fact(DisplayName = "服务端信息也重试瞬态错误")]
    public async Task GetServerInfoRetries()
    {
        var (client, handler, _) = CreateClient(";MaxRetries=2;RetryInterval=1");
        handler.EnqueueText("busy", HttpStatusCode.ServiceUnavailable);
        handler.EnqueueJson("""{ "nodeVersion": "468" }""");

        var info = await client.GetServerInfoAsync(CancellationToken.None);

        Assert.Equal("468", info.NodeVersion);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact(DisplayName = "提交请求不重试")]
    public async Task SubmitDoesNotRetry()
    {
        var (client, handler, _) = CreateClient(";MaxRetries=3;RetryInterval=0");
        handler.EnqueueText("busy", HttpStatusCode.ServiceUnavailable);
        handler.EnqueueJson("""{ "id": "q1" }""");

        var ex = await Assert.ThrowsAsync<PrestoException>(() => client.SubmitAsync("select 1", CancellationToken.None));

        Assert.Equal(503, ex.HttpStatusCode);
        Assert.Single(handler.Requests);
    }

    [Fact(DisplayName = "200 响应中的错误对象映射为异常")]
    public async Task ErrorObjectThrows()
    {
        var (client, handler, _) = CreateClient();
        handler.EnqueueJson("""
        { "id": "q9", "error": {
            "message": "Table not found",
            "errorCode": 47,
            "errorName": "TABLE_NOT_FOUND",
            "errorType": "USER_ERROR",
            "sqlState": "42S02",
            "failureInfo": { "type": "io.trino.spi.TrinoException" },
            "errorLocation": { "lineNumber": 1, "columnNumber": 8 }
        } }
        """);

        var ex = await Assert.ThrowsAsync<PrestoException>(() => client.SubmitAsync("select 1", CancellationToken.None));

        Assert.Equal("q9", ex.QueryId);
        Assert.Equal(47, ex.PrestoErrorCode);
        Assert.Equal("TABLE_NOT_FOUND", ex.ErrorName);
        Assert.Equal("USER_ERROR", ex.ErrorType);
        Assert.Equal("42S02", ex.PrestoSqlState);
        Assert.Contains("Table not found", ex.Message);
        Assert.Contains("q9", ex.Message);
        Assert.Contains("47", ex.Message);
        Assert.NotNull(ex.FailureInfo);
        Assert.NotNull(ex.ErrorLocation);
    }

    [Fact(DisplayName = "HTTP 错误解析 JSON 错误体")]
    public async Task HttpErrorWithJson()
    {
        var (client, handler, _) = CreateClient();
        handler.EnqueueJson("""{ "error": { "message": "bad request", "errorCode": 1 } }""", HttpStatusCode.BadRequest);

        var ex = await Assert.ThrowsAsync<PrestoException>(() => client.SubmitAsync("select 1", CancellationToken.None));

        Assert.Equal(400, ex.HttpStatusCode);
        Assert.Equal(1, ex.PrestoErrorCode);
        Assert.Contains("bad request", ex.Message);
    }

    [Fact(DisplayName = "HTTP 错误保留纯文本响应")]
    public async Task HttpErrorWithText()
    {
        var (client, handler, _) = CreateClient();
        handler.EnqueueText("boom", HttpStatusCode.InternalServerError);

        var ex = await Assert.ThrowsAsync<PrestoException>(() => client.SubmitAsync("select 1", CancellationToken.None));

        Assert.Equal(500, ex.HttpStatusCode);
        Assert.Contains("boom", ex.Message);
        Assert.Equal("boom", ex.Data["response"]);
    }

    [Fact(DisplayName = "取消发送 DELETE")]
    public async Task CancelSendsDelete()
    {
        var (client, handler, _) = CreateClient();
        handler.EnqueueJson("""{ "id": "q1", "nextUri": "http://localhost:8080/v1/statement/q1/1", "data": [] }""");
        handler.EnqueueJson("""{ "id": "q1" }""");

        await client.SubmitAsync("select 1", CancellationToken.None);
        await client.CancelAsync();

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(HttpMethod.Delete, handler.Requests[1].Method);
        Assert.Equal("http://localhost:8080/v1/statement/q1/1", handler.Requests[1].RequestUri!.AbsoluteUri);
        Assert.Null(client.NextUri);
    }

    [Fact(DisplayName = "无活动查询时取消不发请求")]
    public async Task CancelWithoutQueryDoesNothing()
    {
        var (client, handler, _) = CreateClient();

        await client.CancelAsync();

        Assert.Empty(handler.Requests);
    }

    [Fact(DisplayName = "取消失败不抛异常")]
    public async Task CancelFailureIsSwallowed()
    {
        var (client, handler, _) = CreateClient();
        handler.EnqueueJson("""{ "id": "q1", "nextUri": "http://localhost:8080/v1/statement/q1/1", "data": [] }""");
        handler.EnqueueException(new HttpRequestException("network"));

        await client.SubmitAsync("select 1", CancellationToken.None);
        await client.CancelAsync();

        Assert.Null(client.NextUri);
    }

    [Fact(DisplayName = "应用服务端会话指令")]
    public async Task ApplySessionHeaders()
    {
        var (client, handler, session) = CreateClient();
        handler.EnqueueJson("""{ "id": "q1", "data": [] }""", HttpStatusCode.OK, response =>
        {
            response.Headers.TryAddWithoutValidation("X-Presto-Set-Catalog", "newcat");
            response.Headers.TryAddWithoutValidation("X-Presto-Set-Schema", "newschema");
            response.Headers.TryAddWithoutValidation("X-Presto-Set-Session", "x=1,y=2");
            response.Headers.TryAddWithoutValidation("X-Presto-Clear-Session", "b");
            response.Headers.TryAddWithoutValidation("X-Presto-Set-Role", "analyst");
        });

        await client.SubmitAsync("select 1", CancellationToken.None);

        Assert.Equal("newcat", session.Catalog);
        Assert.Equal("newschema", session.Schema);
        Assert.Equal("1", session.SessionProperties["x"]);
        Assert.Equal("2", session.SessionProperties["y"]);
        Assert.Equal("analyst", session.Role);
    }

    [Fact(DisplayName = "获取服务端信息")]
    public async Task GetServerInfo()
    {
        var (client, handler, _) = CreateClient();
        handler.EnqueueJson("""{ "nodeVersion": { "version": "468" }, "environment": "prod" }""");

        var info = await client.GetServerInfoAsync(CancellationToken.None);

        Assert.Equal("468", info.NodeVersion);
        Assert.Equal("prod", info.Environment);
        Assert.Equal("http://localhost:8080/v1/info", Assert.Single(handler.Requests).RequestUri!.AbsoluteUri);
    }

    [Fact(DisplayName = "连接超时映射为驱动异常")]
    public async Task ConnectionTimeoutThrows()
    {
        var (client, handler, _) = CreateClient(";ConnectionTimeout=1");
        handler.EnqueueDelay(TimeSpan.FromSeconds(5), """{ "id": "q1" }""");

        var ex = await Assert.ThrowsAsync<PrestoException>(() => client.SubmitAsync("select 1", CancellationToken.None));

        Assert.Equal(NewLife.Localization.Translate("Connection timeout of {0} seconds expired.", 1), ex.Message);
    }

    [Fact(DisplayName = "调用方取消保持取消异常")]
    public async Task CallerCancellationPropagates()
    {
        var (client, handler, _) = CreateClient();
        handler.EnqueueDelay(TimeSpan.FromSeconds(5), """{ "id": "q1" }""");
        using var cts = new CancellationTokenSource(100);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.SubmitAsync("select 1", cts.Token));
    }

    [Fact(DisplayName = "非法响应映射为驱动异常")]
    public async Task InvalidJsonThrows()
    {
        var (client, handler, _) = CreateClient();
        handler.EnqueueJson("oops");

        await Assert.ThrowsAsync<PrestoException>(() => client.SubmitAsync("select 1", CancellationToken.None));
    }

    [Fact(DisplayName = "空响应映射为驱动异常")]
    public async Task EmptyBodyThrows()
    {
        var (client, handler, _) = CreateClient();
        handler.EnqueueText(String.Empty);

        await Assert.ThrowsAsync<PrestoException>(() => client.SubmitAsync("select 1", CancellationToken.None));
    }

    [Fact(DisplayName = "无内容的 HTTP 响应按空响应处理")]
    public async Task NullContentThrows()
    {
        var (client, handler, _) = CreateClient();
        handler.Enqueue((request, token) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));

        await Assert.ThrowsAsync<PrestoException>(() => client.SubmitAsync("select 1", CancellationToken.None));
    }

    [Fact(DisplayName = "轮询返回错误对象")]
    public async Task FetchErrorObject()
    {
        var (client, handler, _) = CreateClient();
        handler.EnqueueJson("""{ "id": "q1", "data": [], "nextUri": "http://localhost:8080/v1/statement/q1/1" }""");
        handler.EnqueueJson("""{ "error": { "message": "bad", "errorCode": 2 } }""");

        var first = await client.SubmitAsync("select 1", CancellationToken.None);
        var ex = await Assert.ThrowsAsync<PrestoException>(() => client.FetchAsync(first.NextUri!, CancellationToken.None));

        Assert.Equal(2, ex.PrestoErrorCode);
        Assert.Equal("q1", ex.QueryId);
    }

    [Fact(DisplayName = "HTTP 错误体不是标准错误对象")]
    public async Task HttpErrorNonStandardBody()
    {
        var (client, handler, _) = CreateClient();
        handler.EnqueueJson("""{ "message": "plain" }""", HttpStatusCode.BadRequest);

        var ex = await Assert.ThrowsAsync<PrestoException>(() => client.SubmitAsync("select 1", CancellationToken.None));

        Assert.Equal(400, ex.HttpStatusCode);
        Assert.Contains("plain", ex.Message);
    }

    [Fact(DisplayName = "语句客户端构造参数校验")]
    public void ConstructorValidation()
    {
        var options = PrestoConnectionOptions.From(new PrestoConnectionStringBuilder("Host=localhost"));
        var session = new PrestoSessionState(options);
        using var http = new HttpClient(new FakePrestoHandler());

        Assert.Throws<ArgumentNullException>(() => new PrestoStatementClient(null!, options, session));
        Assert.Throws<ArgumentNullException>(() => new PrestoStatementClient(http, null!, session));
        Assert.Throws<ArgumentNullException>(() => new PrestoStatementClient(http, options, null!));
    }
}
