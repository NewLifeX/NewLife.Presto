namespace NewLife.Presto;

/// <summary>查询结果列</summary>
public sealed class PrestoColumn
{
    /// <summary>列名</summary>
    public String Name { get; init; } = "";

    /// <summary>Presto 原始类型签名，如 decimal(10,2)、timestamp(3) with time zone</summary>
    public String Type { get; init; } = "";

    /// <summary>对应的 .NET 类型</summary>
    [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicFields | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties)]
    public Type FieldType { get; init; } = typeof(String);

    /// <summary>是否复杂类型</summary>
    public Boolean IsComplex => PrestoTypeMapper.IsComplex(Type);
}
