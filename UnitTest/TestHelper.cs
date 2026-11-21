using NewLife;
using NewLife.Presto;

namespace UnitTest;

/// <summary>测试辅助</summary>
internal static class TestHelper
{
    /// <summary>创建使用假 HTTP 处理器的连接</summary>
    public static PrestoConnection CreateConnection(FakePrestoHandler handler, String extra = "")
        => new("Host=localhost;Port=8080;Catalog=hive;Schema=default;User=test;Password=secret;ConnectionTimeout=5;MaxRetries=0;RetryInterval=0" + extra, handler);

    /// <summary>创建并打开连接</summary>
    public static PrestoConnection CreateOpenConnection(FakePrestoHandler handler, String extra = "")
    {
        var connection = CreateConnection(handler, extra);
        connection.Open();
        return connection;
    }

    /// <summary>单列结果页</summary>
    public static String SingleColumn(String columnName, String type, String? nextUri = null, String? data = "[[\"1\"]]", String? id = null)
    {
        var builder = "{";
        if (!id.IsNullOrEmpty()) builder += $"\"id\":\"{id}\",";
        builder += $"\"columns\":[{{\"name\":\"{columnName}\",\"type\":\"{type}\"}}],";
        builder += $"\"data\":{data ?? "[]"}";
        if (!nextUri.IsNullOrEmpty()) builder += $",\"nextUri\":\"{nextUri}\"";
        builder += "}";
        return builder;
    }
}
