using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace NewLife.Presto.EntityFrameworkCore;

/// <summary>Presto 日期时间方法翻译器。把 .NET 的 AddXxx 映射为 date_add</summary>
public class PrestoDateTimeMethodTranslator : IMethodCallTranslator
{
    private readonly ISqlExpressionFactory _sqlExpressionFactory;

    /// <summary>实例化</summary>
    /// <param name="sqlExpressionFactory">SQL 表达式工厂</param>
    public PrestoDateTimeMethodTranslator(ISqlExpressionFactory sqlExpressionFactory) => _sqlExpressionFactory = sqlExpressionFactory;

    /// <summary>翻译方法调用</summary>
    /// <param name="instance">实例表达式</param>
    /// <param name="method">方法</param>
    /// <param name="arguments">参数</param>
    /// <param name="logger">日志</param>
    /// <returns>SQL 表达式；无法翻译时返回空</returns>
    public SqlExpression? Translate(SqlExpression? instance, MethodInfo method, IReadOnlyList<SqlExpression> arguments, IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        if (instance == null || arguments.Count != 1) return null;
        if (method.DeclaringType != typeof(DateTime) && method.DeclaringType != typeof(DateTimeOffset)) return null;

        var unit = method.Name switch
        {
            nameof(DateTime.AddYears) => "year",
            nameof(DateTime.AddMonths) => "month",
            nameof(DateTime.AddDays) => "day",
            nameof(DateTime.AddHours) => "hour",
            nameof(DateTime.AddMinutes) => "minute",
            nameof(DateTime.AddSeconds) => "second",
            nameof(DateTime.AddMilliseconds) => "millisecond",
            _ => null,
        };
        if (unit == null) return null;

        return _sqlExpressionFactory.Function("date_add", [ _sqlExpressionFactory.Constant(unit), arguments[0], instance ], true, [ true, true, true ], method.ReturnType);
    }
}
