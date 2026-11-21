using System.Data.Common;
using System.Data.Entity.Core.Common;
using System.Data.Entity.Core.Common.CommandTrees;
using System.Data.Entity.Core.Metadata.Edm;
using System.Data.Entity.Infrastructure;
using NewLife.Presto;

namespace NewLife.Presto.EntityFramework;

/// <summary>Presto EF6 提供程序服务。只支持查询，不支持写入、事务与迁移</summary>
public class PrestoProviderServices : DbProviderServices
{
    /// <summary>默认实例</summary>
    public static readonly PrestoProviderServices Instance = new();

    /// <summary>获取数据库提供程序清单</summary>
    /// <param name="manifestToken">清单标记</param>
    /// <returns>提供程序清单</returns>
    protected override DbProviderManifest GetDbProviderManifest(String manifestToken) => new PrestoProviderManifest(manifestToken);

    /// <summary>获取数据库提供程序清单标记</summary>
    /// <param name="connection">数据库连接</param>
    /// <returns>清单标记</returns>
    protected override String GetDbProviderManifestToken(DbConnection connection) => "1.0";

    /// <summary>创建数据库命令定义</summary>
    /// <param name="providerManifest">提供程序清单</param>
    /// <param name="commandTree">命令树</param>
    /// <returns>命令定义</returns>
    protected override DbCommandDefinition CreateDbCommandDefinition(DbProviderManifest providerManifest, DbCommandTree commandTree)
    {
        if (providerManifest == null) throw new ArgumentNullException(nameof(providerManifest));
        if (commandTree == null) throw new ArgumentNullException(nameof(commandTree));

        var result = new PrestoSqlGenerator().GenerateSql(commandTree);
        return new PrestoCommandDefinition(result.CommandText, result.Parameters);
    }

    #region 数据库管理
    /// <summary>检查数据库是否可用</summary>
    /// <param name="connection">数据库连接</param>
    /// <param name="commandTimeout">命令超时时间（秒）</param>
    /// <param name="storeItemCollection">存储项集合</param>
    /// <returns>是否可用</returns>
    protected override Boolean DbDatabaseExists(DbConnection connection, Int32? commandTimeout, StoreItemCollection storeItemCollection)
    {
        if (connection == null) throw new ArgumentNullException(nameof(connection));

        try
        {
            var wasClosed = connection.State != System.Data.ConnectionState.Open;
            if (wasClosed) connection.Open();
            try
            {
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT 1";
                if (commandTimeout.HasValue) command.CommandTimeout = commandTimeout.Value;
                command.ExecuteScalar();

                return true;
            }
            finally
            {
                if (wasClosed) connection.Close();
            }
        }
        catch
        {
            return false;
        }
    }

    /// <summary>创建数据库。Presto/Trino 不支持由驱动执行 DDL</summary>
    protected override void DbCreateDatabase(DbConnection connection, Int32? commandTimeout, StoreItemCollection storeItemCollection)
        => throw new NotSupportedException(Localization.Translate("The provider does not support database creation or deletion."));

    /// <summary>删除数据库。Presto/Trino 不支持由驱动执行 DDL</summary>
    protected override void DbDeleteDatabase(DbConnection connection, Int32? commandTimeout, StoreItemCollection storeItemCollection)
        => throw new NotSupportedException(Localization.Translate("The provider does not support database creation or deletion."));

    /// <summary>生成建库脚本。Presto/Trino 不支持由驱动执行 DDL</summary>
    protected override String DbCreateDatabaseScript(String providerManifestToken, StoreItemCollection storeItemCollection)
        => throw new NotSupportedException(Localization.Translate("The provider does not support database creation or deletion."));
    #endregion
}
