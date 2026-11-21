using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace NewLife.Presto.EntityFrameworkCore;

/// <summary>服务集合扩展方法</summary>
public static class PrestoServiceCollectionExtensions
{
    /// <summary>注册 Presto Entity Framework Core 服务到依赖注入容器</summary>
    /// <typeparam name="TContext">上下文类型</typeparam>
    /// <param name="services">服务集合</param>
    /// <param name="connectionString">Presto/Trino 连接字符串</param>
    /// <param name="prestoOptionsAction">Presto 选项配置回调</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddPresto<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors | DynamicallyAccessedMemberTypes.PublicProperties)] TContext>(
        this IServiceCollection services,
        String connectionString,
        Action<PrestoDbContextOptionsBuilder>? prestoOptionsAction = null)
        where TContext : DbContext
    {
        if (services == null) throw new ArgumentNullException(nameof(services));
        if (connectionString.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(connectionString));

        return services.AddDbContext<TContext>(options => options.UsePresto(connectionString, prestoOptionsAction));
    }
}
