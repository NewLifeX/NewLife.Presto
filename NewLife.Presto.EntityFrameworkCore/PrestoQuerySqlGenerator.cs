using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace NewLife.Presto.EntityFrameworkCore;

/// <summary>Presto 查询 SQL 生成器。生成 LIMIT/OFFSET 分页子句</summary>
public class PrestoQuerySqlGenerator : QuerySqlGenerator
{
    /// <summary>实例化</summary>
    /// <param name="dependencies">查询 SQL 生成器依赖</param>
    public PrestoQuerySqlGenerator(QuerySqlGeneratorDependencies dependencies) : base(dependencies) { }

    /// <summary>生成 Presto 的 LIMIT/OFFSET 子句</summary>
    /// <remarks>Trino 与 PrestoDB 要求 OFFSET 位于 LIMIT 之前，且 OFFSET 需与 LIMIT 同时出现</remarks>
    /// <param name="selectExpression">查询表达式</param>
    protected override void GenerateLimitOffset(SelectExpression selectExpression)
    {
        if (selectExpression == null) throw new ArgumentNullException(nameof(selectExpression));

        if (selectExpression.Offset != null)
        {
            Sql.AppendLine().Append("OFFSET ");
            Visit(selectExpression.Offset);
        }

        if (selectExpression.Limit != null)
        {
            Sql.AppendLine().Append("LIMIT ");
            Visit(selectExpression.Limit);
        }
    }
}
