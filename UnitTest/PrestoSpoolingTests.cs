using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NewLife.Presto;

namespace UnitTest;

/// <summary>Trino Spooling 协议测试</summary>
public class PrestoSpoolingTests
{
    private static String InlineData(String json) => Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

    private static String SpooledPage(String segments, String encoding = "json")
        => $$"""
        {
          "id": "q1",
          "columns": [{ "name": "id", "type": "integer" }, { "name": "name", "type": "varchar" }],
          "data": { "encoding": "{{encoding}}", "segments": [{{segments}}] }
        }
        """;

    private static async Task<List<String>> ReadAsync(PrestoConnection connection, String sql)
    {
        using var command = connection.CreateCommand(sql);
        using var reader = await command.ExecuteReaderAsync(CancellationToken.None);

        var rows = new List<String>();
        while (await reader.ReadAsync(CancellationToken.None))
        {
            rows.Add(reader.GetInt32(0) + ":" + reader.GetString(1));
        }

        return rows;
    }

    [Fact(DisplayName = "Spooling 选项别名归一化并默认关闭")]
    public void SpoolingOption()
    {
        Assert.False(new PrestoConnectionStringBuilder().Spooling);
        Assert.False(new PrestoConnectionStringBuilder("Protocol=Trino").Spooling);

        var builder = new PrestoConnectionStringBuilder("Protocol=Trino;SpoolingEnabled=true");
        Assert.True(builder.Spooling);
        Assert.True(builder.ContainsKey("Spooling"));
        Assert.False(builder.ContainsKey("SpoolingEnabled"));

        builder.Spooling = false;
        Assert.False(builder.Spooling);
    }

    [Fact(DisplayName = "PrestoDB 协议下启用 Spooling 校验失败")]
    public void SpoolingRequiresTrino()
    {
        var builder = new PrestoConnectionStringBuilder("Host=localhost;Spooling=true");

        var ex = Assert.Throws<ArgumentException>(() => PrestoConnectionOptions.From(builder));
        Assert.Contains("Trino", ex.Message);
    }

    [Fact(DisplayName = "未启用 Spooling 时不发送数据编码头")]
    public async Task NoEncodingHeaderWithoutSpooling()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson(SpooledPage($$"""{ "type": "inline", "data": "{{InlineData("[]")}}" }"""));
        var connection = TestHelper.CreateOpenConnection(handler, ";Protocol=Trino");

        await ReadAsync(connection, "select 1");

        var request = handler.Requests[0];
        Assert.False(request.Headers.Contains(PrestoHeaders.QueryDataEncoding(PrestoProtocol.Trino)));
    }

    [Fact(DisplayName = "内嵌与对象存储分段按顺序拼接，下载后确认")]
    public async Task InlineAndSpooledSegments()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson(SpooledPage($$"""
            { "type": "inline", "data": "{{InlineData("[[1,\"a\"]]")}}" },
            { "type": "spooled", "uri": "http://storage.local/seg1", "ackUri": "http://storage.local/ack1", "headers": { "X-Signature": ["abc"] } },
            { "type": "inline", "data": "{{InlineData("[[3,\"c\"]]")}}" }
            """));
        handler.EnqueueText("""[[2,"b"]]""");
        handler.EnqueueText("");
        var connection = TestHelper.CreateOpenConnection(handler, ";Protocol=Trino;Spooling=true");

        var rows = await ReadAsync(connection, "select * from t");

        Assert.Equal(["1:a", "2:b", "3:c"], rows);

        var submit = handler.Requests[0];
        Assert.Equal("json", submit.Headers.GetValues(PrestoHeaders.QueryDataEncoding(PrestoProtocol.Trino)).Single());

        var download = handler.Requests[1];
        Assert.Equal(HttpMethod.Get, download.Method);
        Assert.Equal("http://storage.local/seg1", download.RequestUri!.AbsoluteUri);
        Assert.Equal("abc", download.Headers.GetValues("X-Signature").Single());
        Assert.Equal("identity", download.Headers.GetValues("Accept-Encoding").Single());
        Assert.False(download.Headers.Contains("X-Trino-User"));
        Assert.False(download.Headers.Contains("Authorization"));

        var ack = handler.Requests[2];
        Assert.Equal(HttpMethod.Get, ack.Method);
        Assert.Equal("http://storage.local/ack1", ack.RequestUri!.AbsoluteUri);
        Assert.Equal("abc", ack.Headers.GetValues("X-Signature").Single());
    }

    [Fact(DisplayName = "分段自带的 Accept-Encoding 不被覆盖")]
    public async Task SegmentHeadersArePreserved()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson(SpooledPage("""
            { "type": "spooled", "uri": "/seg1", "headers": { "Accept-Encoding": ["gzip"], "X-A": "text", "X-B": ["v1", "v2"] } }
            """));
        handler.EnqueueText("""[[1,"a"]]""");
        var connection = TestHelper.CreateOpenConnection(handler, ";Protocol=Trino;Spooling=true");

        var rows = await ReadAsync(connection, "select * from t");

        Assert.Equal(["1:a"], rows);
        var download = handler.Requests[1];
        Assert.Equal("http://localhost:8080/seg1", download.RequestUri!.AbsoluteUri);
        Assert.Equal("gzip", download.Headers.GetValues("Accept-Encoding").Single());
        Assert.False(download.Headers.Contains("X-A"));
        Assert.Equal(["v1", "v2"], download.Headers.GetValues("X-B"));
    }

    [Fact(DisplayName = "确认请求失败不影响查询结果")]
    public async Task AcknowledgeFailureIsIgnored()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson(SpooledPage("""
            { "type": "spooled", "uri": "http://storage.local/seg1", "ackUri": "http://storage.local/ack1" }
            """));
        handler.EnqueueText("""[[1,"a"]]""");
        handler.EnqueueException(new HttpRequestException("ack failed"));
        var connection = TestHelper.CreateOpenConnection(handler, ";Protocol=Trino;Spooling=true");

        var rows = await ReadAsync(connection, "select * from t");

        Assert.Equal(["1:a"], rows);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact(DisplayName = "缺少确认地址时跳过确认请求")]
    public async Task MissingAckUriIsSkipped()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson(SpooledPage("""{ "type": "spooled", "uri": "http://storage.local/seg1" }"""));
        handler.EnqueueText("""[[1,"a"]]""");
        var connection = TestHelper.CreateOpenConnection(handler, ";Protocol=Trino;Spooling=true");

        var rows = await ReadAsync(connection, "select * from t");

        Assert.Equal(["1:a"], rows);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact(DisplayName = "轮询请求不重复发送数据编码头")]
    public async Task FetchDoesNotSendEncodingHeader()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson("""
            { "id": "q1", "columns": [{ "name": "id", "type": "integer" }], "data": [], "nextUri": "/v1/statement/q1/1" }
            """);
        handler.EnqueueJson($$"""
            { "id": "q1", "data": { "encoding": "json", "segments": [{ "type": "inline", "data": "{{InlineData("[]")}}" }] } }
            """);
        var connection = TestHelper.CreateOpenConnection(handler, ";Protocol=Trino;Spooling=true");

        var rows = await ReadAsync(connection, "select * from t");

        Assert.Empty(rows);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(HttpMethod.Get, handler.Requests[1].Method);
        Assert.False(handler.Requests[1].Headers.Contains(PrestoHeaders.QueryDataEncoding(PrestoProtocol.Trino)));
    }

    [Fact(DisplayName = "不支持的编码抛出驱动异常")]
    public async Task UnsupportedEncoding()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson(SpooledPage("""{ "type": "inline", "data": "W10=" }""", "json+zstd"));
        var connection = TestHelper.CreateOpenConnection(handler, ";Protocol=Trino;Spooling=true");

        var ex = await Assert.ThrowsAsync<PrestoException>(() => ReadAsync(connection, "select 1"));
        Assert.Contains("json+zstd", ex.Message);
    }

    [Fact(DisplayName = "缺少编码按不支持处理")]
    public async Task MissingEncoding()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson("""
            { "id": "q1", "columns": [{ "name": "id", "type": "integer" }], "data": { "segments": [{ "type": "inline", "data": "W10=" }] } }
            """);
        var connection = TestHelper.CreateOpenConnection(handler, ";Protocol=Trino;Spooling=true");

        await Assert.ThrowsAsync<PrestoException>(() => ReadAsync(connection, "select 1"));
    }

    [Fact(DisplayName = "未知分段类型抛出驱动异常")]
    public async Task UnknownSegmentType()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson(SpooledPage("""{ "type": "unknown" }"""));
        var connection = TestHelper.CreateOpenConnection(handler, ";Protocol=Trino;Spooling=true");

        var ex = await Assert.ThrowsAsync<PrestoException>(() => ReadAsync(connection, "select 1"));
        Assert.Contains("unknown", ex.Message);
    }

    [Fact(DisplayName = "空内嵌分段返回空行")]
    public async Task EmptyInlineSegment()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson(SpooledPage("""{ "type": "inline", "data": "" }"""));
        var connection = TestHelper.CreateOpenConnection(handler, ";Protocol=Trino;Spooling=true");

        var rows = await ReadAsync(connection, "select 1");

        Assert.Empty(rows);
    }

    [Fact(DisplayName = "内嵌分段缺少数据视为非法分段")]
    public async Task InlineSegmentWithoutData()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson(SpooledPage("""{ "type": "inline" }"""));
        var connection = TestHelper.CreateOpenConnection(handler, ";Protocol=Trino;Spooling=true");

        await Assert.ThrowsAsync<PrestoException>(() => ReadAsync(connection, "select 1"));
    }

    [Fact(DisplayName = "内嵌分段 Base64 非法抛出驱动异常")]
    public async Task InlineSegmentInvalidBase64()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson(SpooledPage("""{ "type": "inline", "data": "!!!" }"""));
        var connection = TestHelper.CreateOpenConnection(handler, ";Protocol=Trino;Spooling=true");

        await Assert.ThrowsAsync<PrestoException>(() => ReadAsync(connection, "select 1"));
    }

    [Fact(DisplayName = "对象存储分段缺少地址视为非法分段")]
    public async Task SpooledSegmentWithoutUri()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson(SpooledPage("""{ "type": "spooled", "ackUri": "http://localhost/ack" }"""));
        var connection = TestHelper.CreateOpenConnection(handler, ";Protocol=Trino;Spooling=true");

        await Assert.ThrowsAsync<PrestoException>(() => ReadAsync(connection, "select 1"));
    }

    [Fact(DisplayName = "分段下载失败映射为驱动异常")]
    public async Task SegmentDownloadFailure()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson(SpooledPage("""{ "type": "spooled", "uri": "http://storage.local/seg1" }"""));
        handler.EnqueueText("gone", HttpStatusCode.NotFound);
        var connection = TestHelper.CreateOpenConnection(handler, ";Protocol=Trino;Spooling=true");

        var ex = await Assert.ThrowsAsync<PrestoException>(() => ReadAsync(connection, "select 1"));
        Assert.Equal(404, ex.HttpStatusCode);
    }

    [Fact(DisplayName = "分段内容不是行数组时抛出驱动异常")]
    public async Task SegmentBodyNotArray()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson(SpooledPage("""{ "type": "spooled", "uri": "http://storage.local/seg1" }"""));
        handler.EnqueueJson("""{ "not": "array" }""");
        var connection = TestHelper.CreateOpenConnection(handler, ";Protocol=Trino;Spooling=true");

        await Assert.ThrowsAsync<PrestoException>(() => ReadAsync(connection, "select 1"));
    }

    [Fact(DisplayName = "行数组解析边界")]
    public void ParseRowsEdges()
    {
        Assert.Empty(PrestoJson.ParseRows(null));
        Assert.Empty(PrestoJson.ParseRows("  "));
        Assert.Throws<PrestoException>(() => PrestoJson.ParseRows("""{ "a": 1 }"""));
        Assert.Equal(2, PrestoJson.ParseRows("""[[1],[2]]""").Count);
    }

    [Fact(DisplayName = "分段请求头解析忽略非数组值")]
    public void ParseSegmentHeaders()
    {
        var page = PrestoJson.ParseResults("""
            {
              "id": "q1",
              "columns": [{ "name": "id", "type": "integer" }],
              "data": {
                "encoding": "json",
                "segments": [{ "type": "spooled", "uri": "http://storage.local/seg1", "headers": { "X-A": "text", "X-B": ["v1", 2] } }]
              }
            }
            """);

        var segment = Assert.Single(page.Segments);
        Assert.False(segment.Headers.ContainsKey("X-A"));
        Assert.Equal(["v1", "2"], segment.Headers["X-B"]);
        Assert.Equal("json", page.DataEncoding);
        Assert.Empty(page.Data);
    }
}
