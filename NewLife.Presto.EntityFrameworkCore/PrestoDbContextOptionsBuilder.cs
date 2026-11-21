using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace NewLife.Presto.EntityFrameworkCore;

/// <summary>Presto DbContext 选项构建器。用于配置 Presto/Trino 特定选项</summary>
public class PrestoDbContextOptionsBuilder : RelationalDbContextOptionsBuilder<PrestoDbContextOptionsBuilder, PrestoOptionsExtension>
{
    /// <summary>实例化</summary>
    /// <param name="optionsBuilder">选项构建器</param>
    public PrestoDbContextOptionsBuilder(DbContextOptionsBuilder optionsBuilder) : base(optionsBuilder) { }
}
