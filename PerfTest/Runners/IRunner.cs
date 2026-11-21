namespace PerfTest;

/// <summary>数据库客户端基准测试适配器，统一各竞品的调用方式</summary>
public interface IRunner : IDisposable
{
    /// <summary>显示名称</summary>
    String Name { get; }

    /// <summary>客户端类别：ADO / SDK / 原始 HTTP</summary>
    String Category { get; }

    /// <summary>目标端点描述</summary>
    String Target { get; }

    /// <summary>读取到的数据校验和，用于确认各客户端读到相同结果</summary>
    Int64 Checksum { get; }

    /// <summary>建立并保持连接</summary>
    void Open();

    /// <summary>关闭并释放连接</summary>
    void Close();

    /// <summary>执行标量查询，返回首行首列</summary>
    /// <param name="sql">查询语句</param>
    Int64 ExecuteScalar(String sql);

    /// <summary>读取全部结果行，返回行数</summary>
    /// <param name="sql">查询语句</param>
    Int32 ReadAll(String sql);

    /// <summary>读取全部结果行并累计多列校验值，返回行数</summary>
    /// <param name="sql">查询语句</param>
    Int64 ReadColumns(String sql);
}
