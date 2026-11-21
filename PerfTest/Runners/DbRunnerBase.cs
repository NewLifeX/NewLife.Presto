using System.Data.Common;

namespace PerfTest.Runners;

/// <summary>ADO.NET 客户端通用适配器，子类只负责创建连接</summary>
public abstract class DbRunnerBase : IRunner
{
    private DbConnection? _connection;

    /// <summary>显示名称</summary>
    public abstract String Name { get; }

    /// <summary>客户端类别</summary>
    public virtual String Category => "ADO";

    /// <summary>目标端点描述</summary>
    public virtual String Target => _connection?.ConnectionString ?? String.Empty;

    /// <summary>读取到的数据校验和</summary>
    public Int64 Checksum { get; private set; }

    /// <summary>创建尚未打开的连接</summary>
    protected abstract DbConnection CreateConnection();

    /// <summary>建立并保持连接</summary>
    public void Open()
    {
        var conn = CreateConnection();
        conn.Open();
        _connection = conn;
    }

    /// <summary>关闭并释放连接</summary>
    public void Close()
    {
        _connection?.Close();
        _connection?.Dispose();
        _connection = null;
    }

    /// <summary>执行标量查询，返回首行首列</summary>
    /// <param name="sql">查询语句</param>
    public Int64 ExecuteScalar(String sql)
    {
        var conn = _connection ?? throw new InvalidOperationException("Connection is not open");
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var value = cmd.ExecuteScalar();
        return value == null || value == DBNull.Value ? 0 : Convert.ToInt64(value);
    }

    /// <summary>读取全部结果行，返回行数</summary>
    /// <param name="sql">查询语句</param>
    public Int32 ReadAll(String sql)
    {
        var conn = _connection ?? throw new InvalidOperationException("Connection is not open");
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        using var reader = cmd.ExecuteReader();

        var count = 0;
        var sum = 0L;
        while (reader.Read())
        {
            var value = reader.GetValue(0);
            if (value != null && value != DBNull.Value) sum += Convert.ToInt64(value);
            count++;
        }

        Checksum = sum;
        return count;
    }

    /// <summary>释放资源</summary>
    public void Dispose() => Close();
}
