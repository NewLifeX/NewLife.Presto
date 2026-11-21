using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace NewLife.Presto.EntityFrameworkCore;

/// <summary>Presto 注解提供者。为模型与迁移提供提供程序注解</summary>
public class PrestoAnnotationProvider : RelationalAnnotationProvider
{
    /// <summary>实例化</summary>
    /// <param name="dependencies">依赖</param>
    public PrestoAnnotationProvider(RelationalAnnotationProviderDependencies dependencies) : base(dependencies) { }
}
