using Microsoft.EntityFrameworkCore.Query;

namespace NewLife.Presto.EntityFrameworkCore;

/// <summary>Presto 查询 SQL 生成器工厂</summary>
public class PrestoQuerySqlGeneratorFactory : IQuerySqlGeneratorFactory
{
    private readonly QuerySqlGeneratorDependencies _dependencies;

    /// <summary>实例化</summary>
    /// <param name="dependencies">查询 SQL 生成器依赖</param>
    public PrestoQuerySqlGeneratorFactory(QuerySqlGeneratorDependencies dependencies) => _dependencies = dependencies;

    /// <summary>创建查询 SQL 生成器</summary>
    /// <returns>查询 SQL 生成器</returns>
    public QuerySqlGenerator Create() => new PrestoQuerySqlGenerator(_dependencies);
}
