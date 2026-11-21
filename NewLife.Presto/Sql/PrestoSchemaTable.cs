using System.Collections.Generic;
using System.Data;
using System.Globalization;

namespace NewLife.Presto;

/// <summary>SchemaTable 构建器</summary>
/// <remarks>按 ADO.NET 约定输出列名、序号、类型、可空性与数值精度等元数据。</remarks>
internal static class PrestoSchemaTable
{
    /// <summary>构建 SchemaTable</summary>
    /// <param name="columns">列定义</param>
    /// <returns>SchemaTable</returns>
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2111", Justification = "DataType 列存放 System.Type 引用，仅作数据容器，不反射该类型成员；System.Type 自身成员由运行时保证可用")]
    public static DataTable Build(IList<PrestoColumn> columns)
    {
        var table = new DataTable("SchemaTable") { Locale = CultureInfo.InvariantCulture };
        table.Columns.Add("ColumnName", typeof(String));
        table.Columns.Add("ColumnOrdinal", typeof(Int32));
        table.Columns.Add("ColumnSize", typeof(Int32));
        table.Columns.Add("NumericPrecision", typeof(Int16));
        table.Columns.Add("NumericScale", typeof(Int16));
        table.Columns.Add("DataType", typeof(Type));
        table.Columns.Add("DataTypeName", typeof(String));
        table.Columns.Add("ProviderType", typeof(Int32));
        table.Columns.Add("IsLong", typeof(Boolean));
        table.Columns.Add("AllowDBNull", typeof(Boolean));
        table.Columns.Add("IsReadOnly", typeof(Boolean));
        table.Columns.Add("IsRowVersion", typeof(Boolean));
        table.Columns.Add("IsUnique", typeof(Boolean));
        table.Columns.Add("IsKey", typeof(Boolean));
        table.Columns.Add("IsAutoIncrement", typeof(Boolean));
        table.Columns.Add("BaseCatalogName", typeof(String));
        table.Columns.Add("BaseSchemaName", typeof(String));
        table.Columns.Add("BaseTableName", typeof(String));
        table.Columns.Add("BaseColumnName", typeof(String));
        table.Columns.Add("IsAliased", typeof(Boolean));
        table.Columns.Add("IsExpression", typeof(Boolean));

        for (var i = 0; i < columns.Count; i++)
        {
            var column = columns[i];
            GetSize(column.Type, out var size, out var precision, out var scale);

            var row = table.NewRow();
            row["ColumnName"] = column.Name;
            row["ColumnOrdinal"] = i;
            row["ColumnSize"] = size;
            row["NumericPrecision"] = precision;
            row["NumericScale"] = scale;
            row["DataType"] = column.FieldType;
            row["DataTypeName"] = column.Type;
            row["ProviderType"] = (Int32)PrestoTypeMapper.GetDbType(column.Type);
            row["IsLong"] = false;
            row["AllowDBNull"] = true;
            row["IsReadOnly"] = false;
            row["IsRowVersion"] = false;
            row["IsUnique"] = false;
            row["IsKey"] = false;
            row["IsAutoIncrement"] = false;
            row["BaseCatalogName"] = DBNull.Value;
            row["BaseSchemaName"] = DBNull.Value;
            row["BaseTableName"] = DBNull.Value;
            row["BaseColumnName"] = column.Name;
            row["IsAliased"] = false;
            row["IsExpression"] = false;
            table.Rows.Add(row);
        }

        return table;
    }

    private static void GetSize(String type, out Int32 size, out Int16 precision, out Int16 scale)
    {
        size = -1;
        precision = 0;
        scale = 0;

        var baseType = PrestoTypeMapper.GetBaseType(type);
        if (baseType is not ("decimal" or "varchar" or "char")) return;

        if (!TryParseArguments(type, out var first, out var second)) return;

        if (baseType == "decimal")
        {
            precision = (Int16)Math.Min(first, Int16.MaxValue);
            scale = (Int16)Math.Min(second, Int16.MaxValue);
        }
        else
            size = first;
    }

    private static Boolean TryParseArguments(String type, out Int32 first, out Int32 second)
    {
        first = 0;
        second = 0;

        var start = type.IndexOf('(');
        if (start < 0) return false;

        var end = type.IndexOf(')', start + 1);
        if (end < 0) return false;

        var args = type[(start + 1)..end].Split(',');
        if (!Int32.TryParse(args[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out first)) return false;

        if (args.Length > 1)
            Int32.TryParse(args[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out second);

        return true;
    }
}
