using System.Data.Entity;

namespace NewLife.Presto.EntityFramework;

/// <summary>Presto EF6 配置。在应用启动时注册提供程序工厂与服务</summary>
/// <remarks>
/// 本提供程序只支持查询。写入、事务与迁移都不支持；Presto/Trino 的 DDL 由连接器与外部元数据管理。
/// </remarks>
public class PrestoEFConfiguration : DbConfiguration
{
    /// <summary>提供程序不变名称</summary>
    public const String ProviderInvariantName = "NewLife.Presto.PrestoClient";

    /// <summary>实例化并注册 Presto 提供程序</summary>
    public PrestoEFConfiguration()
    {
        SetProviderFactory(ProviderInvariantName, PrestoClientFactory.Instance);
        SetProviderServices(ProviderInvariantName, PrestoProviderServices.Instance);
        SetDefaultConnectionFactory(new PrestoConnectionFactory());
    }
}
