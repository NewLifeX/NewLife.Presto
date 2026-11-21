using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace NewLife.Presto.EntityFrameworkCore;

/// <summary>Presto 字符串方法翻译器。使用 strpos/substr 等函数实现 .NET 字符串语义</summary>
public class PrestoStringMethodTranslator : IMethodCallTranslator
{
    private readonly ISqlExpressionFactory _sqlExpressionFactory;

    /// <summary>实例化</summary>
    /// <param name="sqlExpressionFactory">SQL 表达式工厂</param>
    public PrestoStringMethodTranslator(ISqlExpressionFactory sqlExpressionFactory) => _sqlExpressionFactory = sqlExpressionFactory;

    /// <summary>翻译方法调用</summary>
    /// <param name="instance">实例表达式</param>
    /// <param name="method">方法</param>
    /// <param name="arguments">参数</param>
    /// <param name="logger">日志</param>
    /// <returns>SQL 表达式；无法翻译时返回空</returns>
    public SqlExpression? Translate(SqlExpression? instance, MethodInfo method, IReadOnlyList<SqlExpression> arguments, IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        if (method.DeclaringType != typeof(String)) return null;

        switch (method.Name)
        {
            case nameof(String.Contains) when instance != null && arguments.Count == 1:
                return _sqlExpressionFactory.GreaterThan(Function("strpos", typeof(Int64), instance, arguments[0]), _sqlExpressionFactory.Constant(0));
            case nameof(String.StartsWith) when instance != null && arguments.Count == 1:
                return _sqlExpressionFactory.Equal(Function("strpos", typeof(Int64), instance, arguments[0]), _sqlExpressionFactory.Constant(1));
            case nameof(String.EndsWith) when instance != null && arguments.Count == 1:
                return _sqlExpressionFactory.Equal(Function("substr", typeof(String), instance, _sqlExpressionFactory.Negate(Function("length", typeof(Int64), arguments[0]))), arguments[0]);
            case nameof(String.IndexOf) when instance != null && arguments.Count == 1:
                return _sqlExpressionFactory.Subtract(Function("strpos", typeof(Int64), instance, arguments[0]), _sqlExpressionFactory.Constant(1));
            case nameof(String.ToLower) when instance != null:
                return Function("lower", typeof(String), instance);
            case nameof(String.ToUpper) when instance != null:
                return Function("upper", typeof(String), instance);
            case nameof(String.Trim) when instance != null:
                return Function("trim", typeof(String), instance);
            case nameof(String.TrimStart) when instance != null:
                return Function("ltrim", typeof(String), instance);
            case nameof(String.TrimEnd) when instance != null:
                return Function("rtrim", typeof(String), instance);
            case nameof(String.Substring) when instance != null && arguments.Count >= 1:
                return arguments.Count == 1
                    ? Function("substr", typeof(String), instance, _sqlExpressionFactory.Add(arguments[0], _sqlExpressionFactory.Constant(1)))
                    : Function("substr", typeof(String), instance, _sqlExpressionFactory.Add(arguments[0], _sqlExpressionFactory.Constant(1)), arguments[1]);
            case nameof(String.Replace) when instance != null && arguments.Count == 2:
                return Function("replace", typeof(String), instance, arguments[0], arguments[1]);
            case nameof(String.Concat) when arguments.Count == 2:
                return Function("concat", typeof(String), arguments[0], arguments[1]);
            case nameof(String.IsNullOrEmpty) when arguments.Count == 1:
                return _sqlExpressionFactory.OrElse(
                    _sqlExpressionFactory.IsNull(arguments[0]),
                    _sqlExpressionFactory.Equal(arguments[0], _sqlExpressionFactory.Constant(String.Empty)));
            case nameof(String.IsNullOrWhiteSpace) when arguments.Count == 1:
                return _sqlExpressionFactory.OrElse(
                    _sqlExpressionFactory.IsNull(arguments[0]),
                    _sqlExpressionFactory.Equal(Function("trim", typeof(String), arguments[0]), _sqlExpressionFactory.Constant(String.Empty)));
            case nameof(String.Equals) when arguments.Count == 2:
                return _sqlExpressionFactory.Equal(arguments[0], arguments[1]);
            default:
                return null;
        }
    }

    private SqlExpression Function(String name, Type returnType, params SqlExpression[] arguments)
        => _sqlExpressionFactory.Function(name, arguments, true, arguments.Select(_ => true), returnType);
}
