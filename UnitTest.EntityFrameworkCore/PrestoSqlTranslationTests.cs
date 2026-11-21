using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace UnitTest.EntityFrameworkCore;

/// <summary>SQL 生成与只读行为测试。不连接真实服务</summary>
public class PrestoSqlTranslationTests
{
    private static NationContext CreateContext() => new(NationContext.CreateOptions("Host=127.0.0.1;Port=1;Protocol=Trino;Catalog=tpch;Schema=tiny;User=test"));

    [Fact]
    [DisplayName("提供程序名称与关系型能力已注册")]
    public void ProviderRegistered()
    {
        using var context = CreateContext();

        Assert.Equal("NewLife.Presto.EntityFrameworkCore", context.Database.ProviderName);
        Assert.NotNull(context.GetService<IRelationalTypeMappingSource>());
    }

    [Fact]
    [DisplayName("投影与过滤生成双引号标识符")]
    public void GenerateSelect()
    {
        using var context = CreateContext();

        var sql = context.Nations.Where(e => e.RegionKey == 1).Select(e => e.Name).ToQueryString();

        Assert.Contains("FROM \"tiny\".\"nation\"", sql);
        Assert.Contains("\"name\"", sql);
        Assert.Contains("WHERE", sql);
    }

    [Fact]
    [DisplayName("Take 生成 LIMIT，Skip 生成 OFFSET 且在 LIMIT 之前")]
    public void GenerateLimitOffset()
    {
        using var context = CreateContext();

        var take = context.Nations.OrderBy(e => e.NationKey).Take(3).ToQueryString();
        Assert.Contains("LIMIT", take);
        Assert.DoesNotContain("OFFSET", take);

        var page = context.Nations.OrderBy(e => e.NationKey).Skip(2).Take(3).ToQueryString();
        var offsetIndex = page.IndexOf("OFFSET", StringComparison.Ordinal);
        var limitIndex = page.IndexOf("LIMIT", StringComparison.Ordinal);
        Assert.True(offsetIndex >= 0);
        Assert.True(limitIndex > offsetIndex);
    }

    [Fact]
    [DisplayName("仅 Skip 不生成巨型 LIMIT 兜底")]
    public void GenerateOffsetWithoutLimit()
    {
        using var context = CreateContext();

        var sql = context.Nations.OrderBy(e => e.NationKey).Skip(5).Select(e => e.Name).ToQueryString();

        Assert.Contains("OFFSET", sql);
        Assert.DoesNotContain("2147483647", sql);
    }

    [Fact]
    [DisplayName("分组聚合生成 GROUP BY 与 count")]
    public void GenerateGroupBy()
    {
        using var context = CreateContext();

        var sql = context.Nations.GroupBy(e => e.RegionKey).Select(g => new { g.Key, Count = g.Count() }).ToQueryString();

        Assert.Contains("GROUP BY", sql);
        Assert.Contains("count(*)", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [DisplayName("字符串方法翻译为 strpos/substr/upper 等 Presto 函数")]
    public void TranslateStringMethods()
    {
        using var context = CreateContext();

        Assert.Contains("strpos(\"n\".\"name\", 'A') > 0", context.Nations.Where(e => e.Name.Contains("A")).ToQueryString());
        Assert.Contains("strpos(\"n\".\"name\", 'A') = 1", context.Nations.Where(e => e.Name.StartsWith("A")).ToQueryString());
        Assert.Contains("substr(", context.Nations.Where(e => e.Name.EndsWith("A")).ToQueryString());
        Assert.Contains("upper(", context.Nations.Where(e => e.Name.ToUpper() == "A").ToQueryString());
        Assert.Contains("length(", context.Nations.Where(e => e.Name.Length > 3).ToQueryString());
        Assert.Contains("substr(", context.Nations.Where(e => e.Name.Substring(1, 2) == "RA").ToQueryString());
    }

    [Fact]
    [DisplayName("数学函数翻译为 Presto 函数")]
    public void TranslateMathFunctions()
    {
        using var context = CreateContext();

        Assert.Contains("abs(", context.Nations.Where(e => Math.Abs(e.RegionKey) > 1).ToQueryString());
        Assert.Contains("power(", context.Nations.Where(e => Math.Pow(e.RegionKey, 2) > 1).ToQueryString());
    }

    [Fact]
    [DisplayName("Like 生成 LIKE 表达式")]
    public void TranslateLike()
    {
        using var context = CreateContext();

        var sql = context.Nations.Where(e => EF.Functions.Like(e.Name, "A%")).ToQueryString();

        Assert.Contains("LIKE 'A%'", sql);
    }

    [Fact]
    [DisplayName("常用 CLR 类型映射到 Presto 存储类型")]
    public void MapStoreTypes()
    {
        using var context = CreateContext();
        var source = context.GetService<IRelationalTypeMappingSource>();

        Assert.Equal("boolean", source.FindMapping(typeof(Boolean))!.StoreType);
        Assert.Equal("smallint", source.FindMapping(typeof(Int16))!.StoreType);
        Assert.Equal("integer", source.FindMapping(typeof(Int32))!.StoreType);
        Assert.Equal("bigint", source.FindMapping(typeof(Int64))!.StoreType);
        Assert.Equal("real", source.FindMapping(typeof(Single))!.StoreType);
        Assert.Equal("double", source.FindMapping(typeof(Double))!.StoreType);
        Assert.StartsWith("decimal", source.FindMapping(typeof(Decimal))!.StoreType);
        Assert.Equal("varchar", source.FindMapping(typeof(String))!.StoreType);
        Assert.Equal("varbinary", source.FindMapping(typeof(Byte[]))!.StoreType);
        Assert.StartsWith("timestamp", source.FindMapping(typeof(DateTime))!.StoreType);
        Assert.StartsWith("time", source.FindMapping(typeof(TimeSpan))!.StoreType);
    }

    [Fact]
    [DisplayName("生成建表脚本抛出不支持异常")]
    public void GenerateCreateScriptThrows()
    {
        using var context = CreateContext();

        Assert.Throws<NotSupportedException>(() => context.Database.GenerateCreateScript());
    }

    [Fact]
    [DisplayName("建表操作抛出不支持异常")]
    public void CreateTablesThrows()
    {
        using var context = CreateContext();
        var creator = context.GetService<IRelationalDatabaseCreator>();

        Assert.Throws<NotSupportedException>(() => creator.CreateTables());
        Assert.Throws<NotSupportedException>(() => creator.Create());
        Assert.Throws<NotSupportedException>(() => creator.Delete());
    }

    [Fact]
    [DisplayName("SaveChanges 抛出不支持异常")]
    public void SaveChangesThrows()
    {
        using var context = CreateContext();
        context.Nations.Add(new Nation { NationKey = 999, Name = "x", RegionKey = 1 });

        Assert.Throws<NotSupportedException>(() => context.SaveChanges());
    }
}
