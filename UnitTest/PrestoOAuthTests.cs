using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using NewLife.Presto;

namespace UnitTest;

/// <summary>OAuth2 客户端凭据认证测试</summary>
public class PrestoOAuthTests
{
    private const String TokenJson = """{ "access_token": "abc", "token_type": "Bearer", "expires_in": 3600 }""";
    private const String ScalarJson = """{ "id": "q1", "columns": [{"name":"n","type":"integer"}], "data": [["42"]] }""";
    private const String Extra = ";Auth=OAuth2;TokenEndpoint=https://idp.local/token;ClientId=cid;ClientSecret=sec;Scope=all";

    [Fact(DisplayName = "OAuth2 客户端凭据换取 Bearer 令牌")]
    public void ClientCredentials()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson(TokenJson);
        handler.EnqueueJson(ScalarJson);
        var connection = TestHelper.CreateOpenConnection(handler, Extra);

        using var command = connection.CreateCommand("select 42");
        Assert.Equal(42, command.ExecuteScalar());

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("https://idp.local/token", handler.Requests[0].RequestUri!.ToString());
        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.Contains("grant_type=client_credentials", handler.Bodies[0]);
        Assert.Contains("client_id=cid", handler.Bodies[0]);
        Assert.Contains("client_secret=sec", handler.Bodies[0]);
        Assert.Contains("scope=all", handler.Bodies[0]);

        var authorization = handler.Requests[1].Headers.Authorization;
        Assert.NotNull(authorization);
        Assert.Equal("Bearer", authorization!.Scheme);
        Assert.Equal("abc", authorization.Parameter);
    }

    [Fact(DisplayName = "OAuth2 令牌在有效期内复用")]
    public void TokenIsCached()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson(TokenJson);
        handler.EnqueueJson(ScalarJson);
        handler.EnqueueJson(ScalarJson);
        var connection = TestHelper.CreateOpenConnection(handler, Extra);

        using var first = connection.CreateCommand("select 42");
        Assert.Equal(42, first.ExecuteScalar());
        using var second = connection.CreateCommand("select 42");
        Assert.Equal(42, second.ExecuteScalar());

        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact(DisplayName = "OAuth2 令牌过期后自动刷新")]
    public async Task TokenRefreshes()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson("""{ "access_token": "first", "token_type": "Bearer", "expires_in": 1 }""");
        handler.EnqueueJson(ScalarJson);
        handler.EnqueueJson("""{ "access_token": "second", "token_type": "Bearer", "expires_in": 3600 }""");
        handler.EnqueueJson(ScalarJson);
        var connection = TestHelper.CreateOpenConnection(handler, Extra);

        using (var command = connection.CreateCommand("select 42"))
        {
            Assert.Equal(42, command.ExecuteScalar());
        }

        await Task.Delay(1100);

        using (var command = connection.CreateCommand("select 42"))
        {
            Assert.Equal(42, command.ExecuteScalar());
        }

        Assert.Equal(4, handler.Requests.Count);
        Assert.Equal("second", handler.Requests[3].Headers.Authorization!.Parameter);
    }

    [Fact(DisplayName = "显式令牌回调优先于 OAuth2 缓存")]
    public void TokenProviderWins()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson(ScalarJson);
        var connection = TestHelper.CreateOpenConnection(handler, Extra);
        connection.AccessTokenProvider = () => "custom";

        using var command = connection.CreateCommand("select 42");
        Assert.Equal(42, command.ExecuteScalar());

        Assert.Single(handler.Requests);
        Assert.Equal("custom", handler.Requests[0].Headers.Authorization!.Parameter);
    }

    [Fact(DisplayName = "OAuth2 令牌请求失败抛出带状态码的异常")]
    public void TokenFailure()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueText("denied", HttpStatusCode.Unauthorized);
        var connection = TestHelper.CreateOpenConnection(handler, Extra);

        using var command = connection.CreateCommand("select 42");
        var ex = Assert.Throws<PrestoException>(() => command.ExecuteScalar());
        Assert.Equal(401, ex.HttpStatusCode);
        Assert.DoesNotContain("sec", ex.Message);
        Assert.Single(handler.Requests);
    }

    [Fact(DisplayName = "缺少令牌响应字段时明确报错")]
    public void TokenWithoutAccessToken()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson("""{ "token_type": "Bearer" }""");
        var connection = TestHelper.CreateOpenConnection(handler, Extra);

        using var command = connection.CreateCommand("select 42");
        var ex = Assert.Throws<PrestoException>(() => command.ExecuteScalar());
        Assert.Contains("OAuth2", ex.Message);
    }

    [Fact(DisplayName = "OAuth2 令牌请求超时映射为驱动异常")]
    public void TokenTimeout()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueDelay(TimeSpan.FromSeconds(5), TokenJson);
        var connection = TestHelper.CreateOpenConnection(handler, Extra + ";ConnectionTimeout=1");

        using var command = connection.CreateCommand("select 42");
        var ex = Assert.Throws<PrestoException>(() => command.ExecuteScalar());
        Assert.Contains("1", ex.Message);
    }

    [Fact(DisplayName = "关闭连接释放 OAuth2 令牌提供者")]
    public void DisposeProvider()
    {
        var handler = new FakePrestoHandler();
        var connection = TestHelper.CreateOpenConnection(handler, Extra);
        connection.Close();

        // 无自定义处理器时使用共享客户端，释放分支同样要覆盖
        var options = PrestoConnectionOptions.From(new PrestoConnectionStringBuilder("Host=localhost;Auth=OAuth2;TokenEndpoint=https://idp.local/token;ClientId=cid"));
        using var provider = new PrestoOAuthTokenProvider(options);
    }

    [Fact(DisplayName = "最小令牌响应与默认有效期")]
    public void MinimalToken()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson("""{ "access_token": "xyz" }""");
        handler.EnqueueJson(ScalarJson);
        var connection = TestHelper.CreateOpenConnection(handler, Extra);

        using var command = connection.CreateCommand("select 42");
        Assert.Equal(42, command.ExecuteScalar());
        Assert.Equal("Bearer", handler.Requests[1].Headers.Authorization!.Scheme);
        Assert.Equal("xyz", handler.Requests[1].Headers.Authorization!.Parameter);
    }

    [Fact(DisplayName = "expires_in 非正数时使用默认有效期")]
    public void ZeroLifetime()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson("""{ "access_token": "abc", "token_type": "bearer", "expires_in": 0 }""");
        handler.EnqueueJson(ScalarJson);
        var connection = TestHelper.CreateOpenConnection(handler, Extra);

        using var command = connection.CreateCommand("select 42");
        Assert.Equal(42, command.ExecuteScalar());
        Assert.Equal("bearer", handler.Requests[1].Headers.Authorization!.Scheme);
    }

    [Fact(DisplayName = "未初始化 OAuth2 与空 JWT 明确报错")]
    public void NotInitialized()
    {
        var options = PrestoConnectionOptions.From(new PrestoConnectionStringBuilder("Host=localhost;Auth=OAuth2;TokenEndpoint=https://idp.local/token;ClientId=cid"));
        Assert.Throws<PrestoException>(() => { _ = PrestoAuthProvider.GetAuthorizationAsync(options, null, null, System.Threading.CancellationToken.None); });

        var jwt = new PrestoConnectionOptions { Auth = PrestoAuth.Jwt };
        Assert.Throws<PrestoException>(() => { _ = PrestoAuthProvider.GetAuthorizationAsync(jwt, null, null, System.Threading.CancellationToken.None); });
    }

    [Fact(DisplayName = "OAuth2 必填参数校验与别名归一化")]
    public void Validation()
    {
        Assert.Throws<ArgumentException>(() => new PrestoConnectionStringBuilder("Auth=OAuth2").Validate());
        Assert.Throws<ArgumentException>(() => new PrestoConnectionStringBuilder("Auth=OAuth2;TokenEndpoint=https://idp.local/token").Validate());
        Assert.Throws<ArgumentException>(() => new PrestoConnectionStringBuilder("Auth=Kerberos").Validate());

        var builder = new PrestoConnectionStringBuilder();
        builder.TokenEndpoint = "https://idp.local/token";
        builder.ClientId = "c";
        builder.ClientSecret = "s";
        builder.Scope = "a";
        builder.ComplexTypes = PrestoComplexType.Structured;
        Assert.Equal("https://idp.local/token", builder.TokenEndpoint);
        Assert.Equal("c", builder.ClientId);
        Assert.Equal("s", builder.ClientSecret);
        Assert.Equal("a", builder.Scope);
        Assert.Equal(PrestoComplexType.Structured, builder.ComplexTypes);

        var aliased = new PrestoConnectionStringBuilder("Auth=OAuth;OAuthTokenEndpoint=https://idp.local/token;OAuthClientId=c;OAuthClientSecret=s;OAuthScope=a");
        Assert.Equal(PrestoAuth.OAuth2, aliased.Auth);
        Assert.Equal("https://idp.local/token", aliased.TokenEndpoint);
        Assert.Equal("c", aliased.ClientId);
        Assert.Equal("s", aliased.ClientSecret);
        Assert.Equal("a", aliased.Scope);
    }
}
