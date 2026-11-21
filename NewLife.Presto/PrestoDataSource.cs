using System.Data.Common;

#if NET8_0_OR_GREATER
namespace NewLife.Presto;

/// <summary>Presto/Trino 数据源</summary>
/// <remarks>封装连接字符串，按需创建并打开连接，便于依赖注入与连接池管理。</remarks>
public sealed class PrestoDataSource : DbDataSource
{
    private readonly String _connectionString;
    private readonly Func<DbConnection>? _factory;

    #region 构造
    /// <summary>实例化数据源</summary>
    /// <param name="connectionString">连接字符串</param>
    public PrestoDataSource(String connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    /// <summary>实例化数据源，注入连接工厂。用于测试</summary>
    /// <param name="connectionString">连接字符串</param>
    /// <param name="factory">连接工厂</param>
    internal PrestoDataSource(String connectionString, Func<DbConnection> factory) : this(connectionString) => _factory = factory;
    #endregion

    #region 属性
    /// <summary>连接字符串</summary>
    public override String ConnectionString => _connectionString;
    #endregion

    #region 方法
    /// <summary>创建连接</summary>
    /// <returns>连接</returns>
    protected override DbConnection CreateDbConnection() => _factory != null ? _factory() : new PrestoConnection(_connectionString);
    #endregion
}
#endif
