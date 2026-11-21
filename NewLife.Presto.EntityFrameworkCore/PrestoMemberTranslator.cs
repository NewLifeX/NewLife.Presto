using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace NewLife.Presto.EntityFrameworkCore;

/// <summary>Presto 成员翻译器。处理 Length 与日期时间分量等成员访问</summary>
public class PrestoMemberTranslator : IMemberTranslator
{
    private readonly ISqlExpressionFactory _sqlExpressionFactory;

    /// <summary>实例化</summary>
    /// <param name="sqlExpressionFactory">SQL 表达式工厂</param>
    public PrestoMemberTranslator(ISqlExpressionFactory sqlExpressionFactory) => _sqlExpressionFactory = sqlExpressionFactory;

    /// <summary>翻译成员访问</summary>
    /// <param name="instance">实例表达式</param>
    /// <param name="member">成员</param>
    /// <param name="returnType">返回类型</param>
    /// <param name="logger">日志</param>
    /// <returns>SQL 表达式；无法翻译时返回空</returns>
    public SqlExpression? Translate(SqlExpression? instance, MemberInfo member, Type returnType, IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        if (member.DeclaringType == typeof(String) && member.Name == nameof(String.Length) && instance != null)
            return _sqlExpressionFactory.Function("length", [ instance ], true, [ true ], typeof(Int32));

        if (member.DeclaringType != typeof(DateTime) && member.DeclaringType != typeof(DateTimeOffset)) return null;

        if (instance == null)
        {
            if (member.Name == nameof(DateTime.UtcNow)) return _sqlExpressionFactory.Function("now", [], true, [], returnType);

            return member.Name switch
            {
                nameof(DateTime.Now) or nameof(DateTimeOffset.Now) => _sqlExpressionFactory.Function("now", [], true, [], returnType),
                nameof(DateTime.Today) => _sqlExpressionFactory.Function("current_date", [], true, [], returnType),
                _ => null,
            };
        }

        var name = member.Name switch
        {
            nameof(DateTime.Year) => "year",
            nameof(DateTime.Month) => "month",
            nameof(DateTime.Day) => "day",
            nameof(DateTime.Hour) => "hour",
            nameof(DateTime.Minute) => "minute",
            nameof(DateTime.Second) => "second",
            nameof(DateTime.Millisecond) => "millisecond",
            _ => null,
        };

        return name == null ? null : _sqlExpressionFactory.Function(name, [ instance ], true, [ true ], typeof(Int32));
    }
}
