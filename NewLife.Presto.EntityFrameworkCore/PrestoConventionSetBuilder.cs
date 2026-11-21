using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;

namespace NewLife.Presto.EntityFrameworkCore;

/// <summary>Presto 约定集构建器。Presto 无自增与序列，使用基类关系型约定</summary>
public class PrestoConventionSetBuilder : RelationalConventionSetBuilder
{
    /// <summary>实例化</summary>
    /// <param name="dependencies">提供程序约定依赖</param>
    /// <param name="relationalDependencies">关系型约定依赖</param>
    public PrestoConventionSetBuilder(
        ProviderConventionSetBuilderDependencies dependencies,
        RelationalConventionSetBuilderDependencies relationalDependencies)
        : base(dependencies, relationalDependencies) { }
}
