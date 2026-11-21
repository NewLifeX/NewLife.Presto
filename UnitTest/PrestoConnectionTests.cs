using System;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NewLife.Presto;

namespace UnitTest;

/// <summary>连接与元数据测试</summary>
public class PrestoConnectionTests
{
    [Fact(DisplayName = "打开与关闭状态机")]
    public void OpenCloseState()
    {
        var handler = new FakePrestoHandler();
        var connection = TestHelper.CreateConnection(handler);
        var states = new System.Collections.Generic.List<ConnectionState>();
        connection.StateChange += (sender, args) => states.Add(args.CurrentState);

        Assert.Equal(ConnectionState.Closed, connection.State);

        connection.Open();
        Assert.Equal(ConnectionState.Open, connection.State);
        Assert.Throws<InvalidOperationException>(() => connection.Open());
        Assert.Throws<InvalidOperationException>(() => connection.ConnectionString = "Host=other");

        connection.Close();
        Assert.Equal(ConnectionState.Closed, connection.State);
        Assert.Contains(ConnectionState.Open, states);
        Assert.Contains(ConnectionState.Closed, states);

        connection.ConnectionString = "Host=other";
        Assert.Equal("other", connection.Settings.Host);
    }

    [Fact(DisplayName = "连接基本属性")]
    public void BasicProperties()
    {
        var connection = TestHelper.CreateConnection(new FakePrestoHandler());

        Assert.Equal("localhost", connection.DataSource);
        Assert.Equal("hive", connection.Database);
        Assert.Equal("hive", connection.Catalog);
        Assert.Equal("default", connection.Schema);
        Assert.Equal(String.Empty, connection.ServerVersion);
        Assert.Equal(5, connection.ConnectionTimeout);
        Assert.NotNull(connection.Log);
    }

    [Fact(DisplayName = "切换 Catalog 与 Schema")]
    public void ChangeDatabase()
    {
        var connection = TestHelper.CreateOpenConnection(new FakePrestoHandler());

        connection.ChangeDatabase("cat2.sch2");
        Assert.Equal("cat2", connection.Database);
        Assert.Equal("sch2", connection.Schema);
        Assert.Equal("cat2", connection.Settings.Catalog);
        Assert.Equal("sch2", connection.Settings.Schema);

        connection.ChangeDatabase("cat3");
        Assert.Equal("cat3", connection.Database);
        Assert.Equal("sch2", connection.Schema);

        Assert.Throws<ArgumentException>(() => connection.ChangeDatabase(" "));

        connection.Close();
        Assert.Throws<InvalidOperationException>(() => connection.ChangeDatabase("cat"));
    }

    [Fact(DisplayName = "关闭连接取消活跃查询")]
    public void CloseCancelsActiveCommands()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson("""{ "id":"q1","columns":[{"name":"n","type":"integer"}],"data":[],"nextUri":"http://localhost:8080/v1/statement/q1/1" }""");
        handler.EnqueueJson("""{ "id":"q1" }""");
        var connection = TestHelper.CreateOpenConnection(handler);
        using var command = connection.CreateCommand("select 1");
        using var reader = command.ExecuteReader();

        connection.Close();

        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    [Fact(DisplayName = "OAuth2 连接打开失败时清理会话与令牌")]
    public void OAuthFailureDuringOpen()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueText("bad", System.Net.HttpStatusCode.InternalServerError);
        var connection = TestHelper.CreateConnection(handler, ";Auth=OAuth2;TokenEndpoint=https://idp.local/token;ClientId=cid;TestConnection=true");

        Assert.Throws<PrestoException>(() => connection.Open());
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    [Fact(DisplayName = "无 Catalog 的连接属性")]
    public void WithoutCatalog()
    {
        using var connection = new PrestoConnection("Host=127.0.0.1;Port=1;User=test");
        connection.Open();

        Assert.Equal(String.Empty, connection.Catalog);
        Assert.Equal(String.Empty, connection.Schema);
        Assert.Equal(String.Empty, connection.Database);
    }

    [Fact(DisplayName = "TestConnection 预取服务端信息")]
    public void TestConnectionOnOpen()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson("""{ "nodeVersion": { "version": "468" }, "environment": "prod" }""");
        var connection = TestHelper.CreateConnection(handler, ";TestConnection=true");

        connection.Open();

        Assert.Equal("468", connection.ServerVersion);
        Assert.Equal("prod", connection.ServerInfo!.Environment);
        connection.Close();
    }

    [Fact(DisplayName = "TestConnection 失败回到关闭状态")]
    public void TestConnectionFailure()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueText("down", System.Net.HttpStatusCode.InternalServerError);
        var connection = TestHelper.CreateConnection(handler, ";TestConnection=true");

        Assert.Throws<PrestoException>(() => connection.Open());
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    [Fact(DisplayName = "ServerVersion 按需请求并缓存")]
    public void ServerVersionLazy()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson("""{ "nodeVersion": "468" }""");
        var connection = TestHelper.CreateOpenConnection(handler);

        Assert.Equal("468", connection.ServerVersion);
        Assert.Equal("468", connection.ServerVersion);
        Assert.Single(handler.Requests);
    }

    [Fact(DisplayName = "异步获取服务端信息")]
    public async Task GetServerInfoAsync()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson("""{ "nodeVersion": "468" }""");
        var connection = TestHelper.CreateOpenConnection(handler);

        var info = await connection.GetServerInfoAsync(CancellationToken.None);

        Assert.Equal("468", info.NodeVersion);
        Assert.Same(info, connection.ServerInfo);
    }

    [Fact(DisplayName = "不支持事务与自增")]
    public void UnsupportedFeatures()
    {
        var connection = TestHelper.CreateOpenConnection(new FakePrestoHandler());

        Assert.Throws<NotSupportedException>(() => connection.BeginTransaction());
        Assert.IsType<PrestoCommand>(connection.CreateCommand());
        Assert.Null(connection.CreateCommand().Transaction);
    }

    [Fact(DisplayName = "MetaDataCollections 集合")]
    public void GetMetaDataCollections()
    {
        var connection = TestHelper.CreateOpenConnection(new FakePrestoHandler());

        var table = connection.GetSchema();

        Assert.Equal(6, table.Rows.Count);
        var names = table.Rows.Cast<DataRow>().Select(e => (String)e["CollectionName"]).ToArray();
        Assert.Contains("Tables", names);
        Assert.Contains("Columns", names);
        Assert.Contains("Catalogs", names);
        Assert.Contains("Schemas", names);
    }

    [Fact(DisplayName = "DataTypes 集合")]
    public void GetDataTypes()
    {
        var connection = TestHelper.CreateOpenConnection(new FakePrestoHandler());

        var table = connection.GetSchema("DataTypes");

        Assert.True(table.Rows.Count > 10);
        Assert.Contains("boolean", table.Rows.Cast<DataRow>().Select(e => (String)e["TypeName"]));
        Assert.Equal(typeof(String).FullName, table.Rows.Cast<DataRow>().First(e => (String)e["TypeName"] == "varchar")["DataType"]);
    }

    [Fact(DisplayName = "Tables 元数据查询与缓存")]
    public void GetTables()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson("""
        { "columns": [
            {"name":"table_catalog","type":"varchar"},
            {"name":"table_schema","type":"varchar"},
            {"name":"table_name","type":"varchar"},
            {"name":"table_type","type":"varchar"}],
          "data": [["hive","default","t1","BASE TABLE"]] }
        """);
        var connection = TestHelper.CreateOpenConnection(handler);

        var table = connection.GetSchema("Tables", ["hive", "default", "t1", "BASE TABLE"]);

        Assert.Single(table.Rows);
        Assert.Equal("t1", table.Rows[0]["TABLE_NAME"]);
        Assert.Equal("BASE TABLE", table.Rows[0]["TABLE_TYPE"]);
        Assert.Contains("FROM \"hive\".information_schema.tables", handler.LastBody);
        Assert.Contains("table_schema = 'default'", handler.LastBody);
        Assert.Contains("table_name = 't1'", handler.LastBody);
        Assert.Contains("table_type = 'BASE TABLE'", handler.LastBody);

        var again = connection.GetSchema("Tables", ["hive", "default", "t1", "BASE TABLE"]);
        Assert.Single(again.Rows);
        Assert.Single(handler.Requests);
    }

    [Fact(DisplayName = "Columns 元数据查询")]
    public void GetColumns()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson("""
        { "columns": [
            {"name":"table_catalog","type":"varchar"},
            {"name":"table_schema","type":"varchar"},
            {"name":"table_name","type":"varchar"},
            {"name":"column_name","type":"varchar"},
            {"name":"ordinal_position","type":"bigint"},
            {"name":"column_default","type":"varchar"},
            {"name":"is_nullable","type":"varchar"},
            {"name":"data_type","type":"varchar"}],
          "data": [["hive","default","t1","id","1",null,"YES","bigint"]] }
        """);
        var connection = TestHelper.CreateOpenConnection(handler);

        var table = connection.GetSchema("Columns", ["hive", "default", "t1", "id"]);

        Assert.Single(table.Rows);
        Assert.Equal("id", table.Rows[0]["COLUMN_NAME"]);
        Assert.Equal(1, table.Rows[0]["ORDINAL_POSITION"]);
        Assert.Equal(DBNull.Value, table.Rows[0]["COLUMN_DEFAULT"]);
        Assert.Equal("YES", table.Rows[0]["IS_NULLABLE"]);
        Assert.Equal("bigint", table.Rows[0]["DATA_TYPE"]);
        Assert.Contains("FROM \"hive\".information_schema.columns", handler.LastBody);
        Assert.Contains("table_schema = 'default'", handler.LastBody);
        Assert.Contains("table_name = 't1'", handler.LastBody);
        Assert.Contains("column_name = 'id'", handler.LastBody);
    }

    [Fact(DisplayName = "Catalogs 与 Schemas 集合")]
    public void GetCatalogsAndSchemas()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson("""{ "columns":[{"name":"Catalog","type":"varchar"}], "data": [["hive"],["system"]] }""");
        handler.EnqueueJson("""{ "columns":[{"name":"Schema","type":"varchar"}], "data": [["default"],["other"]] }""");
        var connection = TestHelper.CreateOpenConnection(handler);

        var catalogs = connection.GetSchema("Catalogs");
        Assert.Equal(2, catalogs.Rows.Count);
        Assert.Equal("hive", catalogs.Rows[0]["CATALOG_NAME"]);

        var schemas = connection.GetSchema("Schemas", ["hive", "default"]);
        Assert.Single(schemas.Rows);
        Assert.Equal("default", schemas.Rows[0]["SCHEMA_NAME"]);
        Assert.Contains("SHOW CATALOGS", handler.Bodies[0]);
        Assert.Contains("SHOW SCHEMAS FROM \"hive\"", handler.Bodies[1]);
    }

    [Fact(DisplayName = "Schema 缓存过期后重新查询")]
    public void SchemaCacheExpires()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson("""{ "columns":[{"name":"Catalog","type":"varchar"}], "data": [["hive"]] }""");
        handler.EnqueueJson("""{ "columns":[{"name":"Catalog","type":"varchar"}], "data": [["hive"]] }""");
        var connection = TestHelper.CreateOpenConnection(handler);
        connection.SchemaCacheExpire = TimeSpan.FromSeconds(-1);

        connection.GetSchema("Catalogs");
        connection.GetSchema("Catalogs");

        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact(DisplayName = "GetSchema 参数校验")]
    public void GetSchemaValidation()
    {
        var connection = TestHelper.CreateOpenConnection(new FakePrestoHandler());

        Assert.Throws<ArgumentException>(() => connection.GetSchema("Unknown"));
        Assert.Throws<ArgumentException>(() => connection.GetSchema(" "));

        connection.Close();
        Assert.Throws<InvalidOperationException>(() => connection.GetSchema("MetaDataCollections"));
    }

    [Fact(DisplayName = "未配置 Catalog 时 Tables 元数据报错")]
    public void TablesRequiresCatalog()
    {
        var handler = new FakePrestoHandler();
        var connection = new PrestoConnection("Host=localhost;Port=8080;User=test", handler);
        connection.Open();

        var ex = Assert.Throws<PrestoException>(() => connection.GetSchema("Tables"));

        Assert.Equal(NewLife.Localization.Translate("Catalog is required for the '{0}' schema collection.", "Tables"), ex.Message);
    }

    [Fact(DisplayName = "关闭连接后创建语句客户端报错")]
    public void CreateStatementClientRequiresOpen()
    {
        var connection = TestHelper.CreateConnection(new FakePrestoHandler());

        Assert.Throws<InvalidOperationException>(() => connection.CreateStatementClient());
    }

    [Fact(DisplayName = "重复关闭与共享客户端关闭")]
    public void CloseIdempotentAndCachedClient()
    {
        var cached = new PrestoConnection("Host=localhost;Port=19999;User=test");
        cached.Open();
        cached.Close();
        cached.Close();
        Assert.Equal(ConnectionState.Closed, cached.State);

        var connection = TestHelper.CreateOpenConnection(new FakePrestoHandler());
        connection.Close();
        connection.Close();
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    [Fact(DisplayName = "自定义处理器不能为空")]
    public void HandlerRequired()
    {
        Assert.Throws<ArgumentNullException>(() => new PrestoConnection("Host=localhost", null!));
    }

    [Fact(DisplayName = "服务端版本为空时返回空字符串")]
    public void EmptyServerVersion()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson("""{ "nodeVersion": {} }""");
        var connection = TestHelper.CreateOpenConnection(handler);

        Assert.Equal(String.Empty, connection.ServerVersion);
    }

    [Fact(DisplayName = "无 Schema 连接切换 Catalog")]
    public void ChangeDatabaseWithoutSchema()
    {
        var handler = new FakePrestoHandler();
        var connection = new PrestoConnection("Host=localhost;Port=8080;Catalog=hive;User=test", handler);
        connection.Open();

        connection.ChangeDatabase("other");

        Assert.Equal("other", connection.Database);
        Assert.Equal(String.Empty, connection.Schema);
    }

    [Fact(DisplayName = "空过滤值不参与元数据查询")]
    public void ShortRestrictions()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson("""{ "columns": [{"name":"column_name","type":"varchar"}], "data": [] }""");
        var connection = TestHelper.CreateOpenConnection(handler);

        var table = connection.GetSchema("Columns", ["hive"]);

        Assert.Empty(table.Rows);
        Assert.DoesNotContain("WHERE", handler.LastBody);
    }
}
