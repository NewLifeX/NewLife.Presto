using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace NewLife.Presto.EntityFrameworkCore;

/// <summary>DbContextOptionsBuilder 扩展。提供 UsePresto 配置入口</summary>
public static class PrestoDbContextOptionsExtensions
{
    /// <summary>配置 DbContext 使用 NewLife.Presto 数据库提供程序</summary>
    /// <param name="optionsBuilder">选项构建器</param>
    /// <param name="connectionString">Presto/Trino 连接字符串</param>
    /// <param name="prestoOptionsAction">Presto 选项配置回调</param>
    /// <returns>选项构建器</returns>
    public static DbContextOptionsBuilder UsePresto(
        this DbContextOptionsBuilder optionsBuilder,
        String connectionString,
        Action<PrestoDbContextOptionsBuilder>? prestoOptionsAction = null)
    {
        if (optionsBuilder == null) throw new ArgumentNullException(nameof(optionsBuilder));
        if (connectionString.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(connectionString));

        var extension = (PrestoOptionsExtension)GetOrCreateExtension(optionsBuilder).WithConnectionString(connectionString);
        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(extension);

        prestoOptionsAction?.Invoke(new PrestoDbContextOptionsBuilder(optionsBuilder));

        return optionsBuilder;
    }

    /// <summary>配置 DbContext 使用 NewLife.Presto 数据库提供程序</summary>
    /// <typeparam name="TContext">DbContext 类型</typeparam>
    /// <param name="optionsBuilder">选项构建器</param>
    /// <param name="connectionString">Presto/Trino 连接字符串</param>
    /// <param name="prestoOptionsAction">Presto 选项配置回调</param>
    /// <returns>选项构建器</returns>
    public static DbContextOptionsBuilder<TContext> UsePresto<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder,
        String connectionString,
        Action<PrestoDbContextOptionsBuilder>? prestoOptionsAction = null)
        where TContext : DbContext
    {
        ((DbContextOptionsBuilder)optionsBuilder).UsePresto(connectionString, prestoOptionsAction);
        return optionsBuilder;
    }

    /// <summary>配置 DbContext 使用已有的 DbConnection 连接到 Presto/Trino</summary>
    /// <param name="optionsBuilder">选项构建器</param>
    /// <param name="connection">已有的数据库连接</param>
    /// <param name="prestoOptionsAction">Presto 选项配置回调</param>
    /// <returns>选项构建器</returns>
    public static DbContextOptionsBuilder UsePresto(
        this DbContextOptionsBuilder optionsBuilder,
        DbConnection connection,
        Action<PrestoDbContextOptionsBuilder>? prestoOptionsAction = null)
    {
        if (optionsBuilder == null) throw new ArgumentNullException(nameof(optionsBuilder));
        if (connection == null) throw new ArgumentNullException(nameof(connection));

        var extension = (PrestoOptionsExtension)GetOrCreateExtension(optionsBuilder).WithConnection(connection);
        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(extension);

        prestoOptionsAction?.Invoke(new PrestoDbContextOptionsBuilder(optionsBuilder));

        return optionsBuilder;
    }

    /// <summary>配置 DbContext 使用已有的 DbConnection 连接到 Presto/Trino</summary>
    /// <typeparam name="TContext">DbContext 类型</typeparam>
    /// <param name="optionsBuilder">选项构建器</param>
    /// <param name="connection">已有的数据库连接</param>
    /// <param name="prestoOptionsAction">Presto 选项配置回调</param>
    /// <returns>选项构建器</returns>
    public static DbContextOptionsBuilder<TContext> UsePresto<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder,
        DbConnection connection,
        Action<PrestoDbContextOptionsBuilder>? prestoOptionsAction = null)
        where TContext : DbContext
    {
        ((DbContextOptionsBuilder)optionsBuilder).UsePresto(connection, prestoOptionsAction);
        return optionsBuilder;
    }

    private static PrestoOptionsExtension GetOrCreateExtension(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.Options.FindExtension<PrestoOptionsExtension>() ?? new PrestoOptionsExtension();
}
