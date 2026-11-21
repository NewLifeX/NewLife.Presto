#if NET6_0_OR_GREATER
using System;
using System.Data;
using System.Data.Common;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using NewLife.Presto;

namespace UnitTest;

/// <summary>批量命令测试</summary>
public class PrestoBatchTests
{
    [Fact(DisplayName = "批量执行汇总受影响行数")]
    public void NonQuerySumsRecordsAffected()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson("""{ "id": "q1", "updateType": "INSERT", "updateCount": 2 }""");
        handler.EnqueueJson("""{ "id": "q2", "updateType": "DELETE", "updateCount": 3 }""");
        var connection = TestHelper.CreateOpenConnection(handler);

        using var batch = connection.CreateBatch();
        var first = batch.CreateBatchCommand();
        first.CommandText = "insert into t values (1)";
        batch.BatchCommands.Add(first);

        var second = batch.CreateBatchCommand();
        second.CommandText = "delete from t";
        batch.BatchCommands.Add(second);

        Assert.Equal(5, batch.ExecuteNonQuery());
        Assert.Equal(2, first.RecordsAffected);
        Assert.Equal(3, second.RecordsAffected);
        Assert.Equal(2, handler.Bodies.Count);
    }

    [Fact(DisplayName = "无法判断行数时批次返回 -1")]
    public void NonQueryUnknownReturnsMinusOne()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson("""{ "id": "q1", "columns": [{"name":"n","type":"integer"}], "data": [["1"]] }""");
        var connection = TestHelper.CreateOpenConnection(handler);

        using var batch = connection.CreateBatch();
        var item = batch.CreateBatchCommand();
        item.CommandText = "select 1";
        batch.BatchCommands.Add(item);

        Assert.Equal(-1, batch.ExecuteNonQuery());
    }

    [Fact(DisplayName = "批量标量返回最后一条命令")]
    public async Task ScalarUsesLastCommand()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson("""{ "id": "q1", "updateType": "INSERT", "updateCount": 1 }""");
        handler.EnqueueJson("""{ "id": "q2", "columns": [{"name":"n","type":"integer"}], "data": [["42"]] }""");
        var connection = TestHelper.CreateOpenConnection(handler);

        using var batch = connection.CreateBatch();
        batch.BatchCommands.Add(new PrestoBatchCommand { CommandText = "insert into t values (1)" });
        batch.BatchCommands.Add(new PrestoBatchCommand { CommandText = "select 42" });

        Assert.Equal(42, await batch.ExecuteScalarAsync(CancellationToken.None));
    }

    [Fact(DisplayName = "批量读取器返回最后一条命令的结果")]
    public async Task ReaderUsesLastCommand()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson("""{ "id": "q1", "updateType": "INSERT", "updateCount": 1 }""");
        handler.EnqueueJson("""{ "id": "q2", "columns": [{"name":"name","type":"varchar"}], "data": [["alpha"],["beta"]] }""");
        var connection = TestHelper.CreateOpenConnection(handler);

        using var batch = connection.CreateBatch();
        batch.BatchCommands.Add(new PrestoBatchCommand { CommandText = "insert into t values (1)" });
        batch.BatchCommands.Add(new PrestoBatchCommand { CommandText = "select name from t" });

        using var reader = await batch.ExecuteReaderAsync(CommandBehavior.Default, CancellationToken.None);
        Assert.Equal(1, reader.FieldCount);
        Assert.True(await reader.ReadAsync(CancellationToken.None));
        Assert.Equal("alpha", reader.GetString(0));
        Assert.True(await reader.ReadAsync(CancellationToken.None));
        Assert.Equal("beta", reader.GetString(0));
        Assert.False(await reader.ReadAsync(CancellationToken.None));
    }

    [Fact(DisplayName = "批量命令绑定参数")]
    public void BindsParameters()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson("""{ "id": "q1", "columns": [{"name":"n","type":"integer"}], "data": [["7"]] }""");
        var connection = TestHelper.CreateOpenConnection(handler);

        using var batch = connection.CreateBatch();
        var item = batch.CreateBatchCommand();
        item.CommandText = "select @x";
        item.Parameters.Add(new PrestoParameter("@x", 7));
        batch.BatchCommands.Add(item);

        Assert.Equal(7, batch.ExecuteScalar());
        Assert.Equal("select 7", handler.LastBody);
    }

    [Fact(DisplayName = "空批次的行为")]
    public void EmptyBatch()
    {
        var handler = new FakePrestoHandler();
        var connection = TestHelper.CreateOpenConnection(handler);

        using var batch = connection.CreateBatch();
        Assert.Equal(0, batch.ExecuteNonQuery());
        Assert.Null(batch.ExecuteScalar());
        Assert.Throws<InvalidOperationException>(() => batch.ExecuteReader());
    }

    [Fact(DisplayName = "批次取消传播到请求")]
    public async Task CancelPropagates()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueDelay(TimeSpan.FromSeconds(5), """{ "id": "q1", "columns": [{"name":"n","type":"integer"}], "data": [["1"]] }""");
        var connection = TestHelper.CreateOpenConnection(handler);

        using var batch = connection.CreateBatch();
        batch.BatchCommands.Add(new PrestoBatchCommand { CommandText = "select 1" });

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => batch.ExecuteNonQueryAsync(cts.Token));
    }

    [Fact(DisplayName = "批次前置校验与不支持能力")]
    public void Validation()
    {
        var disconnected = new PrestoBatch();
        disconnected.BatchCommands.Add(new PrestoBatchCommand { CommandText = "select 1" });
        Assert.Throws<InvalidOperationException>(() => disconnected.ExecuteNonQuery());

        var handler = new FakePrestoHandler();
        var connection = TestHelper.CreateOpenConnection(handler);
        using var batch = connection.CreateBatch();
        batch.BatchCommands.Add(new PrestoBatchCommand { CommandText = "" });
        Assert.Throws<InvalidOperationException>(() => batch.ExecuteNonQuery());

        using var other = connection.CreateBatch();
        var command = other.CreateBatchCommand();
        command.CommandType = CommandType.Text;
        Assert.Equal(CommandType.Text, command.CommandType);
        Assert.Throws<NotSupportedException>(() => command.CommandType = CommandType.StoredProcedure);

        using var transaction = new FakeTransaction();
        Assert.Throws<NotSupportedException>(() => batch.Transaction = transaction);
    }

    [Fact(DisplayName = "命令集合支持增删改查")]
    public void CommandCollection()
    {
        var handler = new FakePrestoHandler();
        var connection = TestHelper.CreateOpenConnection(handler);
        using var batch = connection.CreateBatch();

        var a = new PrestoBatchCommand { CommandText = "select 1" };
        var b = new PrestoBatchCommand { CommandText = "select 2" };
        var c = new PrestoBatchCommand { CommandText = "select 3" };

        batch.BatchCommands.Add(a);
        batch.BatchCommands.Add(c);
        batch.BatchCommands.Insert(1, b);

        Assert.Equal(3, batch.BatchCommands.Count);
        Assert.Same(b, batch.BatchCommands[1]);
        Assert.Equal(1, batch.BatchCommands.IndexOf(b));
        System.Collections.Generic.ICollection<DbBatchCommand> commands = batch.BatchCommands;
        Assert.True(commands.Contains(b));
        Assert.Contains(b, batch.BatchCommands);
        Assert.False(batch.BatchCommands.IsReadOnly);

        var array = new DbBatchCommand[3];
        batch.BatchCommands.CopyTo(array, 0);
        Assert.Same(a, array[0]);

        batch.BatchCommands[1] = c;
        Assert.Same(c, batch.BatchCommands[1]);
        batch.BatchCommands.RemoveAt(1);
        Assert.Equal(2, batch.BatchCommands.Count);
        Assert.True(batch.BatchCommands.Remove(a));

        var names = new List<String>();
        foreach (var item in batch.BatchCommands) names.Add(item.CommandText);
        Assert.Equal(["select 3"], names);

        batch.BatchCommands.Clear();
        Assert.Empty(batch.BatchCommands);
    }

    [Fact(DisplayName = "批次超时为零与关闭连接时校验")]
    public void CancellationEdges()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson("""{ "id": "q1", "updateType": "INSERT", "updateCount": 1 }""");
        var connection = TestHelper.CreateOpenConnection(handler);

        using var batch = connection.CreateBatch();
        batch.Timeout = 0;
        batch.BatchCommands.Add(new PrestoBatchCommand { CommandText = "insert into t values (1)" });
        Assert.Equal(1, batch.ExecuteNonQuery());

        connection.Close();
        using var closed = connection.CreateBatch();
        closed.BatchCommands.Add(new PrestoBatchCommand { CommandText = "select 1" });
        Assert.Throws<InvalidOperationException>(() => closed.ExecuteNonQuery());
    }

    [Fact(DisplayName = "批次标量空列与空行")]
    public void ScalarEdges()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson("""{ "id": "q1", "columns": [], "data": [] }""");
        handler.EnqueueJson("""{ "id": "q2", "columns": [{"name":"n","type":"integer"}], "data": [] }""");
        var connection = TestHelper.CreateOpenConnection(handler);

        using var batch = connection.CreateBatch();
        batch.BatchCommands.Add(new PrestoBatchCommand { CommandText = "select 1 where 1=0" });

        Assert.Null(batch.ExecuteScalar());
        Assert.Null(batch.ExecuteScalar());
    }

    [Fact(DisplayName = "批次命令文本空值与类型校验")]
    public void CommandTextEdges()
    {
        var command = new PrestoBatchCommand();
        command.CommandText = null!;
        Assert.Equal(String.Empty, command.CommandText);
        Assert.Equal(CommandType.Text, command.CommandType);
        Assert.True(command.CanCreateParameter);
    }

    [Fact(DisplayName = "批次连接与事务属性")]
    public void ConnectionProperties()
    {
        using var batch = new PrestoBatch();
        Assert.Null(batch.Connection);
        Assert.Null(batch.Transaction);
        batch.Transaction = null;
        Assert.Throws<ArgumentException>(() => batch.Connection = new FakeDbConnection());
    }

    [Fact(DisplayName = "批次读取失败释放执行范围")]
    public void ReaderFailureDisposesScope()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson("""{ "id": "q1", "error": { "message": "boom", "errorCode": 1 } }""");
        var connection = TestHelper.CreateOpenConnection(handler);

        using var batch = connection.CreateBatch();
        batch.BatchCommands.Add(new PrestoBatchCommand { CommandText = "select 1" });

        Assert.Throws<PrestoException>(() => batch.ExecuteReader());
        Assert.True(connection.CanCreateBatch);
    }

    [Fact(DisplayName = "工厂可创建批量命令")]
    public async Task FactoryCreatesBatch()
    {
        using var batch = PrestoClientFactory.Instance.CreateBatch();
        Assert.IsType<PrestoBatch>(batch);
        Assert.True(batch.CreateBatchCommand().CanCreateParameter);
        Assert.IsType<PrestoParameter>(batch.CreateBatchCommand().CreateParameter());

        using var empty = new PrestoBatch();
        Assert.Equal(30, empty.Timeout);
        empty.Timeout = 5;
        Assert.Equal(5, empty.Timeout);
        empty.Prepare();
        await empty.PrepareAsync(CancellationToken.None);
    }

    private sealed class FakeTransaction : DbTransaction
    {
        public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;

        protected override DbConnection? DbConnection => null;

        public override void Commit() { }

        public override void Rollback() { }
    }

    private sealed class FakeDbConnection : DbConnection
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
#endif
