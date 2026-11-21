using Microsoft.EntityFrameworkCore.Migrations;

namespace NewLife.Presto.EntityFrameworkCore;

/// <summary>Presto 迁移 SQL 生成器。只读提供程序不支持迁移，基类仅用于满足 EF Core 服务解析</summary>
public class PrestoMigrationsSqlGenerator : MigrationsSqlGenerator
{
    /// <summary>实例化</summary>
    /// <param name="dependencies">迁移 SQL 生成器依赖</param>
    public PrestoMigrationsSqlGenerator(MigrationsSqlGeneratorDependencies dependencies) : base(dependencies) { }
}
