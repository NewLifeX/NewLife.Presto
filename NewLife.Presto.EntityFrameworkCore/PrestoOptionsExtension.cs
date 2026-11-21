using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Update;
using Microsoft.Extensions.DependencyInjection;

namespace NewLife.Presto.EntityFrameworkCore;

/// <summary>Presto EF Core 选项扩展。注册 Presto/Trino 提供程序所需的服务</summary>
public class PrestoOptionsExtension : RelationalOptionsExtension
{
    private DbContextOptionsExtensionInfo? _info;

    /// <summary>实例化</summary>
    public PrestoOptionsExtension() { }

    /// <summary>复制构造</summary>
    /// <param name="copyFrom">源扩展</param>
    protected PrestoOptionsExtension(PrestoOptionsExtension copyFrom) : base(copyFrom) { }

    /// <summary>扩展信息</summary>
    public override DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

    /// <summary>克隆</summary>
    /// <returns>扩展副本</returns>
    protected override RelationalOptionsExtension Clone() => new PrestoOptionsExtension(this);

    /// <summary>注册 Presto/Trino 提供程序服务</summary>
    /// <param name="services">服务集合</param>
    public override void ApplyServices(IServiceCollection services)
    {
        if (services == null) throw new ArgumentNullException(nameof(services));

        new EntityFrameworkRelationalServicesBuilder(services)
            .TryAdd<IDatabaseProvider, DatabaseProvider<PrestoOptionsExtension>>()
            .TryAdd<LoggingDefinitions, PrestoLoggingDefinitions>()
            .TryAdd<IRelationalTypeMappingSource, PrestoTypeMappingSource>()
            .TryAdd<ISqlGenerationHelper, PrestoSqlGenerationHelper>()
            .TryAdd<IRelationalConnection, PrestoRelationalConnection>()
            .TryAdd<IRelationalDatabaseCreator, PrestoDatabaseCreator>()
            .TryAdd<IQuerySqlGeneratorFactory, PrestoQuerySqlGeneratorFactory>()
            .TryAdd<IMethodCallTranslatorProvider, PrestoMethodCallTranslatorProvider>()
            .TryAdd<IMemberTranslatorProvider, PrestoMemberTranslatorProvider>()
            .TryAdd<IRelationalAnnotationProvider, PrestoAnnotationProvider>()
            .TryAdd<IProviderConventionSetBuilder, PrestoConventionSetBuilder>()
            .TryAdd<IUpdateSqlGenerator, PrestoUpdateSqlGenerator>()
            .TryAdd<IModificationCommandBatchFactory, PrestoModificationCommandBatchFactory>()
            .TryAdd<IHistoryRepository, PrestoHistoryRepository>()
            .TryAdd<IMigrationsSqlGenerator, PrestoMigrationsSqlGenerator>()
            .TryAddCoreServices();
    }

    /// <summary>扩展信息实现</summary>
    private sealed class ExtensionInfo : RelationalExtensionInfo
    {
        public ExtensionInfo(IDbContextOptionsExtension extension) : base(extension) { }

        public override Boolean ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) => other is ExtensionInfo;

        public override String LogFragment => "using NewLife.Presto ";

        public override void PopulateDebugInfo(IDictionary<String, String> debugInfo)
        {
            debugInfo["NewLife.Presto:ConnectionString"] = (Extension as PrestoOptionsExtension)?.ConnectionString ?? "";
        }
    }
}
