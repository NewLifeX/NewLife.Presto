using Microsoft.EntityFrameworkCore.Update;

namespace NewLife.Presto.EntityFrameworkCore;

/// <summary>Presto 修改命令批量工厂。Presto/Trino 在本驱动中只读，写入直接拒绝</summary>
public class PrestoModificationCommandBatchFactory : IModificationCommandBatchFactory
{
    /// <summary>实例化</summary>
    /// <param name="dependencies">工厂依赖</param>
    public PrestoModificationCommandBatchFactory(ModificationCommandBatchFactoryDependencies dependencies) { }

    /// <summary>创建修改命令批。始终抛出 NotSupportedException</summary>
    /// <returns>不会返回</returns>
    public ModificationCommandBatch Create()
        => throw new NotSupportedException(Localization.Translate("The provider does not support SaveChanges. Presto/Trino is read-only in this driver."));
}
