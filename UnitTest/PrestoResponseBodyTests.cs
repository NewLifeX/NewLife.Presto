using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NewLife.Presto;

namespace UnitTest;

/// <summary>响应体读取测试：无长度响应扩容与读取异常释放</summary>
public class PrestoResponseBodyTests
{
    [Fact(DisplayName = "无 Content-Length 的大响应按需扩容")]
    public void LargeBodyGrows()
    {
        var handler = new FakePrestoHandler();
        var rows = String.Join(",", Enumerable.Range(0, 800).Select(e => $"[\"{e}\"]"));
        var json = "{\"columns\":[{\"name\":\"s\",\"type\":\"varchar\"}],\"data\":[" + rows + "]}";
        Assert.True(json.Length > 4096);

        handler.Enqueue((request, token) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new NonSeekableContent(Encoding.UTF8.GetBytes(json)),
        }));

        using var connection = TestHelper.CreateOpenConnection(handler);
        using var command = connection.CreateCommand("select s");
        using var reader = command.ExecuteReader();

        var count = 0;
        while (reader.Read()) count++;

        Assert.Equal(800, count);
    }

    [Fact(DisplayName = "响应流读取异常时保留原异常")]
    public void ReadFailure()
    {
        var handler = new FakePrestoHandler();
        handler.Enqueue((request, token) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ThrowingContent(),
        }));

        using var connection = TestHelper.CreateOpenConnection(handler);
        using var command = connection.CreateCommand("select 1");

        var ex = Assert.Throws<HttpRequestException>(() => command.ExecuteScalar());
        Assert.IsType<IOException>(ex.InnerException);
    }

    [Fact(DisplayName = "空响应体解析与重复释放")]
    public async Task EmptyBodyLifecycle()
    {
        using var none = await PrestoResponseBody.ReadAsync(null!, CancellationToken.None);
        Assert.Equal(String.Empty, none.GetText());
        Assert.Empty(none.ParseRows());
        Assert.Throws<PrestoException>(() => none.ParseResults(false));
        none.Dispose();

        using var empty = await PrestoResponseBody.ReadAsync(new HttpResponseMessage(HttpStatusCode.OK), CancellationToken.None);
        Assert.Equal(String.Empty, empty.GetText());
        empty.Dispose();
        empty.Dispose();
    }

    private sealed class NonSeekableContent(Byte[] data) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(data, 0, data.Length);

        protected override Boolean TryComputeLength(out Int64 length)
        {
            length = 0;

            return false;
        }
    }

    private sealed class ThrowingContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => Task.FromException(new IOException("boom"));

        protected override Boolean TryComputeLength(out Int64 length)
        {
            length = 0;

            return false;
        }
    }
}
