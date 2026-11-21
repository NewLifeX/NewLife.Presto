using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using NewLife.Presto;
using NewLife.Presto.EntityFramework;

namespace UnitTest.EntityFramework;

/// <summary>测试用区域实体</summary>
public class Region
{
    /// <summary>区域编号</summary>
    public Int64 RegionKey { get; set; }

    /// <summary>区域名称</summary>
    public String Name { get; set; } = "";

    /// <summary>备注</summary>
    public String Comment { get; set; } = "";

    /// <summary>国家集合</summary>
    public List<Nation> Nations { get; set; } = [];
}

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

    /// <summary>所属区域</summary>
    public Region? Region { get; set; }
}

/// <summary>测试用 DbContext。映射到 tpch.tiny 下的 nation/region</summary>
public class NationContext : DbContext
{
    /// <summary>默认连接字符串</summary>
    public const String DefaultConnectionString = "Host=127.0.0.1;Port=8080;Protocol=Trino;Catalog=tpch;Schema=tiny;User=test";

    /// <summary>实例化</summary>
    public NationContext() : base(ConnectionString) { }

    /// <summary>当前连接字符串</summary>
    public static String ConnectionString { get; set; } = DefaultConnectionString;

    /// <summary>国家集合</summary>
    public DbSet<Nation> Nations { get; set; } = null!;

    /// <summary>区域集合</summary>
    public DbSet<Region> Regions { get; set; } = null!;

    /// <summary>配置模型</summary>
    /// <param name="modelBuilder">模型构建器</param>
    protected override void OnModelCreating(DbModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Region>().ToTable("region", "tiny");
        modelBuilder.Entity<Region>().HasKey(e => e.RegionKey);
        modelBuilder.Entity<Region>().Property(e => e.RegionKey);
        modelBuilder.Entity<Region>().Property(e => e.Name);

        modelBuilder.Entity<Nation>().ToTable("nation", "tiny");
        modelBuilder.Entity<Nation>().HasKey(e => e.NationKey);
        modelBuilder.Entity<Nation>().Property(e => e.NationKey);
        modelBuilder.Entity<Nation>().Property(e => e.Name);
        modelBuilder.Entity<Nation>().Property(e => e.RegionKey);
        modelBuilder.Entity<Nation>().Property(e => e.Comment);
        modelBuilder.Entity<Nation>().HasRequired(e => e.Region).WithMany(e => e.Nations).HasForeignKey(e => e.RegionKey);

        base.OnModelCreating(modelBuilder);
    }
}

/// <summary>测试用 EF6 配置。注册 Presto 提供程序</summary>
public class TestConfiguration : DbConfiguration
{
    /// <summary>实例化并注册提供程序</summary>
    public TestConfiguration()
    {
        SetProviderFactory(PrestoEFConfiguration.ProviderInvariantName, PrestoClientFactory.Instance);
        SetProviderServices(PrestoEFConfiguration.ProviderInvariantName, PrestoProviderServices.Instance);
        SetDefaultConnectionFactory(new PrestoConnectionFactory());
    }
}
