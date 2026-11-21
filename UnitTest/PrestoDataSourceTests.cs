using System;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using NewLife.Presto;

namespace UnitTest;

/// <summary>数据源测试（.NET 8+）</summary>
public class PrestoDataSourceTests
{
    private const String ConnStr = "Host=localhost;Port=8080;Catalog=hive;Schema=default;User=test;Password=secret;ConnectionTimeout=5;MaxRetries=0;RetryInterval=0";

    [Fact(DisplayName = "数据源创建连接并保留连接串")]
    public void CreateConnection()
    {
        using var source = new PrestoDataSource(ConnStr);

        using var connection = source.CreateConnection();

        Assert.IsType<PrestoConnection>(connection);
        Assert.Equal(ConnStr, connection.ConnectionString, ignoreCase: true);
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    [Fact(DisplayName = "数据源打开连接并查询")]
    public void OpenConnection()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson("""{ "id": "q1", "columns": [{"name":"n","type":"integer"}], "data": [["1"]] }""");
        using var source = new PrestoDataSource(ConnStr, () => new PrestoConnection(ConnStr, handler));

        using var connection = (PrestoConnection)source.OpenConnection();
        using var command = connection.CreateCommand("select 1");
        using var reader = command.ExecuteReader();

        Assert.True(reader.Read());
        Assert.Equal(1, reader.GetInt32(0));
    }

    [Fact(DisplayName = "数据源异步打开连接")]
    public async Task OpenConnectionAsync()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson("""{ "id": "q1", "columns": [{"name":"n","type":"integer"}], "data": [["2"]] }""");
        using var source = new PrestoDataSource(ConnStr, () => new PrestoConnection(ConnStr, handler));

        using var connection = (PrestoConnection)await source.OpenConnectionAsync();
        using var command = connection.CreateCommand("select 2");
        var value = await command.ExecuteScalarAsync();

        Assert.Equal(2, value);
    }

    [Fact(DisplayName = "工厂创建数据源")]
    public void FactoryCreatesDataSource()
    {
        using var source = PrestoClientFactory.Instance.CreateDataSource(ConnStr);

        Assert.IsType<PrestoDataSource>(source);
        Assert.Equal(ConnStr, source.ConnectionString);
    }
}
