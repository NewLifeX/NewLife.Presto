using Microsoft.EntityFrameworkCore.Query;

namespace NewLife.Presto.EntityFrameworkCore;

/// <summary>Presto 方法调用翻译器提供者。在 EF Core 默认翻译器之外补充 Presto 专用实现</summary>
public class PrestoMethodCallTranslatorProvider : RelationalMethodCallTranslatorProvider
{
    /// <summary>实例化</summary>
    /// <param name="dependencies">依赖</param>
    public PrestoMethodCallTranslatorProvider(RelationalMethodCallTranslatorProviderDependencies dependencies)
        : base(dependencies)
    {
        var factory = dependencies.SqlExpressionFactory;
        AddTranslators(
        [
            new PrestoStringMethodTranslator(factory),
            new PrestoMathTranslator(factory),
            new PrestoDateTimeMethodTranslator(factory),
        ]);
    }
}
