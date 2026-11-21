using System.Data.Entity;
using System.Data.Entity.Core;
using System.Data.Entity.Infrastructure;

namespace UnitTest.EntityFramework;

/// <summary>只读行为与真实服务集成测试</summary>
public class PrestoReadOnlyTests
{
    private static String? GetConnectionString()
        => Environment.GetEnvironmentVariable("TRINO_TEST_CONNSTR") ?? Environment.GetEnvironmentVariable("PRESTO_TEST_CONNSTR");

    private static NationContext CreateContext(String connectionString)
    {
        NationContext.ConnectionString = connectionString;
        return new NationContext();
    }

    [Fact]
    [DisplayName("建库抛出不支持异常")]
    public void CreateDatabaseRejected()
    {
        using var context = CreateContext("Host=127.0.0.1;Port=1;ConnectionTimeout=1;MaxRetries=0;RetryInterval=0;Protocol=Trino;Catalog=tpch;Schema=tiny;User=test");

        Assert.False(context.Database.Exists());
        Assert.Throws<NotSupportedException>(() => context.Database.Create());
    }

    [Fact]
    [DisplayName("SaveChanges 因不支持事务被拒绝")]
    public void SaveChangesRejected()
    {
        using var context = CreateContext("Host=127.0.0.1;Port=1;ConnectionTimeout=1;MaxRetries=0;RetryInterval=0;Protocol=Trino;Catalog=tpch;Schema=tiny;User=test");
        context.Nations.Add(new Nation { NationKey = -1, Name = "x", RegionKey = 1 });

        var ex = Assert.ThrowsAny<Exception>(() => context.SaveChanges());
        var inner = ex;
        while (inner != null && inner is not NotSupportedException) inner = inner.InnerException;
        Assert.IsType<NotSupportedException>(inner);
    }

    [Fact]
    [DisplayName("真实服务的查询、分页与聚合结果与客户端计算一致")]
    public void QueryAgainstServer()
    {
        var connection = GetConnectionString();
        if (String.IsNullOrEmpty(connection)) return;

        using var context = CreateContext(connection);

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
        var connection = GetConnectionString();
        if (String.IsNullOrEmpty(connection)) return;

        using var context = CreateContext(connection);
        var names = context.Nations.Select(e => e.Name).ToList();

        Assert.Equal(names.Count(n => n.Contains("A")), context.Nations.Count(e => e.Name.Contains("A")));
        Assert.Equal(names.Count(n => n.StartsWith("A")), context.Nations.Count(e => e.Name.StartsWith("A")));
        Assert.Equal(names.Count(n => n.EndsWith("A")), context.Nations.Count(e => e.Name.EndsWith("A")));
    }

    [Fact]
    [DisplayName("导航属性在真实服务上生成连接查询")]
    public void JoinAgainstServer()
    {
        var connection = GetConnectionString();
        if (String.IsNullOrEmpty(connection)) return;

        using var context = CreateContext(connection);

        var expected = context.Regions.Where(e => e.Name == "ASIA").Select(e => e.RegionKey).ToList();
        var actual = context.Nations.Where(e => e.Region!.Name == "ASIA").Select(e => e.RegionKey).Distinct().ToList();

        Assert.Equal(expected.OrderBy(e => e).ToList(), actual.OrderBy(e => e).ToList());
    }
}
