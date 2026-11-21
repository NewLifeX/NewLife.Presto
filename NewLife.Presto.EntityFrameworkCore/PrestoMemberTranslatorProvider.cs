using Microsoft.EntityFrameworkCore.Query;

namespace NewLife.Presto.EntityFrameworkCore;

/// <summary>Presto 成员翻译器提供者。在 EF Core 默认翻译器之外补充 Presto 专用实现</summary>
public class PrestoMemberTranslatorProvider : RelationalMemberTranslatorProvider
{
    /// <summary>实例化</summary>
    /// <param name="dependencies">依赖</param>
    public PrestoMemberTranslatorProvider(RelationalMemberTranslatorProviderDependencies dependencies)
        : base(dependencies) => AddTranslators([ new PrestoMemberTranslator(dependencies.SqlExpressionFactory) ]);
}
