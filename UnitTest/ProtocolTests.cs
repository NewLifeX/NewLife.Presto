using System.Net;
using NewLife;
using NewLife.Presto;

namespace UnitTest;

/// <summary>协议头与类型映射测试</summary>
public class ProtocolTests
{
    /// <summary>协议头前缀</summary>
    [Fact(DisplayName = "协议头前缀")]
    public void HeaderPrefix()
    {
        Assert.Equal("X-Presto-User", PrestoHeaders.User(PrestoProtocol.Presto));
        Assert.Equal("X-Trino-User", PrestoHeaders.User(PrestoProtocol.Trino));
        Assert.Equal("X-Presto-Time-Zone", PrestoHeaders.TimeZone(PrestoProtocol.Presto));
        Assert.Equal("X-Trino-Set-Catalog", PrestoHeaders.SetCatalog(PrestoProtocol.Trino));
    }

    /// <summary>受管请求头判定</summary>
    [Theory(DisplayName = "受管请求头判定")]
    [InlineData("Authorization", true)]
    [InlineData("Content-Type", true)]
    [InlineData("X-Presto-User", true)]
    [InlineData("X-Trino-Session", true)]
    [InlineData("X-Tenant", false)]
    [InlineData("", false)]
    public void ManagedHeaders(String name, Boolean expected) => Assert.Equal(expected, PrestoHeaders.IsManaged(name));

    /// <summary>基础类型名解析</summary>
    [Theory(DisplayName = "基础类型名解析")]
    [InlineData("decimal(10,2)", "decimal")]
    [InlineData("varchar(255)", "varchar")]
    [InlineData("timestamp(3) with time zone", "timestamp")]
    [InlineData("array(varchar)", "array")]
    [InlineData("BIGINT", "bigint")]
    public void BaseType(String type, String expected) => Assert.Equal(expected, PrestoTypeMapper.GetBaseType(type));

    /// <summary>.NET 类型映射</summary>
    [Theory(DisplayName = ".NET 类型映射")]
    [InlineData("boolean", typeof(Boolean))]
    [InlineData("tinyint", typeof(SByte))]
    [InlineData("smallint", typeof(Int16))]
    [InlineData("integer", typeof(Int32))]
    [InlineData("bigint", typeof(Int64))]
    [InlineData("real", typeof(Single))]
    [InlineData("double", typeof(Double))]
    [InlineData("decimal(18,4)", typeof(Decimal))]
    [InlineData("varchar(255)", typeof(String))]
    [InlineData("varbinary", typeof(Byte[]))]
    [InlineData("date", typeof(DateTime))]
    [InlineData("time", typeof(TimeSpan))]
    [InlineData("timestamp(3)", typeof(DateTime))]
    [InlineData("timestamp(3) with time zone", typeof(DateTimeOffset))]
    [InlineData("time with time zone", typeof(String))]
    [InlineData("interval day to second", typeof(TimeSpan))]
    [InlineData("interval year to month", typeof(String))]
    [InlineData("uuid", typeof(Guid))]
    [InlineData("ipaddress", typeof(IPAddress))]
    [InlineData("array(varchar)", typeof(String))]
    [InlineData("map(varchar, bigint)", typeof(String))]
    [InlineData("row(name varchar)", typeof(String))]
    [InlineData("unknown_type", typeof(String))]
    public void FieldType(String type, Type expected) => Assert.Equal(expected, PrestoTypeMapper.GetFieldType(type));

    /// <summary>复杂类型判定</summary>
    [Theory(DisplayName = "复杂类型判定")]
    [InlineData("array(varchar)", true)]
    [InlineData("map(varchar, bigint)", true)]
    [InlineData("row(name varchar)", true)]
    [InlineData("varchar(255)", false)]
    public void ComplexType(String type, Boolean expected) => Assert.Equal(expected, PrestoTypeMapper.IsComplex(type));

    /// <summary>类型清单覆盖常用类型</summary>
    [Fact(DisplayName = "类型清单覆盖常用类型")]
    public void DataTypesCoverage()
    {
        var names = PrestoTypeMapper.DataTypes.Select(e => e.TypeName).ToArray();

        Assert.Contains("boolean", names);
        Assert.Contains("timestamp", names);
        Assert.Contains("array", names);
        Assert.Equal(names.Length, names.Distinct().Count());
        Assert.All(PrestoTypeMapper.DataTypes, e => Assert.False(e.TypeName.IsNullOrEmpty()));
    }

    /// <summary>全部请求头名称</summary>
    [Theory(DisplayName = "全部请求头名称")]
    [InlineData(PrestoProtocol.Presto, "X-Presto-")]
    [InlineData(PrestoProtocol.Trino, "X-Trino-")]
    public void AllHeaderNames(PrestoProtocol protocol, String prefix)
    {
        Assert.Equal(prefix + "User", PrestoHeaders.User(protocol));
        Assert.Equal(prefix + "Catalog", PrestoHeaders.Catalog(protocol));
        Assert.Equal(prefix + "Schema", PrestoHeaders.Schema(protocol));
        Assert.Equal(prefix + "Session", PrestoHeaders.Session(protocol));
        Assert.Equal(prefix + "Source", PrestoHeaders.Source(protocol));
        Assert.Equal(prefix + "Time-Zone", PrestoHeaders.TimeZone(protocol));
        Assert.Equal(prefix + "Language", PrestoHeaders.Language(protocol));
        Assert.Equal(prefix + "Client-Tags", PrestoHeaders.ClientTags(protocol));
        Assert.Equal(prefix + "Client-Info", PrestoHeaders.ClientInfo(protocol));
        Assert.Equal(prefix + "Resource-Group", PrestoHeaders.ResourceGroup(protocol));
        Assert.Equal(prefix + "Trace-Token", PrestoHeaders.TraceToken(protocol));
        Assert.Equal(prefix + "Role", PrestoHeaders.Role(protocol));
        Assert.Equal(prefix + "Transaction", PrestoHeaders.Transaction(protocol));
        Assert.Equal(prefix + "Set-Catalog", PrestoHeaders.SetCatalog(protocol));
        Assert.Equal(prefix + "Set-Schema", PrestoHeaders.SetSchema(protocol));
        Assert.Equal(prefix + "Set-Session", PrestoHeaders.SetSession(protocol));
        Assert.Equal(prefix + "Clear-Session", PrestoHeaders.ClearSession(protocol));
        Assert.Equal(prefix + "Set-Role", PrestoHeaders.SetRole(protocol));
    }

    /// <summary>空类型与空白类型</summary>
    [Theory(DisplayName = "空类型与空白类型")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyTypeFallsBackToString(String? type)
    {
        Assert.Equal(String.Empty, PrestoTypeMapper.GetBaseType(type));
        Assert.Equal(typeof(String), PrestoTypeMapper.GetFieldType(type));
        Assert.False(PrestoTypeMapper.IsComplex(type));
    }

    /// <summary>大小写与空白不影响类型映射</summary>
    [Theory(DisplayName = "大小写与空白不影响类型映射")]
    [InlineData(" BIGINT ", typeof(Int64))]
    [InlineData("Decimal(18,4)", typeof(Decimal))]
    [InlineData("Timestamp(9) With Time Zone", typeof(DateTimeOffset))]
    public void TypeMappingIsCaseInsensitive(String type, Type expected) => Assert.Equal(expected, PrestoTypeMapper.GetFieldType(type));

    /// <summary>更多标量与特殊类型</summary>
    [Theory(DisplayName = "更多标量与特殊类型")]
    [InlineData("varchar", typeof(String))]
    [InlineData("char(1)", typeof(String))]
    [InlineData("json", typeof(String))]
    [InlineData("varbinary", typeof(Byte[]))]
    [InlineData("geometry", typeof(Byte[]))]
    [InlineData("sphericalgeography", typeof(Byte[]))]
    [InlineData("hyperloglog", typeof(String))]
    [InlineData("qdigest", typeof(String))]
    [InlineData("tdigest", typeof(String))]
    public void MoreFieldTypes(String type, Type expected) => Assert.Equal(expected, PrestoTypeMapper.GetFieldType(type));

    /// <summary>数组嵌套仍按字符串降级</summary>
    [Theory(DisplayName = "数组嵌套仍按字符串降级")]
    [InlineData("array(array(varchar))")]
    [InlineData("map(varchar, array(bigint))")]
    [InlineData("row(a array(varchar), b map(varchar, bigint))")]
    public void NestedComplexTypes(String type)
    {
        Assert.Equal(typeof(String), PrestoTypeMapper.GetFieldType(type));
        Assert.True(PrestoTypeMapper.IsComplex(type));
    }

    /// <summary>带精度的时间类型</summary>
    [Theory(DisplayName = "带精度的时间类型")]
    [InlineData("time(3)", typeof(TimeSpan))]
    [InlineData("time(3) with time zone", typeof(String))]
    [InlineData("timestamp(9)", typeof(DateTime))]
    [InlineData("timestamp(9) with time zone", typeof(DateTimeOffset))]
    public void TimestampPrecision(String type, Type expected) => Assert.Equal(expected, PrestoTypeMapper.GetFieldType(type));

    /// <summary>类型映射信息可初始化</summary>
    [Fact(DisplayName = "类型映射信息可初始化")]
    public void DataTypeProperties()
    {
        var info = new PrestoDataType { TypeName = "custom", FieldType = typeof(Int32), IsComplex = true };

        Assert.Equal("custom", info.TypeName);
        Assert.Equal(typeof(Int32), info.FieldType);
        Assert.True(info.IsComplex);
    }
}
