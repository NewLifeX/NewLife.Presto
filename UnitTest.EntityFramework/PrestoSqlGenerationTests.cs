using System.Data.Entity;

namespace UnitTest.EntityFramework;

/// <summary>SQL 生成测试。使用 EF6 的 ToString 获取命令文本，不连接真实服务</summary>
public class PrestoSqlGenerationTests
{
    private static NationContext CreateContext()
    {
        NationContext.ConnectionString = "Host=127.0.0.1;Port=1;Protocol=Trino;Catalog=tpch;Schema=tiny;User=test";
        return new NationContext();
    }

    [Fact]
    [DisplayName("投影与过滤生成双引号标识符与 schema 前缀")]
    public void GenerateSelect()
    {
        using var context = CreateContext();

        var sql = context.Nations.Where(e => e.RegionKey == 1).Select(e => e.Name).ToString();

        Assert.Contains("\"tiny\".\"nation\"", sql);
        Assert.Contains("\"Name\"", sql);
        Assert.Contains("WHERE", sql);
    }

    [Fact]
    [DisplayName("排序与 Take 生成 ORDER BY 与 LIMIT")]
    public void GenerateOrderByTake()
    {
        using var context = CreateContext();

        var sql = context.Nations.OrderBy(e => e.Name).Take(3).Select(e => e.Name).ToString();

        Assert.Contains("ORDER BY", sql);
        Assert.Contains("LIMIT 3", sql);
    }

    [Fact]
    [DisplayName("Skip 生成 OFFSET 且位于 LIMIT 之前")]
    public void GenerateOffset()
    {
        using var context = CreateContext();

        var sql = context.Nations.OrderBy(e => e.NationKey).Skip(2).Take(2).Select(e => e.Name).ToString();
        var offsetIndex = sql.IndexOf("OFFSET", StringComparison.Ordinal);
        var limitIndex = sql.IndexOf("LIMIT", StringComparison.Ordinal);

        Assert.True(offsetIndex >= 0);
        Assert.True(limitIndex > offsetIndex);
    }

    [Fact]
    [DisplayName("字符串函数映射为 Presto 函数")]
    public void GenerateStringFunctions()
    {
        using var context = CreateContext();

        Assert.Contains("strpos(", context.Nations.Where(e => e.Name.Contains("A")).Select(e => e.Name).ToString());
        Assert.Contains("strpos(", context.Nations.Where(e => e.Name.StartsWith("A")).Select(e => e.Name).ToString());
        Assert.Contains("substr(", context.Nations.Where(e => e.Name.Substring(1, 2) == "RA").Select(e => e.Name).ToString());
        Assert.Contains("upper(", context.Nations.Where(e => e.Name.ToUpper() == "A").Select(e => e.Name).ToString());
        Assert.Contains("length(", context.Nations.Where(e => e.Name.Length > 3).Select(e => e.Name).ToString());
    }

    [Fact]
    [DisplayName("分组聚合生成 GROUP BY 与 count")]
    public void GenerateGroupBy()
    {
        using var context = CreateContext();

        var sql = context.Nations.GroupBy(e => e.RegionKey).Select(g => new { g.Key, Count = g.Count() }).ToString();

        Assert.Contains("GROUP BY", sql);
        Assert.Contains("count(", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [DisplayName("导航属性生成连接查询")]
    public void GenerateJoin()
    {
        using var context = CreateContext();

        var sql = context.Nations.Where(e => e.Region!.Name == "ASIA").Select(e => e.Name).ToString();

        Assert.Contains("JOIN", sql);
        Assert.Contains("\"region\"", sql);
    }
}
