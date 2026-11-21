using Microsoft.EntityFrameworkCore;
using NewLife;

namespace UnitTest.EntityFrameworkCore;

/// <summary>真实 Trino/Presto 集成测试。连接串通过 TRINO_TEST_CONNSTR / PRESTO_TEST_CONNSTR 提供</summary>
public class PrestoIntegrationTests
{
    private static String? GetConnectionString()
        => Environment.GetEnvironmentVariable("TRINO_TEST_CONNSTR") ?? Environment.GetEnvironmentVariable("PRESTO_TEST_CONNSTR");

    private static NationContext? CreateContext()
    {
        var connection = GetConnectionString();
        if (connection.IsNullOrWhiteSpace()) return null;

        return new NationContext(NationContext.CreateOptions(connection));
    }

    [Fact]
    [DisplayName("真实服务查询、分页与聚合结果与客户端计算一致")]
    public void QueryAgainstServer()
    {
        using var context = CreateContext();
        if (context == null) return;

        var names = context.Nations.Select(e => e.Name).ToList();
        Assert.NotEmpty(names);

        var page = context.Nations.OrderBy(e => e.Name).Take(3).Select(e => e.Name).ToList();
        Assert.Equal(names.OrderBy(e => e, StringComparer.Ordinal).Take(3).ToList(), page);

        Assert.Equal(names.Count, context.Nations.Count());

        var skipped = context.Nations.OrderBy(e => e.Name).Skip(2).Select(e => e.Name).ToList();
        Assert.Equal(names.OrderBy(e => e, StringComparer.Ordinal).Skip(2).ToList(), skipped);

        var groups = context.Nations.GroupBy(e => e.RegionKey).Select(g => new { g.Key, Count = g.Count() }).ToList();
        Assert.Equal(names.Count, groups.Sum(g => g.Count));
    }

    [Fact]
    [DisplayName("字符串函数在真实服务上生成正确结果")]
    public void StringFunctionsAgainstServer()
    {
        using var context = CreateContext();
        if (context == null) return;

        var names = context.Nations.Select(e => e.Name).ToList();

        Assert.Equal(names.Count(n => n.Contains("A")), context.Nations.Count(e => e.Name.Contains("A")));
        Assert.Equal(names.Count(n => n.StartsWith("A")), context.Nations.Count(e => e.Name.StartsWith("A")));
        Assert.Equal(names.Count(n => n.EndsWith("A")), context.Nations.Count(e => e.Name.EndsWith("A")));
        Assert.Equal(names.Count(n => n.Length > 5), context.Nations.Count(e => e.Name.Length > 5));
    }

    [Fact]
    [DisplayName("写入在真实服务上被拒绝")]
    public void SaveChangesRejected()
    {
        using var context = CreateContext();
        if (context == null) return;

        context.Nations.Add(new Nation { NationKey = -1, Name = "x", RegionKey = 1 });
        Assert.Throws<NotSupportedException>(() => context.SaveChanges());
    }
}
