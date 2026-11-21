using System;
using System.Data;
using NewLife.Presto;

namespace UnitTest;

/// <summary>数据适配器测试</summary>
public class PrestoDataAdapterTests
{
    [Fact(DisplayName = "Fill 填充 DataTable")]
    public void FillTable()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson("""{ "id": "q1", "columns": [{"name":"n","type":"integer"},{"name":"s","type":"varchar"}], "data": [["1","a"],["2","b"]] }""");
        var connection = TestHelper.CreateOpenConnection(handler);
        using var adapter = new PrestoDataAdapter("select n, s from t", connection);

        var table = new DataTable();
        var count = adapter.Fill(table);

        Assert.Equal(2, count);
        Assert.Equal(2, table.Rows.Count);
        Assert.Equal(typeof(Int32), table.Columns["n"]!.DataType);
        Assert.Equal(1, table.Rows[0]["n"]);
        Assert.Equal("a", table.Rows[0]["s"]);
        Assert.Equal("b", table.Rows[1]["s"]);
    }

    [Fact(DisplayName = "Fill 填充 DataSet")]
    public void FillDataSet()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson("""{ "id": "q1", "columns": [{"name":"n","type":"bigint"}], "data": [["7"]] }""");
        var connection = TestHelper.CreateOpenConnection(handler);
        using var adapter = new PrestoDataAdapter("select 7 as n", connection);

        var dataset = new DataSet();
        var count = adapter.Fill(dataset);

        Assert.Equal(1, count);
        Assert.Single(dataset.Tables);
        Assert.Equal(7L, dataset.Tables[0].Rows[0][0]);
    }

    [Fact(DisplayName = "命令构造与 SelectCommand 往返")]
    public void SelectCommandRoundTrip()
    {
        using var connection = new PrestoConnection("Host=localhost;Port=8080;User=test");
        using var command = new PrestoCommand("select 1", connection);
        using var adapter = new PrestoDataAdapter(command);

        Assert.Same(command, adapter.SelectCommand);

        using var another = new PrestoCommand("select 2", connection);
        adapter.SelectCommand = another;

        Assert.Same(another, adapter.SelectCommand);
    }

    [Fact(DisplayName = "没有查询命令时 Fill 抛异常")]
    public void FillWithoutSelectCommand()
    {
        using var adapter = new PrestoDataAdapter();

        Assert.Throws<InvalidOperationException>(() => adapter.Fill(new DataTable()));
    }

    [Fact(DisplayName = "工厂创建数据适配器")]
    public void FactoryCreatesAdapter()
    {
        Assert.True(PrestoClientFactory.Instance.CanCreateDataAdapter);
        Assert.IsType<PrestoDataAdapter>(PrestoClientFactory.Instance.CreateDataAdapter());
    }
}
