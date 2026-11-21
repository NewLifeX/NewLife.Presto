using System.Data;
using System.Data.Common;

namespace NewLife.Presto;

/// <summary>Presto/Trino 命令参数</summary>
/// <remarks>仅支持输入参数，参数名可写成 `@name` 或 `name`；替换时按不区分大小写匹配。</remarks>
public class PrestoParameter : DbParameter
{
    #region 属性
    /// <summary>参数名</summary>
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP
    [System.Diagnostics.CodeAnalysis.AllowNull]
#endif
    public override String ParameterName
    {
        get => _parameterName;
        set => _parameterName = value ?? String.Empty;
    }

    private String _parameterName = String.Empty;

    /// <summary>参数值</summary>
    public override Object? Value { get; set; }

    /// <summary>参数类型</summary>
    public override DbType DbType { get; set; }

    /// <summary>参数方向。仅支持 Input</summary>
    public override ParameterDirection Direction { get; set; } = ParameterDirection.Input;

    /// <summary>是否允许空</summary>
    public override Boolean IsNullable { get; set; }

    /// <summary>大小</summary>
    public override Int32 Size { get; set; }

#if NETSTANDARD2_0_OR_GREATER || NETCOREAPP
    /// <summary>精度</summary>
    public override Byte Precision { get; set; }

    /// <summary>小数位数</summary>
    public override Byte Scale { get; set; }
#endif

    /// <summary>源列</summary>
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP
    [System.Diagnostics.CodeAnalysis.AllowNull]
#endif
    public override String SourceColumn
    {
        get => _sourceColumn;
        set => _sourceColumn = value ?? String.Empty;
    }

    private String _sourceColumn = String.Empty;

    /// <summary>源列空值映射</summary>
    public override Boolean SourceColumnNullMapping { get; set; }

    /// <summary>数据行版本。默认 Current，与 ADO.NET 其它提供程序一致</summary>
    public override DataRowVersion SourceVersion { get; set; } = DataRowVersion.Current;
    #endregion

    #region 构造
    /// <summary>实例化参数</summary>
    public PrestoParameter() { }

    /// <summary>实例化参数</summary>
    /// <param name="name">参数名</param>
    /// <param name="value">参数值</param>
    public PrestoParameter(String name, Object? value)
    {
        ParameterName = name;
        Value = value;
    }
    #endregion

    #region 方法
    /// <summary>重置类型</summary>
    public override void ResetDbType() => DbType = default;
    #endregion
}
