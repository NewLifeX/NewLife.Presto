using System.Data.Entity.Core.Common.CommandTrees;
using System.Data.Entity.Core.Metadata.Edm;
using System.Globalization;
using System.Linq;
using System.Text;

namespace NewLife.Presto.EntityFramework;

/// <summary>Presto EF6 SQL 生成器。只生成 SELECT 查询；写入与迁移一律拒绝</summary>
/// <remarks>
/// 支持投影、过滤、排序、分页、去重、连接、APPLY（LATERAL）、分组聚合与联合。
/// 迁移、写入、存储过程与实体继承不支持；列名要求与 CLR 属性名一致（不使用 HasColumnName 重映射）。
/// </remarks>
internal sealed class PrestoSqlGenerator
{
    #region 属性
    private readonly List<String> _parameters = new();

    /// <summary>分组输出列映射。键为“变量名.成员名”，值为可直接引用的 SQL 列</summary>
    private readonly Dictionary<String, String> _groupColumns = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>分组输入变量别名映射。分组变量引用输入行的列</summary>
    private readonly Dictionary<String, String> _groupVariables = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>绑定变量到 FROM 别名的映射。合并透明查询后旧变量名仍能解析到同一别名</summary>
    private readonly Dictionary<String, String> _aliases = new(StringComparer.OrdinalIgnoreCase);

    private String AliasOf(String variableName) => _aliases.TryGetValue(variableName, out var alias) ? alias : Quote(variableName);
    #endregion

    #region 入口
    /// <summary>将命令树转换为 SQL</summary>
    /// <param name="commandTree">EF6 命令树</param>
    /// <returns>SQL 与参数名</returns>
    public PrestoSqlResult GenerateSql(DbCommandTree commandTree)
    {
        if (commandTree == null) throw new ArgumentNullException(nameof(commandTree));

        return commandTree switch
        {
            DbQueryCommandTree query => new PrestoSqlResult(BuildQuery(query.Query).ToSql(), _parameters),
            DbInsertCommandTree or DbUpdateCommandTree or DbDeleteCommandTree => throw WriteNotSupported(),
            DbFunctionCommandTree => throw new NotSupportedException(Localization.Translate("Stored procedures and table-valued functions are not supported by the Presto provider.")),
            _ => throw new NotSupportedException(Localization.Translate("Unsupported command tree type: {0}.", commandTree.GetType().Name)),
        };
    }

    private static NotSupportedException WriteNotSupported()
        => new(Localization.Translate("The provider does not support SaveChanges. Presto/Trino is read-only in this driver."));
    #endregion

    #region 查询构建
    /// <summary>构建查询片段</summary>
    private Query BuildQuery(DbExpression expression)
    {
        switch (expression)
        {
            case DbScanExpression scan:
                return new Query { Select = "*", Source = GetTableName(scan) };

            case DbProjectExpression project:
            {
                var query = BuildSource(project.Input);
                query.Select = FormatProjection(project.Projection);
                return query;
            }

            case DbFilterExpression filter:
            {
                var query = BuildSource(filter.Input);
                var predicate = Visit(filter.Predicate);
                query.Where = String.IsNullOrEmpty(query.Where) ? predicate : query.Where + " AND " + predicate;
                return query;
            }

            case DbSortExpression sort:
            {
                var query = BuildSource(sort.Input);
                query.OrderBy = FormatSortOrder(sort.SortOrder);
                return query;
            }

            case DbLimitExpression limit:
            {
                var query = BuildQuery(limit.Argument);
                query.Limit = Visit(limit.Limit);
                query.Barrier = true;
                return query;
            }

            case DbSkipExpression skip:
            {
                var query = BuildSource(skip.Input);
                query.OrderBy = FormatSortOrder(skip.SortOrder);
                query.Offset = Visit(skip.Count);
                query.Barrier = true;
                return query;
            }

            case DbDistinctExpression distinct:
            {
                var query = BuildQuery(distinct.Argument);
                query.Distinct = true;
                query.Barrier = true;
                return query;
            }

            case DbGroupByExpression groupBy:
                return BuildGroupBy(groupBy, null);

            case DbUnionAllExpression union:
            {
                var left = BuildQuery(union.Left);
                var right = BuildQuery(union.Right);
                return new Query { Select = "*", Source = "(" + left.ToSql() + " UNION ALL " + right.ToSql() + ")", Barrier = true };
            }

            case DbJoinExpression join:
                return BuildJoin(join);

            case DbCrossJoinExpression crossJoin:
                return BuildCrossJoin(crossJoin);

            case DbApplyExpression apply:
                return BuildApply(apply);

            default:
                return new Query { Select = Visit(expression), Barrier = true };
        }
    }

    /// <summary>构建绑定源。透明查询（扫描/连接/过滤链）直接合并，其余包装为子查询并登记新别名</summary>
    private Query BuildSource(DbExpressionBinding binding) => BuildSource(binding.Expression, binding.VariableName);

    private Query BuildSource(DbExpression expression, String variableName)
    {
        var alias = Quote(variableName);

        // 扫描直接作为 FROM 项，别名即绑定变量名
        if (expression is DbScanExpression scan)
        {
            _aliases[variableName] = alias;
            return new Query { Select = "*", Source = GetTableName(scan) + " AS " + alias };
        }

        // 分组需要先构建聚合，再包装为子查询
        if (expression is DbGroupByExpression groupBy)
        {
            _aliases[variableName] = alias;
            return Wrap(BuildGroupBy(groupBy, variableName), alias);
        }

        var query = BuildQuery(expression);
        if (query.Select == "*" && !query.Barrier)
        {
            _aliases[variableName] = GetSourceAlias(expression);
            return query;
        }

        _aliases[variableName] = alias;
        return Wrap(query, alias);
    }

    /// <summary>获取表达式绑定源的别名，用于透明合并后的变量解析</summary>
    private String GetSourceAlias(DbExpression expression) => expression switch
    {
        DbFilterExpression filter => AliasOf(filter.Input.VariableName),
        DbSortExpression sort => AliasOf(sort.Input.VariableName),
        DbProjectExpression project => AliasOf(project.Input.VariableName),
        DbJoinExpression join => AliasOf(join.Left.VariableName),
        DbApplyExpression apply => AliasOf(apply.Input.VariableName),
        DbCrossJoinExpression crossJoin when crossJoin.Inputs.Count > 0 => AliasOf(crossJoin.Inputs[0].VariableName),
        _ => Quote(String.Empty),
    };

    /// <summary>把查询包装为带别名的子查询</summary>
    private static Query Wrap(Query inner, String alias) => new() { Select = "*", Source = "(" + inner.ToSql() + ") AS " + alias };

    private Query BuildJoin(DbJoinExpression join)
    {
        var left = BuildSource(join.Left);
        var right = BuildSource(join.Right);
        var kind = join.ExpressionKind switch
        {
            DbExpressionKind.InnerJoin => "INNER JOIN",
            DbExpressionKind.LeftOuterJoin => "LEFT JOIN",
            DbExpressionKind.FullOuterJoin => "FULL JOIN",
            _ => throw new NotSupportedException(Localization.Translate("Unsupported join type: {0}.", join.ExpressionKind)),
        };

        return new Query
        {
            Select = "*",
            Source = BuildFromItem(left, join.Left.VariableName) + " " + kind + " " + BuildFromItem(right, join.Right.VariableName) + " ON " + Visit(join.JoinCondition),
        };
    }

    private Query BuildCrossJoin(DbCrossJoinExpression crossJoin)
    {
        var sources = new List<String>();
        foreach (var input in crossJoin.Inputs)
        {
            sources.Add(BuildFromItem(BuildSource(input), input.VariableName));
        }

        return new Query { Select = "*", Source = String.Join(" CROSS JOIN ", sources) };
    }

    private Query BuildApply(DbApplyExpression apply)
    {
        var input = BuildSource(apply.Input);
        var inner = BuildQuery(apply.Apply.Expression);
        var alias = Quote(apply.Apply.VariableName);
        var source = BuildFromItem(input, apply.Input.VariableName);

        var sql = apply.ExpressionKind switch
        {
            DbExpressionKind.CrossApply => source + " CROSS JOIN LATERAL (" + inner.ToSql() + ") AS " + alias,
            DbExpressionKind.OuterApply => source + " LEFT JOIN LATERAL (" + inner.ToSql() + ") AS " + alias + " ON TRUE",
            _ => throw new NotSupportedException(Localization.Translate("Unsupported apply type: {0}.", apply.ExpressionKind)),
        };

        return new Query { Select = "*", Source = sql };
    }

    /// <summary>把绑定源转换为 FROM 项。带过滤/排序等子句时需要包装为子查询</summary>
    private static String BuildFromItem(Query query, String variableName)
        => IsSimple(query) ? query.Source : "(" + query.ToSql() + ") AS " + Quote(variableName);

    /// <summary>判断查询是否为可直接合并的简单源</summary>
    private static Boolean IsSimple(Query query) => query.Select == "*" && !query.Barrier
        && String.IsNullOrEmpty(query.Where) && String.IsNullOrEmpty(query.GroupBy)
        && String.IsNullOrEmpty(query.OrderBy) && String.IsNullOrEmpty(query.Limit) && String.IsNullOrEmpty(query.Offset);

    private Query BuildGroupBy(DbGroupByExpression groupBy, String? variableName)
    {
        var inputVariable = groupBy.Input.VariableName;
        var input = BuildSource(groupBy.Input.Expression, inputVariable);
        String source;
        if (IsSimple(input))
        {
            source = input.Source;
            _groupVariables[groupBy.Input.GroupVariableName] = AliasOf(inputVariable);
        }
        else
        {
            var alias = Quote(inputVariable);
            source = "(" + input.ToSql() + ") AS " + alias;
            _aliases[inputVariable] = alias;
            _groupVariables[groupBy.Input.GroupVariableName] = alias;
        }

        var rowType = (groupBy.ResultType.EdmType as CollectionType)?.TypeUsage.EdmType as RowType;
        var keyProperty = rowType?.Properties.FirstOrDefault();
        var keyPropertyName = keyProperty?.Name ?? "Key";
        var keyRowType = keyProperty?.TypeUsage.EdmType as RowType;

        var select = new List<String>();
        var groupByList = new List<String>();
        var keys = groupBy.Keys;
        for (var i = 0; i < keys.Count; i++)
        {
            var sql = Visit(keys[i]);
            groupByList.Add(sql);

            var alias = keyRowType != null && i < keyRowType.Properties.Count
                ? keyPropertyName + "_" + keyRowType.Properties[i].Name
                : keyPropertyName;
            select.Add(sql + " AS " + Quote(alias));

            if (variableName == null || keyRowType == null || i >= keyRowType.Properties.Count) continue;

            var path = variableName + "." + keyPropertyName + "." + keyRowType.Properties[i].Name;
            _groupColumns[path] = Quote(variableName) + "." + Quote(alias);
        }

        var aggregates = groupBy.Aggregates;
        for (var i = 0; i < aggregates.Count; i++)
        {
            var sql = VisitAggregate(aggregates[i]);
            var alias = rowType != null && i + 1 < rowType.Properties.Count ? rowType.Properties[i + 1].Name : "A" + (i + 1).ToString(CultureInfo.InvariantCulture);
            select.Add(sql + " AS " + Quote(alias));
        }

        return new Query
        {
            Select = String.Join(", ", select),
            Source = source,
            GroupBy = groupByList.Count > 0 ? String.Join(", ", groupByList) : "",
        };
    }

    private String FormatSortOrder(IList<DbSortClause> order)
    {
        var parts = new List<String>();
        foreach (var clause in order)
        {
            parts.Add(Visit(clause.Expression) + (clause.Ascending ? " ASC" : " DESC"));
        }

        return String.Join(", ", parts);
    }

    private String FormatProjection(DbExpression projection)
    {
        var parts = new List<String>();
        AppendProjectionColumns(parts, projection, null);
        return String.Join(", ", parts);
    }

    private void AppendProjectionColumns(List<String> parts, DbExpression expression, String? alias)
    {
        switch (expression)
        {
            case DbVariableReferenceExpression variable when GetStructuralMembers(variable.ResultType) is { Count: > 0 } members:
                foreach (var member in members)
                {
                    parts.Add(Quote(variable.VariableName) + "." + Quote(member.Name) + " AS " + Quote(member.Name));
                }
                return;

            case DbNewInstanceExpression instance when GetStructuralMembers(instance.ResultType) is { Count: > 0 } members:
                for (var i = 0; i < instance.Arguments.Count; i++)
                {
                    var name = i < members.Count ? members[i].Name : alias;
                    AppendProjectionColumns(parts, instance.Arguments[i], name);
                }
                return;

            case DbPropertyExpression property:
                parts.Add(Visit(property) + " AS " + Quote(alias ?? property.Property.Name));
                return;

            default:
                var sql = Visit(expression);
                parts.Add(alias == null ? sql : sql + " AS " + Quote(alias));
                return;
        }
    }

    private static IList<EdmProperty>? GetStructuralMembers(TypeUsage typeUsage) => typeUsage.EdmType switch
    {
        EntityType entity => entity.Properties,
        RowType row => row.Properties,
        _ => null,
    };
    #endregion

    #region 表达式访问
    private String Visit(DbExpression expression)
    {
        switch (expression)
        {
            case DbPropertyExpression property:
                return VisitProperty(property);

            case DbVariableReferenceExpression variable:
                return Quote(variable.VariableName);

            case DbConstantExpression constant:
                return FormatLiteral(constant.Value);

            case DbNullExpression:
                return "NULL";

            case DbParameterReferenceExpression parameter:
                if (!_parameters.Contains(parameter.ParameterName)) _parameters.Add(parameter.ParameterName);
                return "@" + parameter.ParameterName;

            case DbComparisonExpression comparison:
            {
                var op = comparison.ExpressionKind switch
                {
                    DbExpressionKind.Equals => "=",
                    DbExpressionKind.NotEquals => "<>",
                    DbExpressionKind.LessThan => "<",
                    DbExpressionKind.LessThanOrEquals => "<=",
                    DbExpressionKind.GreaterThan => ">",
                    DbExpressionKind.GreaterThanOrEquals => ">=",
                    _ => throw new NotSupportedException(Localization.Translate("Unsupported comparison: {0}.", comparison.ExpressionKind)),
                };
                return "(" + Visit(comparison.Left) + " " + op + " " + Visit(comparison.Right) + ")";
            }

            case DbAndExpression and:
                return "(" + Visit(and.Left) + " AND " + Visit(and.Right) + ")";

            case DbOrExpression or:
                return "(" + Visit(or.Left) + " OR " + Visit(or.Right) + ")";

            case DbNotExpression not:
                return "NOT (" + Visit(not.Argument) + ")";

            case DbIsNullExpression isNull:
                return "(" + Visit(isNull.Argument) + " IS NULL)";

            case DbLikeExpression like:
            {
                var sql = Visit(like.Argument) + " LIKE " + Visit(like.Pattern);
                if (like.Escape != null && like.Escape is not DbNullExpression) sql += " ESCAPE " + Visit(like.Escape);
                return "(" + sql + ")";
            }

            case DbArithmeticExpression arithmetic:
                return VisitArithmetic(arithmetic);

            case DbCastExpression cast:
                return "CAST(" + Visit(cast.Argument) + " AS " + GetStoreTypeName(cast.ResultType) + ")";

            case DbCaseExpression @case:
            {
                var sb = new StringBuilder("CASE");
                for (var i = 0; i < @case.When.Count; i++)
                {
                    sb.Append(" WHEN ").Append(Visit(@case.When[i])).Append(" THEN ").Append(Visit(@case.Then[i]));
                }
                if (@case.Else != null && @case.Else is not DbNullExpression) sb.Append(" ELSE ").Append(Visit(@case.Else));
                sb.Append(" END");
                return sb.ToString();
            }

            case DbFunctionExpression function:
                return VisitFunction(function);

            case DbInExpression @in:
            {
                var items = new List<String>();
                foreach (var item in @in.List) items.Add(Visit(item));
                return "(" + Visit(@in.Item) + " IN (" + String.Join(", ", items) + "))";
            }

            case DbElementExpression element:
            {
                var query = BuildQuery(element.Argument);
                if (String.IsNullOrEmpty(query.Limit))
                    query.Limit = "1";
                else
                    return "(SELECT * FROM (" + query.ToSql() + ") AS \"element\" LIMIT 1)";

                return "(" + query.ToSql() + ")";
            }

            case DbIsOfExpression isOf:
                return "(" + Visit(isOf.Argument) + " IS NOT NULL)";

            case DbTreatExpression treat:
                return Visit(treat.Argument);

            case DbRefExpression reference:
                return Visit(reference.Argument);

            case DbEntityRefExpression entityRef:
                return Visit(entityRef.Argument);

            case DbDerefExpression deref:
                return Visit(deref.Argument);

            case DbQuantifierExpression quantifier:
                return VisitQuantifier(quantifier);

            case DbIsEmptyExpression empty:
                return "NOT EXISTS (" + BuildQuery(empty.Argument).ToSql() + ")";

            case DbUnionAllExpression or DbFilterExpression or DbProjectExpression or DbJoinExpression or DbCrossJoinExpression or DbApplyExpression or DbGroupByExpression or DbDistinctExpression or DbSortExpression or DbLimitExpression or DbSkipExpression:
                return "(" + BuildQuery(expression).ToSql() + ")";

            case DbNewInstanceExpression instance:
            {
                var parts = new List<String>();
                foreach (var argument in instance.Arguments) parts.Add(Visit(argument));
                return "ROW(" + String.Join(", ", parts) + ")";
            }

            default:
                throw new NotSupportedException(Localization.Translate("Unsupported expression: {0}.", expression.GetType().Name));
        }
    }

    private String VisitProperty(DbPropertyExpression property)
    {
        if (property.Instance is DbVariableReferenceExpression variable)
        {
            if (_groupColumns.TryGetValue(variable.VariableName + "." + property.Property.Name, out var mapped)) return mapped;
            if (_groupVariables.TryGetValue(variable.VariableName, out var source)) return source + "." + Quote(property.Property.Name);

            return AliasOf(variable.VariableName) + "." + Quote(property.Property.Name);
        }

        if (property.Instance is DbPropertyExpression inner && inner.Instance is DbVariableReferenceExpression innerVariable)
        {
            var path = innerVariable.VariableName + "." + inner.Property.Name + "." + property.Property.Name;
            if (_groupColumns.TryGetValue(path, out var mapped)) return mapped;

            // 连接输出的行把右侧别名嵌套为成员，例如 Join1.Extent2.Name 对应 Extent2.Name
            return Quote(inner.Property.Name) + "." + Quote(property.Property.Name);
        }

        return Visit(property.Instance) + "." + Quote(property.Property.Name);
    }

    private String VisitArithmetic(DbArithmeticExpression arithmetic)
    {
        if (arithmetic.ExpressionKind == DbExpressionKind.UnaryMinus) return "(-" + Visit(arithmetic.Arguments[0]) + ")";

        var left = Visit(arithmetic.Arguments[0]);
        var right = Visit(arithmetic.Arguments[1]);
        return arithmetic.ExpressionKind switch
        {
            DbExpressionKind.Plus => "(" + left + " + " + right + ")",
            DbExpressionKind.Minus => "(" + left + " - " + right + ")",
            DbExpressionKind.Multiply => "(" + left + " * " + right + ")",
            DbExpressionKind.Divide => "(" + left + " / " + right + ")",
            DbExpressionKind.Modulo => "MOD(" + left + ", " + right + ")",
            _ => throw new NotSupportedException(Localization.Translate("Unsupported arithmetic operation: {0}.", arithmetic.ExpressionKind)),
        };
    }

    private String VisitQuantifier(DbQuantifierExpression quantifier)
    {
        var source = BuildSource(quantifier.Input);
        var predicate = Visit(quantifier.Predicate);

        return quantifier.ExpressionKind switch
        {
            DbExpressionKind.Any => "EXISTS (SELECT 1 FROM " + source.Source + " WHERE " + predicate + ")",
            DbExpressionKind.All => "NOT EXISTS (SELECT 1 FROM " + source.Source + " WHERE NOT (" + predicate + "))",
            _ => throw new NotSupportedException(Localization.Translate("Unsupported quantifier: {0}.", quantifier.ExpressionKind)),
        };
    }

    private String VisitAggregate(DbAggregate aggregate)
    {
        if (aggregate is not DbFunctionAggregate function) throw new NotSupportedException(Localization.Translate("Unsupported aggregate: {0}.", aggregate.GetType().Name));

        var name = function.Function.Name.ToUpperInvariant();
        var distinct = function.Distinct ? "DISTINCT " : "";
        if (name == "COUNT" || name == "BIGCOUNT")
        {
            return function.Arguments.Count == 0 ? "count(*)" : "count(" + distinct + Visit(function.Arguments[0]) + ")";
        }

        var target = name switch
        {
            "SUM" => "sum",
            "MIN" => "min",
            "MAX" => "max",
            "AVG" or "AVERAGE" => "avg",
            _ => throw new NotSupportedException(Localization.Translate("Unsupported aggregate function: {0}.", function.Function.Name)),
        };

        var arguments = new List<String>();
        foreach (var argument in function.Arguments) arguments.Add(Visit(argument));

        return target + "(" + distinct + String.Join(", ", arguments) + ")";
    }

    private String VisitFunction(DbFunctionExpression function)
    {
        var name = function.Function.Name.ToUpperInvariant();
        var arguments = function.Arguments;
        String Arg(Int32 index) => Visit(arguments[index]);

        if (function.Function.NamespaceName != "Edm")
        {
            var parts = new List<String>();
            foreach (var argument in arguments) parts.Add(Visit(argument));
            return function.Function.Name.ToLowerInvariant() + "(" + String.Join(", ", parts) + ")";
        }

        switch (name)
        {
            case "CONTAINS":
                return "(strpos(" + Arg(1) + ", " + Arg(0) + ") > 0)";
            case "STARTSWITH":
                return "(strpos(" + Arg(0) + ", " + Arg(1) + ") = 1)";
            case "ENDSWITH":
                return "(substr(" + Arg(0) + ", -length(" + Arg(1) + ")) = " + Arg(1) + ")";
            case "INDEXOF":
                return "strpos(" + Arg(1) + ", " + Arg(0) + ")";
            case "SUBSTRING":
                return arguments.Count >= 3
                    ? "substr(" + Arg(0) + ", " + Arg(1) + ", " + Arg(2) + ")"
                    : "substr(" + Arg(0) + ", " + Arg(1) + ")";
            case "CONCAT":
            {
                var parts = new List<String>();
                foreach (var argument in arguments) parts.Add(Visit(argument));
                return "concat(" + String.Join(", ", parts) + ")";
            }
            case "LENGTH":
                return "length(" + Arg(0) + ")";
            case "TOLOWER":
                return "lower(" + Arg(0) + ")";
            case "TOUPPER":
                return "upper(" + Arg(0) + ")";
            case "TRIM":
                return "trim(" + Arg(0) + ")";
            case "LTRIM":
                return "ltrim(" + Arg(0) + ")";
            case "RTRIM":
                return "rtrim(" + Arg(0) + ")";
            case "LEFT":
                return "substr(" + Arg(0) + ", 1, " + Arg(1) + ")";
            case "RIGHT":
                return "substr(" + Arg(0) + ", -" + Arg(1) + ")";
            case "REVERSE":
                return "reverse(" + Arg(0) + ")";
            case "REPLACE":
                return "replace(" + Arg(0) + ", " + Arg(1) + ", " + Arg(2) + ")";
            case "YEAR":
                return "year(" + Arg(0) + ")";
            case "MONTH":
                return "month(" + Arg(0) + ")";
            case "DAY":
                return "day(" + Arg(0) + ")";
            case "HOUR":
                return "hour(" + Arg(0) + ")";
            case "MINUTE":
                return "minute(" + Arg(0) + ")";
            case "SECOND":
                return "second(" + Arg(0) + ")";
            case "CURRENTDATETIME":
            case "CURRENTUTCDATETIME":
            case "CURRENTDATETIMEOFFSET":
            case "GETDATE":
                return "now()";
            case "CURRENTUSER":
                return "current_user";
            case "NEWGUID":
                return "uuid()";
            case "ADDDAYS":
                return "date_add('day', " + Arg(1) + ", " + Arg(0) + ")";
            case "ADDHOURS":
                return "date_add('hour', " + Arg(1) + ", " + Arg(0) + ")";
            case "ADDMILLISECONDS":
                return "date_add('millisecond', " + Arg(1) + ", " + Arg(0) + ")";
            case "ADDMINUTES":
                return "date_add('minute', " + Arg(1) + ", " + Arg(0) + ")";
            case "ADDMONTHS":
                return "date_add('month', " + Arg(1) + ", " + Arg(0) + ")";
            case "ADDSECONDS":
                return "date_add('second', " + Arg(1) + ", " + Arg(0) + ")";
            case "ADDYEARS":
                return "date_add('year', " + Arg(1) + ", " + Arg(0) + ")";
            case "DIFFDAYS":
                return "date_diff('day', " + Arg(0) + ", " + Arg(1) + ")";
            case "DIFFHOURS":
                return "date_diff('hour', " + Arg(0) + ", " + Arg(1) + ")";
            case "DIFFMILLISECONDS":
                return "date_diff('millisecond', " + Arg(0) + ", " + Arg(1) + ")";
            case "DIFFMINUTES":
                return "date_diff('minute', " + Arg(0) + ", " + Arg(1) + ")";
            case "DIFFMONTHS":
                return "date_diff('month', " + Arg(0) + ", " + Arg(1) + ")";
            case "DIFFSECONDS":
                return "date_diff('second', " + Arg(0) + ", " + Arg(1) + ")";
            case "DIFFYEARS":
                return "date_diff('year', " + Arg(0) + ", " + Arg(1) + ")";
            case "ABS":
                return "abs(" + Arg(0) + ")";
            case "CEILING":
                return "ceiling(" + Arg(0) + ")";
            case "FLOOR":
                return "floor(" + Arg(0) + ")";
            case "POWER":
                return "power(" + Arg(0) + ", " + Arg(1) + ")";
            case "SQRT":
                return "sqrt(" + Arg(0) + ")";
            case "ROUND":
                return arguments.Count >= 2 ? "round(" + Arg(0) + ", " + Arg(1) + ")" : "round(" + Arg(0) + ")";
            case "TRUNCATE":
                return arguments.Count >= 2 ? "truncate(" + Arg(0) + ", " + Arg(1) + ")" : "truncate(" + Arg(0) + ")";
            case "BITWISEAND":
                return "bitwise_and(" + Arg(0) + ", " + Arg(1) + ")";
            case "BITWISEOR":
                return "bitwise_or(" + Arg(0) + ", " + Arg(1) + ")";
            case "BITWISEXOR":
                return "bitwise_xor(" + Arg(0) + ", " + Arg(1) + ")";
            case "BITWISENOT":
                return "bitwise_not(" + Arg(0) + ")";
            default:
                throw new NotSupportedException(Localization.Translate("Unsupported canonical function: {0}.", function.Function.Name));
        }
    }
    #endregion

    #region 字面量与类型
    /// <summary>格式化字面量</summary>
    private static String FormatLiteral(Object? value)
    {
        switch (value)
        {
            case null:
            case DBNull:
                return "NULL";
            case String text:
                return QuoteLiteral(text);
            case Char ch:
                return QuoteLiteral(ch.ToString());
            case Boolean flag:
                return flag ? "TRUE" : "FALSE";
            case Byte or SByte or Int16 or Int32 or Int64:
                return Convert.ToString(value, CultureInfo.InvariantCulture)!;
            case Single single:
                return single.ToString("R", CultureInfo.InvariantCulture);
            case Double number:
                return number.ToString("R", CultureInfo.InvariantCulture);
            case Decimal money:
                return money.ToString(CultureInfo.InvariantCulture);
            case DateTime time:
                return "TIMESTAMP '" + time.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + "'";
            case DateTimeOffset offset:
                return "TIMESTAMP '" + offset.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture) + "'";
            case TimeSpan:
                throw new NotSupportedException(Localization.Translate("TimeSpan literals are not supported by the Presto provider."));
            case Guid guid:
                return "UUID '" + guid.ToString("D") + "'";
            case Byte[] bytes:
                return "X'" + BitConverter.ToString(bytes).Replace("-", "") + "'";
            default:
                throw new NotSupportedException(Localization.Translate("Unsupported literal type: {0}.", value.GetType().Name));
        }
    }

    private static String QuoteLiteral(String value) => "'" + value.Replace("'", "''") + "'";

    /// <summary>获取 EDM 类型对应的 Presto 类型名</summary>
    private static String GetStoreTypeName(TypeUsage typeUsage)
    {
        if (typeUsage.EdmType is not PrimitiveType primitive)
            throw new NotSupportedException(Localization.Translate("Unsupported store type: {0}.", typeUsage.EdmType.Name));

        switch (primitive.PrimitiveTypeKind)
        {
            case PrimitiveTypeKind.Boolean: return "boolean";
            case PrimitiveTypeKind.Byte:
            case PrimitiveTypeKind.SByte: return "tinyint";
            case PrimitiveTypeKind.Int16: return "smallint";
            case PrimitiveTypeKind.Int32: return "integer";
            case PrimitiveTypeKind.Int64: return "bigint";
            case PrimitiveTypeKind.Single: return "real";
            case PrimitiveTypeKind.Double: return "double";
            case PrimitiveTypeKind.Decimal:
            {
                var precision = 38;
                var scale = 18;
                if (typeUsage.Facets.TryGetValue("Precision", false, out var p) && p.Value is Byte pb) precision = pb;
                if (typeUsage.Facets.TryGetValue("Scale", false, out var s) && s.Value is Byte sb) scale = sb;
                return "decimal(" + precision.ToString(CultureInfo.InvariantCulture) + ", " + scale.ToString(CultureInfo.InvariantCulture) + ")";
            }
            case PrimitiveTypeKind.DateTime:
            case PrimitiveTypeKind.DateTimeOffset: return "timestamp";
            case PrimitiveTypeKind.Time: return "time";
            case PrimitiveTypeKind.String: return "varchar";
            case PrimitiveTypeKind.Binary: return "varbinary";
            case PrimitiveTypeKind.Guid: return "varchar";
            default: throw new NotSupportedException(Localization.Translate("Unsupported store type: {0}.", primitive.PrimitiveTypeKind));
        }
    }

    /// <summary>获取表名，Schema 支持点分多级（catalog.schema）</summary>
    private static String GetTableName(DbScanExpression scan)
    {
        var table = scan.Target.Table;
        if (String.IsNullOrEmpty(table)) table = scan.Target.Name;

        var schema = scan.Target.Schema;
        if (String.IsNullOrEmpty(schema) || String.Equals(schema, "dbo", StringComparison.OrdinalIgnoreCase)) return Quote(table!);

        var parts = new List<String>();
        foreach (var part in schema!.Split('.'))
        {
            if (!String.IsNullOrEmpty(part)) parts.Add(Quote(part));
        }
        parts.Add(Quote(table!));

        return String.Join(".", parts);
    }

    /// <summary>引号包裹标识符</summary>
    internal static String Quote(String identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";
    #endregion

    #region 内部类型
    /// <summary>查询片段</summary>
    private sealed class Query
    {
        public String Select = "*";
        public String Source = "";
        public String Where = "";
        public String GroupBy = "";
        public String OrderBy = "";
        public String Limit = "";
        public String Offset = "";
        public Boolean Distinct;

        /// <summary>是否为不透明查询。不透明查询不能被外层合并，必须作为子查询</summary>
        public Boolean Barrier;

        public String ToSql()
        {
            var sb = new StringBuilder("SELECT ");
            if (Distinct) sb.Append("DISTINCT ");
            sb.Append(Select);

            if (!String.IsNullOrEmpty(Source)) sb.Append(" FROM ").Append(Source);
            if (!String.IsNullOrEmpty(Where)) sb.Append(" WHERE ").Append(Where);
            if (!String.IsNullOrEmpty(GroupBy)) sb.Append(" GROUP BY ").Append(GroupBy);
            if (!String.IsNullOrEmpty(OrderBy)) sb.Append(" ORDER BY ").Append(OrderBy);
            if (!String.IsNullOrEmpty(Offset)) sb.Append(" OFFSET ").Append(Offset);
            if (!String.IsNullOrEmpty(Limit)) sb.Append(" LIMIT ").Append(Limit);

            return sb.ToString();
        }
    }
    #endregion
}
