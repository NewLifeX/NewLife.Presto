using Microsoft.EntityFrameworkCore;
using NewLife.Presto.EntityFrameworkCore;

namespace UnitTest.EntityFrameworkCore;

/// <summary>测试用国家实体</summary>
public class Nation
{
    /// <summary>国家编号</summary>
    public Int64 NationKey { get; set; }

    /// <summary>国家名称</summary>
    public String Name { get; set; } = "";

    /// <summary>区域编号</summary>
    public Int64 RegionKey { get; set; }

    /// <summary>备注</summary>
    public String Comment { get; set; } = "";
}

/// <summary>测试用 DbContext。映射到 tpch.tiny.nation</summary>
public class NationContext : DbContext
{
    /// <summary>连接字符串</summary>
    public static String ConnectionString { get; set; } = "Host=127.0.0.1;Port=8080;Protocol=Trino;Catalog=tpch;Schema=tiny;User=test";

    /// <summary>实例化</summary>
    public NationContext() : base(CreateOptions()) { }

    /// <summary>使用指定选项实例化</summary>
    /// <param name="options">上下文选项</param>
    public NationContext(DbContextOptions<NationContext> options) : base(options) { }

    /// <summary>国家集合</summary>
    public DbSet<Nation> Nations => Set<Nation>();

    /// <summary>配置模型</summary>
    /// <param name="modelBuilder">模型构建器</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var nation = modelBuilder.Entity<Nation>();
        nation.ToTable("nation", "tiny");
        nation.HasKey(e => e.NationKey);
        nation.Property(e => e.NationKey).HasColumnName("nationkey").ValueGeneratedNever();
        nation.Property(e => e.Name).HasColumnName("name").HasMaxLength(25);
        nation.Property(e => e.RegionKey).HasColumnName("regionkey").ValueGeneratedNever();
        nation.Property(e => e.Comment).HasColumnName("comment").HasMaxLength(152);
    }

    /// <summary>创建选项</summary>
    /// <param name="connectionString">连接字符串</param>
    /// <returns>上下文选项</returns>
    public static DbContextOptions<NationContext> CreateOptions(String? connectionString = null)
        => new DbContextOptionsBuilder<NationContext>().UsePresto(connectionString ?? ConnectionString).Options;
}
