using System;
using System.Data;
using System.Data.Common;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NewLife.Presto;

namespace UnitTest;

/// <summary>命令执行测试</summary>
public class PrestoCommandTests
{
    [Fact(DisplayName = "ExecuteScalar 同步与异步")]
    public async Task ExecuteScalar()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson("""{ "id": "q1", "columns": [{"name":"n","type":"integer"}], "data": [["42"]] }""");
        handler.EnqueueJson("""{ "id": "q2", "columns": [{"name":"n","type":"integer"}], "data": [["43"]] }""");
        var connection = TestHelper.CreateOpenConnection(handler);

        using var command = connection.CreateCommand();
        command.CommandText = "select 42";

        Assert.Equal(42, command.ExecuteScalar());
        Assert.Equal(43, await command.ExecuteScalarAsync(CancellationToken.None));
        Assert.Equal("q2", command.LastQueryId);
    }

    [Fact(DisplayName = "释放后仍可读取最后一次查询编号")]
    public void LastQueryIdAfterDispose()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson("""{ "id": "q9", "columns": [{"name":"n","type":"integer"}], "data": [["1"]] }""");
        var connection = TestHelper.CreateOpenConnection(handler);
        var command = connection.CreateCommand();
        command.CommandText = "select 1";

        Assert.Equal(1, command.ExecuteScalar());
        command.Dispose();

        Assert.Equal("q9", command.LastQueryId);
    }

    [Fact(DisplayName = "ExecuteScalar 无数据返回空")]
    public void ExecuteScalarNoRows()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson("""{ "id": "q1", "columns": [], "data": [] }""");
        var connection = TestHelper.CreateOpenConnection(handler);
        using var command = connection.CreateCommand();
        command.CommandText = "select 1 where 1=0";

        Assert.Null(command.ExecuteScalar());
    }

    [Fact(DisplayName = "ExecuteNonQuery 使用 updateCount")]
    public void NonQueryUpdateCount()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson("""{ "id": "q1", "updateType": "INSERT", "updateCount": 3 }""");
        var connection = TestHelper.CreateOpenConnection(handler);
        using var command = connection.CreateCommand();
        command.CommandText = "insert into t values (1)";

        Assert.Equal(3, command.ExecuteNonQuery());
    }

    [Fact(DisplayName = "ExecuteNonQuery 使用 rows 列")]
    public void NonQueryRowsColumn()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson("""{ "id": "q1", "columns": [{"name":"rows","type":"bigint"}], "data": [["5"]] }""");
        var connection = TestHelper.CreateOpenConnection(handler);
        using var command = connection.CreateCommand();
        command.CommandText = "delete from t";

        Assert.Equal(5, command.ExecuteNonQuery());
    }

    [Fact(DisplayName = "异步 ExecuteNonQuery 使用 rows 列")]
    public async Task NonQueryRowsColumnAsync()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson("""{ "id": "q1", "columns": [{"name":"rows","type":"bigint"}], "data": [["5"]] }""");
        var connection = TestHelper.CreateOpenConnection(handler);
        using var command = connection.CreateCommand("delete from t");

        Assert.Equal(5, await command.ExecuteNonQueryAsync(CancellationToken.None));
    }

    [Fact(DisplayName = "异步首包无列定义时继续拉取")]
    public async Task FetchToColumnsAsync()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson("""{ "id": "q1", "nextUri": "http://localhost:8080/v1/statement/q1/1" }""");
        handler.EnqueueJson("""{ "id": "q1", "columns": [{"name":"n","type":"integer"}], "data": [["1"]] }""");
        var connection = TestHelper.CreateOpenConnection(handler);
        using var command = connection.CreateCommand("select 1");

        using var reader = await command.ExecuteReaderAsync(CancellationToken.None);

        Assert.Equal(1, reader.FieldCount);
        Assert.True(await reader.ReadAsync(CancellationToken.None));
        Assert.Equal(1, reader.GetInt32(0));
    }

    [Fact(DisplayName = "异步 SchemaOnly 取列后取消查询")]
    public async Task SchemaOnlyAsync()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson("""{ "id": "q1", "columns": [{"name":"n","type":"integer"}], "data": [], "nextUri": "http://localhost:8080/v1/statement/q1/1" }""");
        handler.EnqueueJson("""{ "id": "q1" }""");
        var connection = TestHelper.CreateOpenConnection(handler);
        using var command = connection.CreateCommand("select 1");

        using var reader = await command.ExecuteReaderAsync(CommandBehavior.SchemaOnly, CancellationToken.None);

        Assert.Equal(1, reader.FieldCount);
        Assert.Contains(handler.Requests, e => e.Method == HttpMethod.Delete);
    }

    [Fact(DisplayName = "异步提交超时映射为命令超时")]
    public async Task SubmitTimeoutAsync()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueDelay(TimeSpan.FromSeconds(5), """{ "id": "q1", "columns": [], "data": [] }""");
        var connection = TestHelper.CreateOpenConnection(handler);
        var command = new PrestoCommand("select 1", connection) { CommandTimeout = 1 };

        var ex = await Assert.ThrowsAsync<PrestoException>(() => command.ExecuteScalarAsync(CancellationToken.None));

        Assert.Equal(NewLife.Localization.Translate("Command timeout of {0} seconds expired.", 1), ex.Message);
    }

    [Fact(DisplayName = "ExecuteNonQuery updateType 无计数返回零")]
    public void NonQueryUpdateTypeZero()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson("""{ "id": "q1", "updateType": "INSERT" }""");
        var connection = TestHelper.CreateOpenConnection(handler);
        using var command = connection.CreateCommand();
        command.CommandText = "insert into t values (1)";

        Assert.Equal(0, command.ExecuteNonQuery());
    }

    [Fact(DisplayName = "ExecuteNonQuery 无法判断返回 -1")]
    public void NonQueryUnknown()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson("""{ "id": "q1", "columns": [{"name":"n","type":"integer"}], "data": [["1"]] }""");
        var connection = TestHelper.CreateOpenConnection(handler);
        using var command = connection.CreateCommand();
        command.CommandText = "select 1";

        Assert.Equal(-1, command.ExecuteNonQuery());
    }

    [Fact(DisplayName = "参数在提交前完成替换")]
    public void BindParameters()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson("""{ "id": "q1", "columns": [{"name":"n","type":"integer"}], "data": [["7"]] }""");
        var connection = TestHelper.CreateOpenConnection(handler);
        using var command = connection.CreateCommand();
        command.CommandText = "select @x";
        command.Parameters.Add(new PrestoParameter("@x", 7));

        Assert.Equal(7, command.ExecuteScalar());
        Assert.Equal("select 7", handler.LastBody);
    }

    [Fact(DisplayName = "命令执行前置校验")]
    public void Validation()
    {
        var command = new PrestoCommand("select 1");
        Assert.Throws<InvalidOperationException>(() => command.ExecuteScalar());

        var handler = new FakePrestoHandler();
        var connection = TestHelper.CreateOpenConnection(handler);
        using var empty = connection.CreateCommand();
        Assert.Throws<InvalidOperationException>(() => empty.ExecuteScalar());

        connection.Close();
        using var closed = new PrestoCommand("select 1", connection);
        Assert.Throws<InvalidOperationException>(() => closed.ExecuteScalar());
    }

    [Fact(DisplayName = "命令类型仅支持 Text")]
    public void CommandTypeValidation()
    {
        using var command = new PrestoCommand();

        command.CommandType = CommandType.Text;
        Assert.Throws<NotSupportedException>(() => command.CommandType = CommandType.StoredProcedure);
        Assert.Throws<NotSupportedException>(() => command.CommandType = CommandType.TableDirect);
    }

    [Fact(DisplayName = "不支持的 CommandBehavior 抛出异常")]
    public void CommandBehaviorValidation()
    {
        var handler = new FakePrestoHandler();
        var connection = TestHelper.CreateOpenConnection(handler);
        using var command = new PrestoCommand("select 1", connection);

        Assert.Throws<NotSupportedException>(() => command.ExecuteReader(CommandBehavior.KeyInfo));
    }

    [Fact(DisplayName = "SchemaOnly 只取列信息并取消查询")]
    public void SchemaOnly()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson("""{ "id": "q1", "columns": [{"name":"n","type":"integer"}], "data": [], "nextUri": "http://localhost:8080/v1/statement/q1/1" }""");
        handler.EnqueueJson("""{ "id": "q1" }""");
        var connection = TestHelper.CreateOpenConnection(handler);
        using var command = new PrestoCommand("select 1", connection);

        using var reader = command.ExecuteReader(CommandBehavior.SchemaOnly);

        Assert.Equal(1, reader.FieldCount);
        Assert.False(reader.HasRows);
        Assert.False(reader.Read());
        reader.Close();

        Assert.Equal(HttpMethod.Delete, handler.Requests[1].Method);
    }

    [Fact(DisplayName = "SingleRow 只返回首行并取消剩余查询")]
    public async Task SingleRow()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson("""{ "id": "q1", "columns": [{"name":"n","type":"integer"}], "data": [["1"],["2"]], "nextUri": "http://localhost:8080/v1/statement/q1/1" }""");
        handler.EnqueueJson("""{ "id": "q1" }""");
        var connection = TestHelper.CreateOpenConnection(handler);
        using var command = new PrestoCommand("select 1", connection);

        using var reader = (PrestoDataReader)command.ExecuteReader(CommandBehavior.SingleRow);

        Assert.True(reader.Read());
        Assert.Equal(1, reader.GetInt32(0));
        Assert.False(reader.Read());
        await reader.CancelTask!;

        Assert.Equal(HttpMethod.Delete, handler.Requests[1].Method);
    }

    [Fact(DisplayName = "默认配置下服务端返回压缩内容仍可解析")]
    public void HandlesCompressedResponseByDefault()
    {
        const String json = """{ "id": "q1", "columns": [{"name":"n","type":"integer"}], "data": [["7"]] }""";
        var handler = new FakePrestoHandler();
        handler.Enqueue((request, token) =>
        {
            using var output = new System.IO.MemoryStream();
            using (var gzip = new System.IO.Compression.GZipStream(output, System.IO.Compression.CompressionLevel.Fastest, true))
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(json);
                gzip.Write(bytes, 0, bytes.Length);
            }

            var content = new ByteArrayContent(output.ToArray());
            content.Headers.ContentEncoding.Add("gzip");
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = content });
        });

        var connection = TestHelper.CreateOpenConnection(handler);
        using var command = new PrestoCommand("select 7", connection);

        Assert.Equal(7, command.ExecuteScalar());
    }

    [Fact(DisplayName = "终态数据页提前关闭不再发送 DELETE")]
    public void SkipCancelWhenFinished()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson("""{ "id": "q1", "columns": [{"name":"n","type":"integer"}], "data": [["1"]], "nextUri": "http://localhost:8080/v1/statement/q1/1", "stats": { "state": "FINISHED" } }""");
        var connection = TestHelper.CreateOpenConnection(handler);
        using var command = new PrestoCommand("select 1", connection);

        using (var reader = command.ExecuteReader(CommandBehavior.SingleRow))
        {
            Assert.True(reader.Read());
            Assert.Equal(1, reader.GetInt32(0));
        }

        Assert.DoesNotContain(handler.Requests, e => e.Method == HttpMethod.Delete);
    }

    [Fact(DisplayName = "CloseConnection 随读取器关闭连接")]
    public void CloseConnectionBehavior()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson("""{ "id": "q1", "columns": [{"name":"n","type":"integer"}], "data": [["1"]] }""");
        var connection = TestHelper.CreateOpenConnection(handler);
        using var command = new PrestoCommand("select 1", connection);

        using (var reader = command.ExecuteReader(CommandBehavior.CloseConnection))
        {
            Assert.True(reader.Read());
        }

        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    [Fact(DisplayName = "取消命令发送 DELETE")]
    public async Task CancelCommand()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson("""{ "id": "q1", "columns": [{"name":"n","type":"integer"}], "data": [], "nextUri": "http://localhost:8080/v1/statement/q1/1" }""");
        handler.EnqueueJson("""{ "id": "q1" }""");
        var connection = TestHelper.CreateOpenConnection(handler);
        using var command = new PrestoCommand("select 1", connection);
        using var reader = command.ExecuteReader();

        command.Cancel();
        await Task.Delay(100);

        Assert.Contains(handler.Requests, e => e.Method == HttpMethod.Delete);
    }

    [Fact(DisplayName = "取消空命令不抛异常")]
    public void CancelWithoutExecution()
    {
        using var command = new PrestoCommand();

        command.Cancel();
    }

    [Fact(DisplayName = "命令超时映射为驱动异常")]
    public void CommandTimeout()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueDelay(TimeSpan.FromSeconds(5), """{ "id": "q1", "columns": [], "data": [] }""");
        var connection = TestHelper.CreateOpenConnection(handler);
        using var command = new PrestoCommand("select 1", connection);
        command.CommandTimeout = 1;

        var ex = Assert.Throws<PrestoException>(() => command.ExecuteScalar());

        Assert.Equal(NewLife.Localization.Translate("Command timeout of {0} seconds expired.", 1), ex.Message);
    }

    [Fact(DisplayName = "命令属性默认值与设置")]
    public void CommandProperties()
    {
        var connection = TestHelper.CreateOpenConnection(new FakePrestoHandler());
        using var command = connection.CreateCommand();

        Assert.Equal(0, command.CommandTimeout);
        command.CommandTimeout = 42;
        Assert.Equal(42, command.CommandTimeout);
        command.DesignTimeVisible = true;
        Assert.True(command.DesignTimeVisible);
        command.UpdatedRowSource = UpdateRowSource.FirstReturnedRecord;
        Assert.Equal(UpdateRowSource.FirstReturnedRecord, command.UpdatedRowSource);
        command.Prepare();
        Assert.Null(command.Transaction);
        Assert.Null(command.LastQueryId);

        command.Transaction = null;
        Assert.Throws<NotSupportedException>(() => command.Transaction = new FakeTransaction());

        var parameter = command.CreateParameter();
        Assert.IsType<PrestoParameter>(parameter);
        command.Parameters.Add(parameter);
        Assert.Single(command.Parameters);

        command.CommandText = null!;
        Assert.Equal(String.Empty, command.CommandText);
        Assert.Equal(30, new PrestoCommand().CommandTimeout);
    }

    [Fact(DisplayName = "命令提交失败释放执行上下文")]
    public void ExecuteHttpError()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueText("boom", System.Net.HttpStatusCode.InternalServerError);
        var connection = TestHelper.CreateOpenConnection(handler);
        using var command = new PrestoCommand("select 1", connection);

        var ex = Assert.Throws<PrestoException>(() => command.ExecuteScalar());

        Assert.Equal(500, ex.HttpStatusCode);
    }

    [Fact(DisplayName = "查询结束仍保留查询编号")]
    public void LastQueryIdAfterClose()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson("""{ "id": "q1", "columns": [{"name":"n","type":"integer"}], "data": [["1"]] }""");
        var connection = TestHelper.CreateOpenConnection(handler);
        using var command = new PrestoCommand("select 1", connection);

        Assert.Equal(1, command.ExecuteScalar());
        Assert.Equal("q1", command.LastQueryId);
    }

    [Fact(DisplayName = "rows 列为空时返回 -1")]
    public void NonQueryRowsNull()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson("""{ "id": "q1", "columns": [{"name":"rows","type":"bigint"}], "data": [[null]] }""");
        var connection = TestHelper.CreateOpenConnection(handler);
        using var command = new PrestoCommand("delete from t", connection);

        Assert.Equal(-1, command.ExecuteNonQuery());
    }

    [Fact(DisplayName = "有列但无数据的标量返回空")]
    public void ScalarWithColumnsNoRows()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson("""{ "id": "q1", "columns": [{"name":"n","type":"integer"}], "data": [] }""");
        var connection = TestHelper.CreateOpenConnection(handler);
        using var command = new PrestoCommand("select 1 where 1=0", connection);

        Assert.Null(command.ExecuteScalar());
    }

    [Fact(DisplayName = "异步读取器入口")]
    public async Task ReaderAsyncEntry()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson("""{ "id": "q1", "columns": [{"name":"n","type":"integer"}], "data": [["1"]] }""");
        var connection = TestHelper.CreateOpenConnection(handler);
        using var command = new PrestoCommand("select 1", connection);

        using var reader = await command.ExecuteReaderAsync(CommandBehavior.Default, CancellationToken.None);
        Assert.True(await reader.ReadAsync(CancellationToken.None));
        Assert.Equal(1, reader.GetInt32(0));
    }

    [Fact(DisplayName = "首包无列定义时推进到数据页")]
    public void FirstPageWithoutColumns()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson("""{ "id": "q1", "stats": { "state": "QUEUED" }, "nextUri": "http://localhost:8080/v1/statement/q1/1" }""");
        handler.EnqueueJson("""{ "id": "q1", "columns": [{"name":"n","type":"integer"},{"name":"s","type":"varchar"}], "data": [["1","x"]] }""");
        var connection = TestHelper.CreateOpenConnection(handler);
        using var command = new PrestoCommand("select 1 as n, 'x' as s", connection);

        using var reader = command.ExecuteReader();

        Assert.Equal(2, reader.FieldCount);
        Assert.Equal("n", reader.GetName(0));
        Assert.True(reader.Read());
        Assert.Equal(1, reader.GetInt32(0));
        Assert.Equal("x", reader.GetString(1));
        Assert.False(reader.Read());
    }

    [Fact(DisplayName = "写语句在轮询后才返回 updateCount")]
    public void NonQueryAfterQueuedPage()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson("""{ "id": "q1", "stats": { "state": "QUEUED" }, "nextUri": "http://localhost:8080/v1/statement/q1/1" }""");
        handler.EnqueueJson("""{ "id": "q1", "updateType": "INSERT", "updateCount": 2 }""");
        var connection = TestHelper.CreateOpenConnection(handler);
        using var command = new PrestoCommand("insert into t values (1)", connection);

        Assert.Equal(2, command.ExecuteNonQuery());
    }

    [Fact(DisplayName = "SchemaOnly 首包无列定义时先取列再取消")]
    public void SchemaOnlyAfterQueuedPage()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson("""{ "id": "q1", "nextUri": "http://localhost:8080/v1/statement/q1/1" }""");
        handler.EnqueueJson("""{ "id": "q1", "columns": [{"name":"n","type":"integer"}], "data": [], "nextUri": "http://localhost:8080/v1/statement/q1/2" }""");
        handler.EnqueueJson("""{ "id": "q1" }""");
        var connection = TestHelper.CreateOpenConnection(handler);
        using var command = new PrestoCommand("select 1", connection);

        using var reader = command.ExecuteReader(CommandBehavior.SchemaOnly);

        Assert.Equal(1, reader.FieldCount);
        Assert.False(reader.HasRows);
        Assert.Equal(HttpMethod.Delete, handler.Requests[2].Method);
    }

    private sealed class FakeTransaction : DbTransaction
    {
        public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;

        public override void Commit() { }

        public override void Rollback() { }

        protected override DbConnection? DbConnection => null;
    }
}
