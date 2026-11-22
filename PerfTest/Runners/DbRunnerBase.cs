using System.Data.Common;
using System.Globalization;

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

    /// <summary>读取全部结果行并累计多列校验值，返回行数</summary>
    /// <param name="sql">查询语句</param>
    public Int64 ReadColumns(String sql)
    {
        var conn = _connection ?? throw new InvalidOperationException("Connection is not open");
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        using var reader = cmd.ExecuteReader();

        var count = 0;
        var sum = 0L;
        while (reader.Read())
        {
            sum += ToInt64(reader.GetValue(0));
            sum += ToText(reader.GetValue(1))?.Length ?? 0;
            sum += ToCents(reader.GetValue(2));
            sum += ToDayOfYear(reader.GetValue(3));
            count++;
        }

        Checksum = sum;
        return count;
    }

    /// <summary>读取全部结果行并累计宽表校验值（列数由查询决定），返回行数</summary>
    /// <param name="sql">查询语句</param>
    public Int64 ReadWide(String sql)
    {
        var conn = _connection ?? throw new InvalidOperationException("Connection is not open");
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        using var reader = cmd.ExecuteReader();

        var count = 0;
        var sum = 0L;
        var fields = reader.FieldCount;
        while (reader.Read())
        {
            for (var i = 0; i < fields; i++) sum += WideValue(reader.GetValue(i));
            count++;
        }

        Checksum = sum;
        return count;
    }

    /// <summary>宽表单元格折算成整数：整数取值、字符串取长度</summary>
    internal static Int64 WideValue(Object? value) => value switch
    {
        null or DBNull => 0,
        Int64 v => v,
        Int32 v => v,
        String v => v.Length,
        _ => Convert.ToInt64(value, CultureInfo.InvariantCulture),
    };

    private static Int64 ToInt64(Object? value) => value switch
    {
        null or DBNull => 0,
        Int64 v => v,
        Int32 v => v,
        _ => Convert.ToInt64(value),
    };

    private static String? ToText(Object? value) => value switch
    {
        null or DBNull => null,
        String v => v,
        _ => Convert.ToString(value, CultureInfo.InvariantCulture),
    };

    private static Int64 ToCents(Object? value)
    {
        var number = value switch
        {
            null or DBNull => 0m,
            Decimal v => v,
            String v => Decimal.Parse(v, CultureInfo.InvariantCulture),
            Double v => (Decimal)v,
            Single v => (Decimal)v,
            IConvertible value2 => value2.ToDecimal(CultureInfo.InvariantCulture),
            _ => Decimal.Parse(value.ToString() ?? "0", CultureInfo.InvariantCulture),
        };
        return (Int64)(number * 100);
    }

    private static Int32 ToDayOfYear(Object? value) => value switch
    {
        null or DBNull => 0,
        DateTime v => v.DayOfYear,
        DateTimeOffset v => v.DayOfYear,
        String v => DateTime.Parse(v, CultureInfo.InvariantCulture).DayOfYear,
        IConvertible value2 => value2.ToDateTime(CultureInfo.InvariantCulture).DayOfYear,
        _ => DateTime.Parse(value.ToString() ?? String.Empty, CultureInfo.InvariantCulture).DayOfYear,
    };

    /// <summary>释放资源</summary>
    public void Dispose() => Close();
}
