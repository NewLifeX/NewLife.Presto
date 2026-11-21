using System.Data;
using Microsoft.EntityFrameworkCore.Storage;

namespace NewLife.Presto.EntityFrameworkCore;

/// <summary>Presto 数据库创建器。只读提供程序不支持建库/删库/建表</summary>
/// <remarks>Presto/Trino 的表由连接器与外部元数据管理，驱动不执行任何 DDL。</remarks>
public class PrestoDatabaseCreator : RelationalDatabaseCreator
{
    private readonly IRelationalConnection _connection;

    /// <summary>实例化</summary>
    /// <param name="dependencies">依赖</param>
    /// <param name="connection">关系型连接</param>
    public PrestoDatabaseCreator(RelationalDatabaseCreatorDependencies dependencies, IRelationalConnection connection)
        : base(dependencies) => _connection = connection;

    /// <summary>检查数据库是否可用。通过打开连接并执行 SELECT 1 判断</summary>
    /// <returns>是否可用</returns>
    public override Boolean Exists()
    {
        try
        {
            var connection = _connection.DbConnection;
            var wasOpen = connection.State == ConnectionState.Open;
            if (!wasOpen) _connection.Open();

            try
            {
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT 1";
                command.ExecuteScalar();

                return true;
            }
            finally
            {
                if (!wasOpen) _connection.Close();
            }
        }
        catch
        {
            return false;
        }
    }

    /// <summary>检查当前 catalog/schema 下是否有表</summary>
    /// <returns>是否存在表</returns>
    public override Boolean HasTables()
    {
        var connection = _connection.DbConnection;
        var catalog = connection is PrestoConnection presto ? presto.Catalog : null;
        if (catalog.IsNullOrEmpty()) return false;

        var schema = (connection as PrestoConnection)?.Schema;
        var sql = $"SELECT COUNT(*) FROM \"{catalog.Replace("\"", "\"\"")}\".information_schema.tables";
        if (!schema.IsNullOrEmpty()) sql += $" WHERE table_schema = '{schema.Replace("'", "''")}'";

        var wasOpen = connection.State == ConnectionState.Open;
        if (!wasOpen) _connection.Open();
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            var value = command.ExecuteScalar();

            return value != null && value != DBNull.Value && Convert.ToInt64(value) > 0;
        }
        finally
        {
            if (!wasOpen) _connection.Close();
        }
    }

    /// <summary>创建数据库。Presto/Trino 不支持由驱动建库</summary>
    public override void Create() => throw new NotSupportedException(Localization.Translate("The provider does not support database creation, deletion or migrations."));

    /// <summary>删除数据库。Presto/Trino 不支持由驱动删库</summary>
    public override void Delete() => throw new NotSupportedException(Localization.Translate("The provider does not support database creation, deletion or migrations."));

    /// <summary>创建表。Presto/Trino 不支持由驱动执行 DDL</summary>
    public override void CreateTables() => throw new NotSupportedException(Localization.Translate("The provider does not support database creation, deletion or migrations."));
}
