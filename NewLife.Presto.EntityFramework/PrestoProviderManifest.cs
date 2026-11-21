using System.Data.Entity.Core.Common;
using System.Data.Entity.Core.Metadata.Edm;
using System.Xml;

namespace NewLife.Presto.EntityFramework;

/// <summary>Presto 提供程序清单。声明类型系统与只读能力的边界</summary>
internal sealed class PrestoProviderManifest : DbXmlEnabledProviderManifest
{
    /// <summary>清单标记</summary>
    public String Token { get; }

    /// <summary>实例化提供程序清单</summary>
    /// <param name="manifestToken">清单标记</param>
    public PrestoProviderManifest(String manifestToken) : base(GetManifestXml()) => Token = manifestToken ?? "1.0";

    /// <summary>提供程序命名空间</summary>
    public override String NamespaceName => "NewLife.Presto";

    /// <summary>获取数据库信息</summary>
    /// <param name="informationType">信息类型</param>
    /// <returns>XML 读取器</returns>
    protected override XmlReader GetDbInformation(String informationType)
    {
        var xml = informationType switch
        {
            DbProviderManifest.StoreSchemaDefinition or DbProviderManifest.StoreSchemaDefinitionVersion3 => GetStoreSchemaDefinitionXml(),
            DbProviderManifest.StoreSchemaMapping or DbProviderManifest.StoreSchemaMappingVersion3 => GetStoreSchemaMappingXml(),
            _ => $"<?xml version=\"1.0\" encoding=\"utf-8\"?><root>{informationType}</root>",
        };

        return XmlReader.Create(new StringReader(xml));
    }

    #region 类型映射
    /// <summary>存储类型转模型类型</summary>
    /// <param name="storeType">存储类型</param>
    /// <returns>模型类型</returns>
    public override TypeUsage GetEdmType(TypeUsage storeType)
    {
        if (storeType == null) throw new ArgumentNullException(nameof(storeType));

        var name = storeType.EdmType.Name.ToUpperInvariant();
        return name switch
        {
            "BOOLEAN" => TypeUsage.CreateDefaultTypeUsage(PrimitiveType.GetEdmPrimitiveType(PrimitiveTypeKind.Boolean)),
            "TINYINT" => TypeUsage.CreateDefaultTypeUsage(PrimitiveType.GetEdmPrimitiveType(PrimitiveTypeKind.SByte)),
            "SMALLINT" => TypeUsage.CreateDefaultTypeUsage(PrimitiveType.GetEdmPrimitiveType(PrimitiveTypeKind.Int16)),
            "INTEGER" or "INT" => TypeUsage.CreateDefaultTypeUsage(PrimitiveType.GetEdmPrimitiveType(PrimitiveTypeKind.Int32)),
            "BIGINT" => TypeUsage.CreateDefaultTypeUsage(PrimitiveType.GetEdmPrimitiveType(PrimitiveTypeKind.Int64)),
            "REAL" => TypeUsage.CreateDefaultTypeUsage(PrimitiveType.GetEdmPrimitiveType(PrimitiveTypeKind.Single)),
            "DOUBLE" => TypeUsage.CreateDefaultTypeUsage(PrimitiveType.GetEdmPrimitiveType(PrimitiveTypeKind.Double)),
            "DECIMAL" or "NUMERIC" => GetDecimalEdmType(storeType),
            "DATE" or "TIMESTAMP" or "DATETIME" => TypeUsage.CreateDefaultTypeUsage(PrimitiveType.GetEdmPrimitiveType(PrimitiveTypeKind.DateTime)),
            "TIME" => TypeUsage.CreateDefaultTypeUsage(PrimitiveType.GetEdmPrimitiveType(PrimitiveTypeKind.Time)),
            "UUID" or "CHAR" => TypeUsage.CreateDefaultTypeUsage(PrimitiveType.GetEdmPrimitiveType(PrimitiveTypeKind.Guid)),
            "VARBINARY" or "BINARY" => GetBinaryEdmType(storeType),
            _ => GetStringEdmType(storeType),
        };
    }

    /// <summary>模型类型转存储类型</summary>
    /// <param name="edmType">模型类型</param>
    /// <returns>存储类型</returns>
    public override TypeUsage GetStoreType(TypeUsage edmType)
    {
        if (edmType == null) throw new ArgumentNullException(nameof(edmType));
        if (edmType.EdmType is not PrimitiveType primitive) throw new NotSupportedException(Localization.Translate("Unsupported EDM type: {0}.", edmType.EdmType.Name));

        return primitive.PrimitiveTypeKind switch
        {
            PrimitiveTypeKind.Boolean => TypeUsage.CreateDefaultTypeUsage(StoreTypeNameToStorePrimitiveType["boolean"]),
            PrimitiveTypeKind.Byte or PrimitiveTypeKind.SByte => TypeUsage.CreateDefaultTypeUsage(StoreTypeNameToStorePrimitiveType["tinyint"]),
            PrimitiveTypeKind.Int16 => TypeUsage.CreateDefaultTypeUsage(StoreTypeNameToStorePrimitiveType["smallint"]),
            PrimitiveTypeKind.Int32 => TypeUsage.CreateDefaultTypeUsage(StoreTypeNameToStorePrimitiveType["integer"]),
            PrimitiveTypeKind.Int64 => TypeUsage.CreateDefaultTypeUsage(StoreTypeNameToStorePrimitiveType["bigint"]),
            PrimitiveTypeKind.Single => TypeUsage.CreateDefaultTypeUsage(StoreTypeNameToStorePrimitiveType["real"]),
            PrimitiveTypeKind.Double => TypeUsage.CreateDefaultTypeUsage(StoreTypeNameToStorePrimitiveType["double"]),
            PrimitiveTypeKind.Decimal => GetDecimalStoreType(edmType),
            PrimitiveTypeKind.DateTime or PrimitiveTypeKind.DateTimeOffset => TypeUsage.CreateDefaultTypeUsage(StoreTypeNameToStorePrimitiveType["timestamp"]),
            PrimitiveTypeKind.Time => TypeUsage.CreateDefaultTypeUsage(StoreTypeNameToStorePrimitiveType["time"]),
            PrimitiveTypeKind.Guid => TypeUsage.CreateStringTypeUsage(PrimitiveType.GetEdmPrimitiveType(PrimitiveTypeKind.String), false, false, 36),
            PrimitiveTypeKind.String => GetStringStoreType(edmType),
            PrimitiveTypeKind.Binary => GetBinaryStoreType(edmType),
            _ => throw new NotSupportedException(Localization.Translate("Unsupported EDM type: {0}.", primitive.PrimitiveTypeKind)),
        };
    }

    private TypeUsage GetDecimalEdmType(TypeUsage storeType)
    {
        Byte precision = 38;
        Byte scale = 18;
        if (storeType.Facets.TryGetValue("Precision", false, out var p) && p.Value is Byte pb) precision = pb;
        if (storeType.Facets.TryGetValue("Scale", false, out var s) && s.Value is Byte sb) scale = sb;

        return TypeUsage.CreateDecimalTypeUsage(PrimitiveType.GetEdmPrimitiveType(PrimitiveTypeKind.Decimal), precision, scale);
    }

    private TypeUsage GetDecimalStoreType(TypeUsage edmType)
    {
        var precision = 38;
        var scale = 18;
        if (edmType.Facets.TryGetValue("Precision", false, out var p) && p.Value is Byte pb) precision = pb;
        if (edmType.Facets.TryGetValue("Scale", false, out var s) && s.Value is Byte sb) scale = sb;

        if (precision > 38) precision = 38;
        if (scale > precision) scale = precision;

        return TypeUsage.CreateDecimalTypeUsage(StoreTypeNameToStorePrimitiveType["decimal"], (Byte)precision, (Byte)scale);
    }

    private static TypeUsage GetStringEdmType(TypeUsage storeType)
    {
        var maxLength = 65535;
        if (storeType.Facets.TryGetValue("MaxLength", false, out var facet) && facet.Value is Int32 len) maxLength = len;

        return TypeUsage.CreateStringTypeUsage(PrimitiveType.GetEdmPrimitiveType(PrimitiveTypeKind.String), false, false, maxLength);
    }

    private TypeUsage GetStringStoreType(TypeUsage edmType)
    {
        var maxLength = 65535;
        if (edmType.Facets.TryGetValue("MaxLength", false, out var facet) && facet.Value is Int32 len) maxLength = len;

        return TypeUsage.CreateStringTypeUsage(StoreTypeNameToStorePrimitiveType["varchar"], false, false, maxLength);
    }

    private static TypeUsage GetBinaryEdmType(TypeUsage storeType)
    {
        var maxLength = 65535;
        if (storeType.Facets.TryGetValue("MaxLength", false, out var facet) && facet.Value is Int32 len) maxLength = len;

        return TypeUsage.CreateBinaryTypeUsage(PrimitiveType.GetEdmPrimitiveType(PrimitiveTypeKind.Binary), false, maxLength);
    }

    private TypeUsage GetBinaryStoreType(TypeUsage edmType)
    {
        var maxLength = 65535;
        if (edmType.Facets.TryGetValue("MaxLength", false, out var facet) && facet.Value is Int32 len) maxLength = len;

        return TypeUsage.CreateBinaryTypeUsage(StoreTypeNameToStorePrimitiveType["varbinary"], false, maxLength);
    }
    #endregion

    #region 清单 XML
    private static XmlReader GetManifestXml() => XmlReader.Create(new StringReader(ProviderManifestXml));

    private const String ProviderManifestXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<ProviderManifest Namespace=""NewLife.Presto"" xmlns=""http://schemas.microsoft.com/ado/2006/04/edm/providermanifest"">
  <Types>
    <Type Name=""boolean"" PrimitiveTypeKind=""Boolean"" />
    <Type Name=""tinyint"" PrimitiveTypeKind=""SByte"">
      <FacetDescriptions />
    </Type>
    <Type Name=""smallint"" PrimitiveTypeKind=""Int16"">
      <FacetDescriptions />
    </Type>
    <Type Name=""integer"" PrimitiveTypeKind=""Int32"">
      <FacetDescriptions />
    </Type>
    <Type Name=""bigint"" PrimitiveTypeKind=""Int64"">
      <FacetDescriptions />
    </Type>
    <Type Name=""real"" PrimitiveTypeKind=""Single"">
      <FacetDescriptions>
        <Precision Minimum=""0"" Maximum=""255"" DefaultValue=""0"" />
      </FacetDescriptions>
    </Type>
    <Type Name=""double"" PrimitiveTypeKind=""Double"">
      <FacetDescriptions>
        <Precision Minimum=""0"" Maximum=""255"" DefaultValue=""0"" />
      </FacetDescriptions>
    </Type>
    <Type Name=""decimal"" PrimitiveTypeKind=""Decimal"">
      <FacetDescriptions>
        <Precision Minimum=""1"" Maximum=""38"" DefaultValue=""38"" />
        <Scale Minimum=""0"" Maximum=""18"" DefaultValue=""18"" />
      </FacetDescriptions>
    </Type>
    <Type Name=""varchar"" PrimitiveTypeKind=""String"">
      <FacetDescriptions>
        <MaxLength Minimum=""1"" Maximum=""2147483647"" DefaultValue=""65535"" />
        <Unicode DefaultValue=""false"" />
        <FixedLength DefaultValue=""false"" />
      </FacetDescriptions>
    </Type>
    <Type Name=""varbinary"" PrimitiveTypeKind=""Binary"">
      <FacetDescriptions>
        <MaxLength Minimum=""1"" Maximum=""2147483647"" DefaultValue=""65535"" />
        <FixedLength DefaultValue=""false"" />
      </FacetDescriptions>
    </Type>
    <Type Name=""date"" PrimitiveTypeKind=""DateTime"">
      <FacetDescriptions>
        <Precision Constant=""true"" DefaultValue=""0"" />
      </FacetDescriptions>
    </Type>
    <Type Name=""time"" PrimitiveTypeKind=""Time"">
      <FacetDescriptions>
        <Precision Minimum=""0"" Maximum=""12"" DefaultValue=""3"" />
      </FacetDescriptions>
    </Type>
    <Type Name=""timestamp"" PrimitiveTypeKind=""DateTime"">
      <FacetDescriptions>
        <Precision Minimum=""0"" Maximum=""12"" DefaultValue=""3"" />
      </FacetDescriptions>
    </Type>
  </Types>
</ProviderManifest>";

    private static String GetStoreSchemaDefinitionXml() => @"<?xml version=""1.0"" encoding=""utf-8""?>
<Schema Namespace=""NewLife.Presto"" Provider=""NewLife.Presto.PrestoClient"" ProviderManifestToken=""1.0""
        Alias=""Self"" xmlns=""http://schemas.microsoft.com/ado/2006/04/edm/ssdl"">
  <EntityContainer Name=""Schema"" />
</Schema>";

    private static String GetStoreSchemaMappingXml() => @"<?xml version=""1.0"" encoding=""utf-8""?>
<Mapping Space=""C-S"" xmlns=""urn:schemas-microsoft-com:windows:storage:mapping:CS"">
  <EntityContainerMapping StorageEntityContainer=""Schema"" CdmEntityContainer=""Schema"" />
</Mapping>";
    #endregion
}
