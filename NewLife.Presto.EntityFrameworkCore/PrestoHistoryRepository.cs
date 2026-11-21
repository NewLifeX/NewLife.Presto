using Microsoft.EntityFrameworkCore.Migrations;

namespace NewLife.Presto.EntityFrameworkCore;

/// <summary>Presto 迁移历史仓储。只读提供程序不支持迁移</summary>
public class PrestoHistoryRepository : HistoryRepository
{
    /// <summary>实例化</summary>
    /// <param name="dependencies">迁移历史仓储依赖</param>
    public PrestoHistoryRepository(HistoryRepositoryDependencies dependencies) : base(dependencies) { }

#if NET10_0_OR_GREATER
    /// <summary>锁释放行为。Presto 不支持迁移，保持默认</summary>
    public override LockReleaseBehavior LockReleaseBehavior => LockReleaseBehavior.Transaction;
#endif

    /// <summary>检查历史表是否存在的 SQL。不支持迁移</summary>
    protected override String ExistsSql => throw NotSupported();

    /// <summary>解析历史表是否存在。不支持迁移</summary>
    /// <param name="result">查询结果</param>
    /// <returns>不会返回</returns>
    protected override Boolean InterpretExistsResult(Object? result) => throw NotSupported();

    /// <summary>获取创建历史表的 SQL。不支持迁移</summary>
    /// <returns>不会返回</returns>
    public override String GetCreateIfNotExistsScript() => throw NotSupported();

    /// <summary>获取迁移开始脚本。不支持迁移</summary>
    /// <param name="migrationId">迁移编号</param>
    /// <returns>不会返回</returns>
    public override String GetBeginIfNotExistsScript(String migrationId) => throw NotSupported();

    /// <summary>获取迁移开始脚本。不支持迁移</summary>
    /// <param name="migrationId">迁移编号</param>
    /// <returns>不会返回</returns>
    public override String GetBeginIfExistsScript(String migrationId) => throw NotSupported();

    /// <summary>获取迁移结束脚本。不支持迁移</summary>
    /// <returns>不会返回</returns>
    public override String GetEndIfScript() => throw NotSupported();

#if NET10_0_OR_GREATER
    /// <summary>获取数据库锁（同步）。不支持迁移</summary>
    /// <returns>不会返回</returns>
    public override IMigrationsDatabaseLock AcquireDatabaseLock() => throw NotSupported();

    /// <summary>获取数据库锁（异步）。不支持迁移</summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>不会返回</returns>
    public override Task<IMigrationsDatabaseLock> AcquireDatabaseLockAsync(CancellationToken cancellationToken = default) => throw NotSupported();
#endif

    private static NotSupportedException NotSupported()
        => new(Localization.Translate("The provider does not support database creation, deletion or migrations."));
}
