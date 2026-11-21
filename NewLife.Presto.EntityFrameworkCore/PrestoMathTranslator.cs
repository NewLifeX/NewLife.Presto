using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace NewLife.Presto.EntityFrameworkCore;

/// <summary>Presto 数学方法翻译器</summary>
public class PrestoMathTranslator : IMethodCallTranslator
{
    private readonly ISqlExpressionFactory _sqlExpressionFactory;

    /// <summary>实例化</summary>
    /// <param name="sqlExpressionFactory">SQL 表达式工厂</param>
    public PrestoMathTranslator(ISqlExpressionFactory sqlExpressionFactory) => _sqlExpressionFactory = sqlExpressionFactory;

    /// <summary>翻译方法调用</summary>
    /// <param name="instance">实例表达式</param>
    /// <param name="method">方法</param>
    /// <param name="arguments">参数</param>
    /// <param name="logger">日志</param>
    /// <returns>SQL 表达式；无法翻译时返回空</returns>
    public SqlExpression? Translate(SqlExpression? instance, MethodInfo method, IReadOnlyList<SqlExpression> arguments, IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        if (method.DeclaringType != typeof(Math) && method.DeclaringType != typeof(MathF)) return null;
        if (arguments.Count == 0) return null;

        var returnType = method.ReturnType;
        switch (method.Name)
        {
            case nameof(Math.Abs):
                return Function("abs", returnType, arguments[0]);
            case nameof(Math.Ceiling):
                return Function("ceiling", returnType, arguments[0]);
            case nameof(Math.Floor):
                return Function("floor", returnType, arguments[0]);
            case nameof(Math.Sqrt):
                return Function("sqrt", returnType, arguments[0]);
            case nameof(Math.Exp):
                return Function("exp", returnType, arguments[0]);
            case nameof(Math.Sign):
                return Function("sign", returnType, arguments[0]);
            case nameof(Math.Truncate):
                return Function("truncate", returnType, arguments[0]);
            case nameof(Math.Log10):
                return Function("log10", returnType, arguments[0]);
            case nameof(Math.Log) when arguments.Count == 1:
                return Function("ln", returnType, arguments[0]);
            case nameof(Math.Log) when arguments.Count == 2:
                return _sqlExpressionFactory.Divide(Function("ln", returnType, arguments[0]), Function("ln", returnType, arguments[1]));
            case nameof(Math.Pow):
                return Function("power", returnType, arguments[0], arguments[1]);
            case nameof(Math.Round) when arguments.Count == 1:
                return Function("round", returnType, arguments[0]);
            case nameof(Math.Round) when arguments.Count == 2:
                return Function("round", returnType, arguments[0], arguments[1]);
            case nameof(Math.Max):
                return Function("greatest", returnType, arguments[0], arguments[1]);
            case nameof(Math.Min):
                return Function("least", returnType, arguments[0], arguments[1]);
            default:
                return null;
        }
    }

    private SqlExpression Function(String name, Type returnType, params SqlExpression[] arguments)
        => _sqlExpressionFactory.Function(name, arguments, true, arguments.Select(_ => true), returnType);
}
