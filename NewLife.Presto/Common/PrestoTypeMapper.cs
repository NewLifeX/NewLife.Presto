using System.Net;

namespace NewLife.Presto;

/// <summary>Presto 类型映射信息</summary>
public sealed class PrestoDataType
{
    /// <summary>Presto 类型名</summary>
    public String TypeName { get; init; } = "";

    /// <summary>对应的 .NET 类型</summary>
    public Type FieldType { get; init; } = typeof(Object);

    /// <summary>是否复杂类型。首版按字符串降级</summary>
    public Boolean IsComplex { get; init; }
}

/// <summary>Presto/Trino 类型签名解析与 .NET 类型映射</summary>
/// <remarks>只做类型名与参数解析，不依赖运行时反射，NativeAOT 可用。未知类型统一降级为字符串</remarks>
public static class PrestoTypeMapper
{
    /// <summary>类型映射清单，供 GetSchema 的 DataTypes 集合使用</summary>
    public static IReadOnlyList<PrestoDataType> DataTypes { get; } =
    [
        new() { TypeName = "boolean", FieldType = typeof(Boolean) },
        new() { TypeName = "tinyint", FieldType = typeof(SByte) },
        new() { TypeName = "smallint", FieldType = typeof(Int16) },
        new() { TypeName = "integer", FieldType = typeof(Int32) },
        new() { TypeName = "bigint", FieldType = typeof(Int64) },
        new() { TypeName = "real", FieldType = typeof(Single) },
        new() { TypeName = "double", FieldType = typeof(Double) },
        new() { TypeName = "decimal", FieldType = typeof(Decimal) },
        new() { TypeName = "varchar", FieldType = typeof(String) },
        new() { TypeName = "char", FieldType = typeof(String) },
        new() { TypeName = "varbinary", FieldType = typeof(Byte[]) },
        new() { TypeName = "json", FieldType = typeof(String) },
        new() { TypeName = "date", FieldType = typeof(DateTime) },
        new() { TypeName = "time", FieldType = typeof(TimeSpan) },
        new() { TypeName = "timestamp", FieldType = typeof(DateTime) },
        new() { TypeName = "uuid", FieldType = typeof(Guid) },
        new() { TypeName = "ipaddress", FieldType = typeof(IPAddress) },
        new() { TypeName = "geometry", FieldType = typeof(Byte[]) },
        new() { TypeName = "array", FieldType = typeof(String), IsComplex = true },
        new() { TypeName = "map", FieldType = typeof(String), IsComplex = true },
        new() { TypeName = "row", FieldType = typeof(String), IsComplex = true },
    ];

    /// <summary>获取基础类型名。去掉参数、时区后缀与首尾空白</summary>
    /// <param name="type">类型签名，如 decimal(10,2)、timestamp(3) with time zone</param>
    /// <returns>基础类型名，如 decimal、timestamp</returns>
    public static String GetBaseType(String? type)
    {
        if (type.IsNullOrEmpty()) return String.Empty;

        var text = type.Trim();
        var index = text.IndexOf('(');
        if (index > 0) text = text[..index].Trim();
        if (text.EndsWithIgnoreCase(" with time zone")) text = text[..^15].Trim();

        return text.ToLowerInvariant();
    }

    /// <summary>获取类型对应的 .NET 类型</summary>
    /// <param name="type">类型签名</param>
    /// <returns>.NET 类型；未知类型返回 <see cref="String"/></returns>
    [return: System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicFields | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties)]
    public static Type GetFieldType(String? type)
    {
        if (type.IsNullOrEmpty()) return typeof(String);

        var text = type.Trim().ToLowerInvariant();
        var hasTimeZone = text.Contains(" with time zone");

        // 复杂类型首版按原始字符串返回
        if (text.StartsWith("array") || text.StartsWith("map") || text.StartsWith("row")) return typeof(String);

        if (text.StartsWith("interval")) return text.Contains("day") ? typeof(TimeSpan) : typeof(String);

        if (hasTimeZone)
        {
            var tzBase = GetBaseType(text);
            // 仅 time 的时区语义没有日期，避免伪造 DateTimeOffset
            if (tzBase == "time") return typeof(String);
            if (tzBase == "timestamp") return typeof(DateTimeOffset);
        }

        var baseType = GetBaseType(text);
        return baseType switch
        {
            "boolean" => typeof(Boolean),
            "tinyint" => typeof(SByte),
            "smallint" => typeof(Int16),
            "integer" or "int" => typeof(Int32),
            "bigint" => typeof(Int64),
            "real" => typeof(Single),
            "double" => typeof(Double),
            "decimal" => typeof(Decimal),
            "varchar" or "char" or "json" or "hyperloglog" or "qdigest" or "tdigest" => typeof(String),
            "varbinary" or "geometry" or "sphericalgeography" => typeof(Byte[]),
            "date" or "timestamp" => typeof(DateTime),
            "time" => typeof(TimeSpan),
            "uuid" => typeof(Guid),
            "ipaddress" => typeof(IPAddress),
            _ => typeof(String),
        };
    }

    /// <summary>判断是否为复杂类型</summary>
    /// <param name="type">类型签名</param>
    /// <returns>array/map/row 返回 true</returns>
    public static Boolean IsComplex(String? type)
    {
        var baseType = GetBaseType(type);
        return baseType is "array" or "map" or "row";
    }

    /// <summary>获取类型对应的 <see cref="System.Data.DbType"/></summary>
    /// <param name="type">类型签名</param>
    /// <returns>DbType；未知类型返回 <see cref="System.Data.DbType.String"/></returns>
    public static System.Data.DbType GetDbType(String? type)
    {
        if (type.IsNullOrEmpty()) return System.Data.DbType.String;

        var text = type.Trim().ToLowerInvariant();
        if (text.StartsWith("array") || text.StartsWith("map") || text.StartsWith("row")) return System.Data.DbType.String;

        var hasTimeZone = text.Contains(" with time zone");
        var baseType = GetBaseType(text);
        if (hasTimeZone)
        {
            if (baseType == "timestamp") return System.Data.DbType.DateTimeOffset;
            if (baseType == "time") return System.Data.DbType.String;
        }

        return baseType switch
        {
            "boolean" => System.Data.DbType.Boolean,
            "tinyint" => System.Data.DbType.SByte,
            "smallint" => System.Data.DbType.Int16,
            "integer" or "int" => System.Data.DbType.Int32,
            "bigint" => System.Data.DbType.Int64,
            "real" => System.Data.DbType.Single,
            "double" => System.Data.DbType.Double,
            "decimal" => System.Data.DbType.Decimal,
            "varbinary" or "geometry" or "sphericalgeography" => System.Data.DbType.Binary,
            "date" => System.Data.DbType.Date,
            "time" => System.Data.DbType.Time,
            "timestamp" => System.Data.DbType.DateTime,
            "uuid" => System.Data.DbType.Guid,
            _ => System.Data.DbType.String,
        };
    }
}
