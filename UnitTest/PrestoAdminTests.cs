using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using NewLife.Presto;

namespace UnitTest;

/// <summary>查询诊断接口测试</summary>
public class PrestoAdminTests
{
    private const String QueriesJson = """
        [
          { "queryId": "20261121_1", "state": "RUNNING", "query": "select 1",
            "session": { "user": "alice", "source": "test", "catalog": "hive", "schema": "default" },
            "queryStats": { "state": "RUNNING", "elapsedTimeMillis": 1200, "processedRows": 10 } },
          { "queryId": "20261121_2", "state": "FAILED", "query": "select bad",
            "session": { "user": "bob" },
            "errorInfo": { "message": "boom", "errorCode": 1, "errorName": "SYNTAX_ERROR", "errorType": "USER_ERROR", "sqlState": "42000" } }
        ]
        """;

    [Fact(DisplayName = "查询列表解析会话、统计与错误")]
    public void Queries()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson(QueriesJson);
        var connection = TestHelper.CreateOpenConnection(handler);

        var list = connection.GetQueries();

        Assert.Equal(2, list.Count);
        Assert.Equal("/v1/query", handler.Requests[0].RequestUri!.AbsolutePath);
        Assert.Equal("20261121_1", list[0].Id);
        Assert.Equal("RUNNING", list[0].State);
        Assert.Equal("select 1", list[0].Query);
        Assert.Equal("alice", list[0].User);
        Assert.Equal("hive", list[0].Catalog);
        Assert.Equal("default", list[0].Schema);
        Assert.Equal(10, list[0].Stats!.ProcessedRows);
        Assert.Equal("20261121_2", list[1].Id);
        Assert.Equal("boom", list[1].Error!.Message);
        Assert.Equal("SYNTAX_ERROR", list[1].Error!.ErrorName);
        Assert.Equal("42000", list[1].Error!.SqlState);
    }

    [Fact(DisplayName = "查询详情按编号转义请求并解析")]
    public async Task QueryInfo()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson("""{ "queryId": "abc/def", "state": "FINISHED", "query": "select 1", "session": { "user": "alice" } }""");
        var connection = TestHelper.CreateOpenConnection(handler);

        var info = await connection.GetQueryInfoAsync("abc/def", CancellationToken.None);

        Assert.Equal("abc/def", info.Id);
        Assert.Equal("FINISHED", info.State);
        Assert.Equal("alice", info.User);
        Assert.EndsWith("v1/query/abc%2Fdef", handler.Requests[0].RequestUri!.AbsolutePath, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "节点列表兼容数组与单对象")]
    public void NodeInfo()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson("""[{ "nodeVersion": { "version": "476" }, "environment": "test", "coordinator": true, "uptime": "1h" }]""");
        handler.EnqueueJson("""{ "nodeVersion": "0.300", "environment": "test" }""");
        var connection = TestHelper.CreateOpenConnection(handler);

        var nodes = connection.GetNodes();

        Assert.Single(nodes);
        Assert.Equal("476", nodes[0].NodeVersion);
        Assert.Equal("test", nodes[0].Environment);
        Assert.True(nodes[0].Coordinator);
        Assert.Equal("/v1/node", handler.Requests[0].RequestUri!.AbsolutePath);

        var single = connection.GetNodes();
        Assert.Single(single);
        Assert.Equal("0.300", single[0].NodeVersion);
    }

    [Fact(DisplayName = "诊断接口透传 HTTP 错误")]
    public void HttpError()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueText("not found", HttpStatusCode.NotFound);
        var connection = TestHelper.CreateOpenConnection(handler);

        var ex = Assert.Throws<PrestoException>(() => connection.GetQueryInfo("missing"));
        Assert.Equal(404, ex.HttpStatusCode);
    }

    [Fact(DisplayName = "列表与节点接口透传 HTTP 错误")]
    public void ListAndNodeErrors()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueText("boom", HttpStatusCode.InternalServerError);
        handler.EnqueueText("boom", HttpStatusCode.InternalServerError);
        var connection = TestHelper.CreateOpenConnection(handler);

        Assert.Equal(500, Assert.Throws<PrestoException>(() => connection.GetQueries()).HttpStatusCode);
        Assert.Equal(500, Assert.Throws<PrestoException>(() => connection.GetNodes()).HttpStatusCode);
    }

    [Fact(DisplayName = "诊断接口对瞬态错误重试")]
    public void Retries()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueText("busy", HttpStatusCode.ServiceUnavailable);
        handler.EnqueueJson("""{ "nodeVersion": "476", "environment": "test" }""");
        var connection = TestHelper.CreateOpenConnection(handler, ";MaxRetries=1;RetryInterval=0");

        var nodes = connection.GetNodes();

        Assert.Single(nodes);
        Assert.Equal("476", nodes[0].NodeVersion);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact(DisplayName = "诊断接口前置校验")]
    public async Task Validation()
    {
        var handler = new FakePrestoHandler();
        var connection = TestHelper.CreateOpenConnection(handler);

        await Assert.ThrowsAsync<ArgumentException>(() => connection.GetQueryInfoAsync(" ", CancellationToken.None));

        connection.Close();
        Assert.Throws<InvalidOperationException>(() => connection.GetQueries());
    }

    [Fact(DisplayName = "空查询列表返回空集合")]
    public void EmptyQueries()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson("[]");
        var connection = TestHelper.CreateOpenConnection(handler);

        Assert.Empty(connection.GetQueries());
    }
}
