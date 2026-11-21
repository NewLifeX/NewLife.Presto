using System.Data.Entity.Core.Metadata.Edm;
using NewLife.Presto.EntityFramework;

namespace UnitTest.EntityFramework;

/// <summary>提供程序清单与类型映射测试</summary>
public class PrestoProviderManifestTests
{
    private static PrestoProviderManifest CreateManifest() => new("1.0");

    private static TypeUsage StoreType(PrestoProviderManifest manifest, String name)
    {
        var primitive = manifest.GetStoreTypes().First(e => String.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
        return TypeUsage.CreateDefaultTypeUsage(primitive);
    }

    private static TypeUsage EdmType(PrimitiveTypeKind kind) => TypeUsage.CreateDefaultTypeUsage(PrimitiveType.GetEdmPrimitiveType(kind));

    [Fact]
    [DisplayName("清单命名空间为 NewLife.Presto")]
    public void NamespaceName()
    {
        Assert.Equal("NewLife.Presto", CreateManifest().NamespaceName);
    }

    [Fact]
    [DisplayName("清单包含 Presto 常用存储类型")]
    public void StoreTypes()
    {
        var manifest = CreateManifest();
        var names = manifest.GetStoreTypes().Select(e => e.Name).ToList();

        Assert.Contains("boolean", names);
        Assert.Contains("integer", names);
        Assert.Contains("bigint", names);
        Assert.Contains("varchar", names);
        Assert.Contains("timestamp", names);
    }

    [Fact]
    [DisplayName("存储类型映射到模型类型")]
    public void StoreToEdm()
    {
        var manifest = CreateManifest();

        Assert.Equal(PrimitiveTypeKind.Boolean, ((PrimitiveType)manifest.GetEdmType(StoreType(manifest, "boolean")).EdmType).PrimitiveTypeKind);
        Assert.Equal(PrimitiveTypeKind.SByte, ((PrimitiveType)manifest.GetEdmType(StoreType(manifest, "tinyint")).EdmType).PrimitiveTypeKind);
        Assert.Equal(PrimitiveTypeKind.Int32, ((PrimitiveType)manifest.GetEdmType(StoreType(manifest, "integer")).EdmType).PrimitiveTypeKind);
        Assert.Equal(PrimitiveTypeKind.Int64, ((PrimitiveType)manifest.GetEdmType(StoreType(manifest, "bigint")).EdmType).PrimitiveTypeKind);
        Assert.Equal(PrimitiveTypeKind.Single, ((PrimitiveType)manifest.GetEdmType(StoreType(manifest, "real")).EdmType).PrimitiveTypeKind);
        Assert.Equal(PrimitiveTypeKind.Double, ((PrimitiveType)manifest.GetEdmType(StoreType(manifest, "double")).EdmType).PrimitiveTypeKind);
        Assert.Equal(PrimitiveTypeKind.Decimal, ((PrimitiveType)manifest.GetEdmType(StoreType(manifest, "decimal")).EdmType).PrimitiveTypeKind);
        Assert.Equal(PrimitiveTypeKind.String, ((PrimitiveType)manifest.GetEdmType(StoreType(manifest, "varchar")).EdmType).PrimitiveTypeKind);
        Assert.Equal(PrimitiveTypeKind.DateTime, ((PrimitiveType)manifest.GetEdmType(StoreType(manifest, "timestamp")).EdmType).PrimitiveTypeKind);
        Assert.Equal(PrimitiveTypeKind.Time, ((PrimitiveType)manifest.GetEdmType(StoreType(manifest, "time")).EdmType).PrimitiveTypeKind);
    }

    [Fact]
    [DisplayName("模型类型映射到存储类型")]
    public void EdmToStore()
    {
        var manifest = CreateManifest();

        Assert.Equal("boolean", manifest.GetStoreType(EdmType(PrimitiveTypeKind.Boolean)).EdmType.Name);
        Assert.Equal("integer", manifest.GetStoreType(EdmType(PrimitiveTypeKind.Int32)).EdmType.Name);
        Assert.Equal("bigint", manifest.GetStoreType(EdmType(PrimitiveTypeKind.Int64)).EdmType.Name);
        Assert.Equal("varchar", manifest.GetStoreType(EdmType(PrimitiveTypeKind.String)).EdmType.Name);
        Assert.Equal("timestamp", manifest.GetStoreType(EdmType(PrimitiveTypeKind.DateTime)).EdmType.Name);
        Assert.Equal("varbinary", manifest.GetStoreType(EdmType(PrimitiveTypeKind.Binary)).EdmType.Name);
    }
}
