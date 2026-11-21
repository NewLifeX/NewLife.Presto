using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Globalization;

namespace NewLife.Presto;

/// <summary>元数据集合提供者</summary>
/// <remarks>
/// `Tables`/`Columns` 查询 `{catalog}.information_schema`；`Catalogs`/`Schemas` 使用 `SHOW`。
/// 查询结果短期缓存，`ChangeDatabase` 时清空。
/// </remarks>
internal static class PrestoMetaData
{
    #region 集合
    /// <summary>获取元数据表</summary>
    /// <param name="connection">连接</param>
    /// <param name="collectionName">集合名</param>
    /// <param name="restrictions">过滤值</param>
    /// <returns>元数据表</returns>
    public static DataTable GetSchema(PrestoConnection connection, String collectionName, String?[]? restrictions)
    {
        if (collectionName.IsNullOrWhiteSpace())
            throw new ArgumentException(Localization.Translate("Schema collection name cannot be empty."), nameof(collectionName));

        var name = collectionName.Trim();
        var cacheable = false;
        DataTable table;
        if (name.EqualIgnoreCase("MetaDataCollections")) table = BuildMetaDataCollections();
        else if (name.EqualIgnoreCase("DataTypes")) table = BuildDataTypes();
        else
        {
            cacheable = true;
            var key = BuildKey(name, restrictions);
            if (connection.SchemaCache.TryGetValue(key, out var entry) && entry.ExpireTime > DateTime.UtcNow)
                return entry.Table.Copy();

            if (name.EqualIgnoreCase("Tables")) table = GetTables(connection, restrictions);
            else if (name.EqualIgnoreCase("Columns")) table = GetColumns(connection, restrictions);
            else if (name.EqualIgnoreCase("Catalogs")) table = GetCatalogs(connection);
            else if (name.EqualIgnoreCase("Schemas")) table = GetSchemas(connection, restrictions);
            else throw new ArgumentException(Localization.Translate("Schema collection '{0}' is not supported.", name), nameof(collectionName));

            connection.SchemaCache[key] = new PrestoSchemaCacheEntry
            {
                ExpireTime = DateTime.UtcNow + connection.SchemaCacheExpire,
                Table = table,
            };
        }

        return cacheable ? table.Copy() : table;
    }

    private static DataTable BuildMetaDataCollections()
    {
        var table = CreateTable(
            ["CollectionName", "NumberOfRestrictions", "NumberOfIdentifierParts"],
            [typeof(String), typeof(Int32), typeof(Int32)]);

        AddCollection(table, "MetaDataCollections", 0, 0);
        AddCollection(table, "DataTypes", 0, 0);
        AddCollection(table, "Tables", 4, 3);
        AddCollection(table, "Columns", 4, 3);
        AddCollection(table, "Catalogs", 0, 0);
        AddCollection(table, "Schemas", 2, 1);

        return table;
    }

    private static void AddCollection(DataTable table, String name, Int32 restrictions, Int32 identifierParts)
    {
        var row = table.NewRow();
        row[0] = name;
        row[1] = restrictions;
        row[2] = identifierParts;
        table.Rows.Add(row);
    }

    private static DataTable BuildDataTypes()
    {
        var table = CreateTable(
            ["TypeName", "ProviderDbType", "ColumnSize", "CreateFormat", "CreateParameters", "DataType", "IsComplex"],
            [typeof(String), typeof(Int32), typeof(Int32), typeof(String), typeof(String), typeof(String), typeof(Boolean)]);

        foreach (var item in PrestoTypeMapper.DataTypes)
        {
            var row = table.NewRow();
            row["TypeName"] = item.TypeName;
            row["ProviderDbType"] = (Int32)PrestoTypeMapper.GetDbType(item.TypeName);
            row["ColumnSize"] = -1;
            row["CreateFormat"] = item.TypeName.ToUpperInvariant();
            row["CreateParameters"] = GetCreateParameters(item.TypeName);
            row["DataType"] = item.FieldType.FullName;
            row["IsComplex"] = item.IsComplex;
            table.Rows.Add(row);
        }

        return table;
    }

    private static String GetCreateParameters(String typeName)
    {
        var text = typeName.ToLowerInvariant();
        if (text is "decimal") return "precision, scale";
        if (text is "varchar" or "char") return "length";
        if (text is "timestamp" or "time") return "precision";

        return String.Empty;
    }
    #endregion

    #region 查询集合
    private static DataTable GetTables(PrestoConnection connection, String?[]? restrictions)
    {
        var catalog = GetCatalog(connection, restrictions, "Tables");
        var schema = GetRestriction(restrictions, 1);
        var tableName = GetRestriction(restrictions, 2);
        var tableType = GetRestriction(restrictions, 3);

        var filters = new List<String>();
        using var command = CreateCommand(connection);
        if (schema != null)
        {
            filters.Add("table_schema = @schema");
            command.Parameters.Add(new PrestoParameter("@schema", schema));
        }
        if (tableName != null)
        {
            filters.Add("table_name = @table");
            command.Parameters.Add(new PrestoParameter("@table", tableName));
        }
        if (tableType != null)
        {
            filters.Add("table_type = @tableType");
            command.Parameters.Add(new PrestoParameter("@tableType", tableType));
        }

        command.CommandText = "SELECT table_catalog, table_schema, table_name, table_type FROM " + QuoteIdentifier(catalog) + ".information_schema.tables";
        if (filters.Count > 0) command.CommandText += " WHERE " + String.Join(" AND ", filters.ToArray());

        return ExecuteTable(
            command,
            ["TABLE_CATALOG", "TABLE_SCHEMA", "TABLE_NAME", "TABLE_TYPE"],
            [typeof(String), typeof(String), typeof(String), typeof(String)]);
    }

    private static DataTable GetColumns(PrestoConnection connection, String?[]? restrictions)
    {
        var catalog = GetCatalog(connection, restrictions, "Columns");
        var schema = GetRestriction(restrictions, 1);
        var tableName = GetRestriction(restrictions, 2);
        var columnName = GetRestriction(restrictions, 3);

        var filters = new List<String>();
        using var command = CreateCommand(connection);
        if (schema != null)
        {
            filters.Add("table_schema = @schema");
            command.Parameters.Add(new PrestoParameter("@schema", schema));
        }
        if (tableName != null)
        {
            filters.Add("table_name = @table");
            command.Parameters.Add(new PrestoParameter("@table", tableName));
        }
        if (columnName != null)
        {
            filters.Add("column_name = @column");
            command.Parameters.Add(new PrestoParameter("@column", columnName));
        }

        command.CommandText = "SELECT table_catalog, table_schema, table_name, column_name, ordinal_position, column_default, is_nullable, data_type FROM " + QuoteIdentifier(catalog) + ".information_schema.columns";
        if (filters.Count > 0) command.CommandText += " WHERE " + String.Join(" AND ", filters.ToArray());

        return ExecuteTable(
            command,
            ["TABLE_CATALOG", "TABLE_SCHEMA", "TABLE_NAME", "COLUMN_NAME", "ORDINAL_POSITION", "COLUMN_DEFAULT", "IS_NULLABLE", "DATA_TYPE"],
            [typeof(String), typeof(String), typeof(String), typeof(String), typeof(Int32), typeof(String), typeof(String), typeof(String)]);
    }

    private static DataTable GetCatalogs(PrestoConnection connection)
    {
        using var command = CreateCommand(connection);
        command.CommandText = "SHOW CATALOGS";

        var table = CreateTable(["CATALOG_NAME"], [typeof(String)]);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var row = table.NewRow();
            row[0] = GetCell(reader, 0, typeof(String));
            table.Rows.Add(row);
        }

        return table;
    }

    private static DataTable GetSchemas(PrestoConnection connection, String?[]? restrictions)
    {
        var catalog = GetCatalog(connection, restrictions, "Schemas");
        var schema = GetRestriction(restrictions, 1);

        using var command = CreateCommand(connection);
        command.CommandText = "SHOW SCHEMAS FROM " + QuoteIdentifier(catalog);

        var table = CreateTable(["SCHEMA_NAME"], [typeof(String)]);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var value = GetCell(reader, 0, typeof(String));
            if (schema != null && !String.Equals(Convert.ToString(value, CultureInfo.InvariantCulture), schema, StringComparison.OrdinalIgnoreCase)) continue;

            var row = table.NewRow();
            row[0] = value;
            table.Rows.Add(row);
        }

        return table;
    }

    private static DataTable ExecuteTable(DbCommand command, String[] names, Type[] types)
    {
        var table = CreateTable(names, types);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var row = table.NewRow();
            for (var i = 0; i < names.Length; i++)
            {
                row[i] = GetCell(reader, i, types[i]);
            }
            table.Rows.Add(row);
        }

        return table;
    }

    private static Object GetCell(DbDataReader reader, Int32 ordinal, Type type)
    {
        if (reader.IsDBNull(ordinal)) return DBNull.Value;

        var value = reader.GetValue(ordinal);
        if (type == typeof(String)) return Convert.ToString(value, CultureInfo.InvariantCulture) ?? String.Empty;

        return System.Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
    }
    #endregion

    #region 辅助
    private static DataTable CreateTable(String[] names, Type[] types)
    {
        var table = new DataTable { Locale = CultureInfo.InvariantCulture };
        for (var i = 0; i < names.Length; i++)
        {
            AddColumn(table, names[i], types[i]);
        }

        return table;
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2062", Justification = "元数据表列类型均为当前程序集中的固定基础类型，仅作为 DataTable 容器使用")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2067", Justification = "元数据表列类型均为当前程序集中的固定基础类型，仅作为 DataTable 容器使用")]
    private static void AddColumn(DataTable table, String name, Type type) => table.Columns.Add(name, type);

    private static PrestoCommand CreateCommand(PrestoConnection connection)
    {
        var command = (PrestoCommand)connection.CreateCommand();
        return command;
    }

    private static String GetCatalog(PrestoConnection connection, String?[]? restrictions, String collection)
    {
        var catalog = GetRestriction(restrictions, 0) ?? connection.Database;
        if (catalog.IsNullOrEmpty())
            throw new PrestoException(Localization.Translate("Catalog is required for the '{0}' schema collection.", collection));

        return catalog;
    }

    private static String? GetRestriction(String?[]? restrictions, Int32 index)
    {
        if (restrictions == null || index >= restrictions.Length) return null;

        var value = restrictions[index];
        return value.IsNullOrWhiteSpace() ? null : value.Trim();
    }

    private static String BuildKey(String name, String?[]? restrictions)
    {
        var builder = new System.Text.StringBuilder(name.ToUpperInvariant());
        if (restrictions == null) return builder.ToString();

        foreach (var item in restrictions)
        {
            builder.Append('|').Append(item ?? String.Empty);
        }

        return builder.ToString();
    }

    private static String QuoteIdentifier(String name) => "\"" + name.Replace("\"", "\"\"") + "\"";
    #endregion
}

/// <summary>Schema 集合缓存项</summary>
internal sealed class PrestoSchemaCacheEntry
{
    /// <summary>过期时间</summary>
    public DateTime ExpireTime { get; set; }

    /// <summary>缓存表</summary>
    public DataTable Table { get; set; } = new();
}
