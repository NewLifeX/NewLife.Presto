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

    [Fact(DisplayName = "OAuth2 必填参数校验与别名归一化")]
    public void Validation()
    {
        Assert.Throws<ArgumentException>(() => new PrestoConnectionStringBuilder("Auth=OAuth2").Validate());
        Assert.Throws<ArgumentException>(() => new PrestoConnectionStringBuilder("Auth=OAuth2;TokenEndpoint=https://idp.local/token").Validate());

        var builder = new PrestoConnectionStringBuilder("Auth=OAuth;OAuthTokenEndpoint=https://idp.local/token;OAuthClientId=c;OAuthClientSecret=s;OAuthScope=a");
        Assert.Equal(PrestoAuth.OAuth2, builder.Auth);
        Assert.Equal("https://idp.local/token", builder.TokenEndpoint);
        Assert.Equal("c", builder.ClientId);
        Assert.Equal("s", builder.ClientSecret);
        Assert.Equal("a", builder.Scope);
    }
}
