using System.Data.Common;

namespace NewLife.Presto;

/// <summary>Presto/Trino 客户端工厂</summary>
/// <remarks>
/// XCode 通过类型名 `NewLife.Presto.PrestoClientFactory` 与程序集 `NewLife.Presto.dll` 反射加载；
/// `netstandard2.1`/`netcoreapp` 同时注册到 <c>DbProviderFactories</c>。
/// </remarks>
public sealed class PrestoClientFactory : DbProviderFactory
{
    /// <summary>默认实例</summary>
    public static readonly PrestoClientFactory Instance = new();

    static PrestoClientFactory()
    {
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP
        DbProviderFactories.RegisterFactory("NewLife.Presto", Instance);
#endif
    }

    #region 属性
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP
    /// <summary>是否支持创建数据适配器</summary>
    public override Boolean CanCreateDataAdapter => true;

    /// <summary>是否支持创建命令构建器。不支持</summary>
    public override Boolean CanCreateCommandBuilder => false;
#endif

    /// <summary>是否支持创建数据源枚举器。不支持</summary>
    public override Boolean CanCreateDataSourceEnumerator => false;
    #endregion

    #region 方法
    /// <summary>创建连接</summary>
    /// <returns>连接</returns>
    public override DbConnection CreateConnection() => new PrestoConnection();

    /// <summary>创建命令</summary>
    /// <returns>命令</returns>
    public override DbCommand CreateCommand() => new PrestoCommand();

    /// <summary>创建参数</summary>
    /// <returns>参数</returns>
    public override DbParameter CreateParameter() => new PrestoParameter();

    /// <summary>创建连接字符串构建器</summary>
    /// <returns>连接字符串构建器</returns>
    public override DbConnectionStringBuilder CreateConnectionStringBuilder() => new PrestoConnectionStringBuilder();

#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP
    /// <summary>创建数据适配器</summary>
    /// <returns>数据适配器</returns>
    public override DbDataAdapter CreateDataAdapter() => new PrestoDataAdapter();
#endif

#if NET8_0_OR_GREATER
    /// <summary>创建数据源</summary>
    /// <param name="connectionString">连接字符串</param>
    /// <returns>数据源</returns>
    public override DbDataSource CreateDataSource(String connectionString) => new PrestoDataSource(connectionString);
#endif

#if NET6_0_OR_GREATER
    /// <summary>创建批量命令</summary>
    /// <returns>批量命令</returns>
    public override DbBatch CreateBatch() => new PrestoBatch();
#endif
    #endregion
}
