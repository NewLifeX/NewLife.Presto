using System;
using System.Data;
using System.Linq;
using NewLife;
using NewLife.Presto;

namespace UnitTest;

/// <summary>真库集成测试。未配置连接串时跳过</summary>
/// <remarks>
/// 覆盖协议主干：提交与轮询、类型取值、参数替换、多页结果、元数据集合与错误映射。
/// 连接串通过 <c>PRESTO_TEST_CONNSTR</c> / <c>TRINO_TEST_CONNSTR</c> 提供，默认使用 tpch 目录。
/// </remarks>
public class IntegrationTests
{
    [Fact(DisplayName = "真库连接与查询（未配置跳过）")]
    public void RealServer()
    {
        Run(Environment.GetEnvironmentVariable("PRESTO_TEST_CONNSTR"));
        Run(Environment.GetEnvironmentVariable("TRINO_TEST_CONNSTR"));
    }

    private static void Run(String? connectionString)
    {
        if (connectionString.IsNullOrWhiteSpace()) return;

        using var connection = new PrestoConnection(connectionString);
        connection.Open();

        ScalarAndTypes(connection);
        Parameters(connection);
        Paging(connection);
        Metadata(connection);
        Diagnostics(connection);
        ComplexTypes(connection);
        SpoolingFallback(connection);
        ErrorMapping(connection);
    }

    private static void ScalarAndTypes(PrestoConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "select 1 as n, 'x' as s";
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(1, reader.GetInt32(0));
        Assert.Equal("x", reader.GetString(1));
        Assert.False(reader.Read());

        Assert.NotNull(connection.ServerVersion);
    }

    private static void Parameters(PrestoConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "select count(*) from tpch.tiny.nation where nationkey > @minKey";
        command.Parameters.Add(new PrestoParameter("@minKey", 0));

        var value = command.ExecuteScalar();
        Assert.Equal(24L, value);
    }

    private static void Paging(PrestoConnection connection)
    {
        // orders 共 15000 行，必然跨多个结果页，用于验证 nextUri 轮询与逐页消费
        using var command = connection.CreateCommand();
        command.CommandText = "select orderkey, custkey, totalprice from tpch.tiny.orders";
        using var reader = command.ExecuteReader();

        var count = 0;
        while (reader.Read()) count++;

        Assert.Equal(15000, count);
        Assert.False(command.LastQueryId.IsNullOrEmpty());
    }

    private static void Metadata(PrestoConnection connection)
    {
        Assert.True(connection.GetSchema("DataTypes").Rows.Count > 0);

        var catalogs = connection.GetSchema("Catalogs");
        Assert.Contains("tpch", catalogs.Rows.Cast<DataRow>().Select(e => (String)e["CATALOG_NAME"]));

        var schemas = connection.GetSchema("Schemas", ["tpch"]);
        Assert.Contains("tiny", schemas.Rows.Cast<DataRow>().Select(e => (String)e["SCHEMA_NAME"]));

        var tables = connection.GetSchema("Tables", ["tpch", "tiny", null, null]);
        Assert.Contains("nation", tables.Rows.Cast<DataRow>().Select(e => (String)e["TABLE_NAME"]));

        var columns = connection.GetSchema("Columns", ["tpch", "tiny", "nation", null]);
        var names = columns.Rows.Cast<DataRow>().Select(e => (String)e["COLUMN_NAME"]).ToArray();
        Assert.Contains("nationkey", names);
        Assert.Contains("name", names);
    }

    private static void Diagnostics(PrestoConnection connection)
    {
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "select 1";
            Assert.Equal(1, command.ExecuteScalar());

            var queryId = command.LastQueryId!;
            var info = connection.GetQueryInfo(queryId);
            Assert.Equal(queryId, info.Id);
            Assert.False(info.State.IsNullOrEmpty());
        }

        var node = connection.GetNodes();

        Assert.NotNull(node);

        // 部分发行版限制查询列表接口，返回空集合也算可用
        Assert.NotNull(connection.GetQueries());
    }

    private static void ComplexTypes(PrestoConnection connection)
    {
        using var structured = new PrestoConnection(connection.ConnectionString + ";ComplexTypes=Structured");
        structured.Open();

        using (var command = structured.CreateCommand())
        {
            command.CommandText = "select array[1, 2, 3]";
            var value = command.ExecuteScalar();
            Assert.Equal(new Object?[] { 1, 2, 3 }, Assert.IsType<Object[]>(value));
        }

        using (var command = structured.CreateCommand())
        {
            command.CommandText = "select map(array['a'], array[1])";
            var value = command.ExecuteScalar();
            var map = Assert.IsAssignableFrom<IDictionary<String, Object?>>(value);
            Assert.Equal(1, map["a"]);
        }

        using (var command = structured.CreateCommand())
        {
            command.CommandText = "select cast(row('alice', 30) as row(name varchar, age integer))";
            var value = command.ExecuteScalar();
            var row = Assert.IsType<Object[]>(value);
            Assert.Equal("alice", row[0]);
            Assert.Equal(30, row[1]);
        }
    }

    private static void SpoolingFallback(PrestoConnection connection)
    {
        // 本地 Trino 未配置对象存储时，服务端不返回 Spooling 编码，驱动应自动回退直接协议
        if (new PrestoConnectionStringBuilder(connection.ConnectionString).Protocol != PrestoProtocol.Trino) return;

        using var spooling = new PrestoConnection(connection.ConnectionString + ";Spooling=true");
        spooling.Open();

        using var command = spooling.CreateCommand();
        command.CommandText = "select count(*) from tpch.tiny.orders";

        Assert.Equal(15000L, command.ExecuteScalar());
    }

    private static void ErrorMapping(PrestoConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "select * from tpch.tiny.no_such_table";

        var ex = Assert.Throws<PrestoException>(() => command.ExecuteReader());
        Assert.False(ex.Message.IsNullOrEmpty());
        Assert.False(ex.QueryId.IsNullOrEmpty());
        Assert.False(ex.PrestoSqlState.IsNullOrEmpty() && ex.ErrorName.IsNullOrEmpty());
    }
}
