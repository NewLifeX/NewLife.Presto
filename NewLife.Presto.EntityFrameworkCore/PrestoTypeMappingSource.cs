using System.Data;
using Microsoft.EntityFrameworkCore.Storage;

namespace NewLife.Presto.EntityFrameworkCore;

/// <summary>Presto 类型映射源。提供 .NET 类型与 Presto/Trino 数据库类型之间的映射</summary>
public class PrestoTypeMappingSource : RelationalTypeMappingSource
{
    private static readonly RelationalTypeMapping _boolean = new BoolTypeMapping("boolean", DbType.Boolean);
    private static readonly RelationalTypeMapping _tinyint = new SByteTypeMapping("tinyint", DbType.SByte);
    private static readonly RelationalTypeMapping _smallint = new ShortTypeMapping("smallint", DbType.Int16);
    private static readonly RelationalTypeMapping _integer = new IntTypeMapping("integer", DbType.Int32);
    private static readonly RelationalTypeMapping _bigint = new LongTypeMapping("bigint", DbType.Int64);
    private static readonly RelationalTypeMapping _real = new FloatTypeMapping("real", DbType.Single);
    private static readonly RelationalTypeMapping _double = new DoubleTypeMapping("double", DbType.Double);
    private static readonly RelationalTypeMapping _decimal = new DecimalTypeMapping("decimal(38, 18)", DbType.Decimal);
    private static readonly RelationalTypeMapping _varchar = new StringTypeMapping("varchar", DbType.String);
    private static readonly RelationalTypeMapping _varbinary = new ByteArrayTypeMapping("varbinary", DbType.Binary);
    private static readonly RelationalTypeMapping _date = new DateOnlyTypeMapping("date", DbType.Date);
    private static readonly RelationalTypeMapping _time = new TimeOnlyTypeMapping("time(3)", DbType.Time);
    private static readonly RelationalTypeMapping _timestamp = new DateTimeTypeMapping("timestamp(3)", DbType.DateTime);
    private static readonly RelationalTypeMapping _timestampOffset = new DateTimeOffsetTypeMapping("timestamp(3) with time zone", DbType.DateTimeOffset);
    private static readonly RelationalTypeMapping _timespan = new TimeSpanTypeMapping("time(3)", DbType.Time);
    private static readonly RelationalTypeMapping _guid = new GuidTypeMapping("uuid", DbType.Guid);

    /// <summary>实例化</summary>
    /// <param name="dependencies">类型映射源依赖</param>
    /// <param name="relationalDependencies">关系型类型映射源依赖</param>
    public PrestoTypeMappingSource(TypeMappingSourceDependencies dependencies, RelationalTypeMappingSourceDependencies relationalDependencies)
        : base(dependencies, relationalDependencies) { }

    /// <summary>查找 CLR 类型或存储类型对应的映射</summary>
    /// <param name="mappingInfo">映射信息</param>
    /// <returns>类型映射</returns>
    protected override RelationalTypeMapping? FindMapping(in RelationalTypeMappingInfo mappingInfo)
    {
        var clrType = mappingInfo.ClrType;
        if (clrType != null)
        {
            if (clrType == typeof(Boolean)) return _boolean;
            if (clrType == typeof(SByte) || clrType == typeof(Byte)) return _tinyint;
            if (clrType == typeof(Int16)) return _smallint;
            if (clrType == typeof(Int32)) return _integer;
            if (clrType == typeof(Int64)) return _bigint;
            if (clrType == typeof(Single)) return _real;
            if (clrType == typeof(Double)) return _double;
            if (clrType == typeof(Decimal))
            {
                var precision = mappingInfo.Precision;
                if (precision.HasValue) return new DecimalTypeMapping($"decimal({precision.Value}, {mappingInfo.Scale ?? 0})", DbType.Decimal);

                return _decimal;
            }
            if (clrType == typeof(DateTime)) return _timestamp;
            if (clrType == typeof(DateTimeOffset)) return _timestampOffset;
            if (clrType == typeof(TimeSpan)) return _timespan;
#if NET6_0_OR_GREATER
            if (clrType == typeof(DateOnly)) return _date;
            if (clrType == typeof(TimeOnly)) return _time;
#endif
            if (clrType == typeof(Guid)) return _guid;
            if (clrType == typeof(Byte[])) return _varbinary;
            if (clrType == typeof(String))
            {
                var size = mappingInfo.Size;
                return size.HasValue && size.Value > 0
                    ? new StringTypeMapping($"varchar({size.Value})", DbType.String, size: size)
                    : _varchar;
            }
        }

        var storeType = mappingInfo.StoreTypeName;
        if (storeType != null)
        {
            var baseName = GetBaseStoreType(storeType);
            switch (baseName)
            {
                case "BOOLEAN": return _boolean;
                case "TINYINT": return _tinyint;
                case "SMALLINT": return _smallint;
                case "INTEGER": return _integer;
                case "BIGINT": return _bigint;
                case "REAL": return _real;
                case "DOUBLE": return _double;
                case "DECIMAL":
                    {
                        var precision = mappingInfo.Precision;
                        return precision.HasValue
                            ? new DecimalTypeMapping($"decimal({precision.Value}, {mappingInfo.Scale ?? 0})", DbType.Decimal)
                            : _decimal;
                    }
                case "DATE": return _date;
                case "TIME": return _time;
                case "TIMESTAMP":
                    return storeType.Contains("with time zone", StringComparison.OrdinalIgnoreCase) ? _timestampOffset : _timestamp;
                case "UUID": return _guid;
                case "VARBINARY": return _varbinary;
                case "VARCHAR":
                case "CHAR":
                case "JSON":
                case "ARRAY":
                case "MAP":
                case "ROW":
                    return new StringTypeMapping(storeType, DbType.String);
            }
        }

        return base.FindMapping(mappingInfo);
    }

    /// <summary>提取存储类型的基础名称，去掉长度/精度括号与空白</summary>
    private static String GetBaseStoreType(String storeType)
    {
        var text = storeType.Trim();
        var index = text.IndexOf('(');
        if (index > 0) text = text[..index];

        text = text.Trim();
        if (text.StartsWith("array", StringComparison.OrdinalIgnoreCase)) return "ARRAY";
        if (text.StartsWith("map", StringComparison.OrdinalIgnoreCase)) return "MAP";
        if (text.StartsWith("row", StringComparison.OrdinalIgnoreCase)) return "ROW";

        return text.ToUpperInvariant();
    }
}
