using System.Text;
using Microsoft.EntityFrameworkCore.Update;

namespace NewLife.Presto.EntityFrameworkCore;

/// <summary>Presto 更新 SQL 生成器。只读提供程序不会执行 DML，仅用于满足 EF Core 服务解析</summary>
public class PrestoUpdateSqlGenerator : UpdateSqlGenerator
{
    /// <summary>实例化</summary>
    /// <param name="dependencies">更新 SQL 生成器依赖</param>
    public PrestoUpdateSqlGenerator(UpdateSqlGeneratorDependencies dependencies) : base(dependencies) { }

#if NET6_0
    /// <summary>追加自增标识条件。不支持写入</summary>
    /// <param name="commandStringBuilder">SQL 构建器</param>
    /// <param name="columnModification">列修改</param>
    protected override void AppendIdentityWhereCondition(StringBuilder commandStringBuilder, IColumnModification columnModification)
        => throw new NotSupportedException(Localization.Translate("The provider does not support SaveChanges. Presto/Trino is read-only in this driver."));

    /// <summary>追加影响行数条件。不支持写入</summary>
    /// <param name="commandStringBuilder">SQL 构建器</param>
    /// <param name="expectedRowsAffected">期望影响行数</param>
    protected override void AppendRowsAffectedWhereCondition(StringBuilder commandStringBuilder, Int32 expectedRowsAffected)
        => throw new NotSupportedException(Localization.Translate("The provider does not support SaveChanges. Presto/Trino is read-only in this driver."));
#endif
}
