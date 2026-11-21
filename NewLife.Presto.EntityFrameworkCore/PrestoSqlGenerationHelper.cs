using System.Text;
using Microsoft.EntityFrameworkCore.Storage;

namespace NewLife.Presto.EntityFrameworkCore;

/// <summary>Presto SQL 生成辅助器。标识符使用双引号，schema 为空时只输出表名</summary>
public class PrestoSqlGenerationHelper : RelationalSqlGenerationHelper
{
    /// <summary>实例化</summary>
    /// <param name="dependencies">SQL 生成辅助器依赖</param>
    public PrestoSqlGenerationHelper(RelationalSqlGenerationHelperDependencies dependencies) : base(dependencies) { }

    /// <summary>语句终结符。Presto 一次只提交一条语句，不需要分号</summary>
    public override String StatementTerminator => String.Empty;

    /// <summary>批量语句终结符</summary>
    public override String BatchTerminator => String.Empty;

    /// <summary>生成转义标识符。Presto 使用双引号</summary>
    /// <param name="identifier">标识符名称</param>
    /// <returns>转义后的标识符</returns>
    public override String DelimitIdentifier(String identifier) => "\"" + EscapeIdentifier(identifier) + "\"";

    /// <summary>生成转义标识符并写入 StringBuilder</summary>
    /// <param name="builder">字符串构建器</param>
    /// <param name="identifier">标识符名称</param>
    public override void DelimitIdentifier(StringBuilder builder, String identifier)
    {
        builder.Append('"');
        EscapeIdentifier(builder, identifier);
        builder.Append('"');
    }

    /// <summary>生成带 Schema 的转义标识符。schema 允许写成 catalog.schema</summary>
    /// <param name="name">标识符名称</param>
    /// <param name="schema">Schema 或 catalog.schema</param>
    /// <returns>转义后的标识符</returns>
    public override String DelimitIdentifier(String name, String? schema)
        => schema.IsNullOrEmpty() ? DelimitIdentifier(name) : DelimitIdentifier(schema) + "." + DelimitIdentifier(name);

    /// <summary>生成带 Schema 的转义标识符并写入 StringBuilder</summary>
    /// <param name="builder">字符串构建器</param>
    /// <param name="name">标识符名称</param>
    /// <param name="schema">Schema 或 catalog.schema</param>
    public override void DelimitIdentifier(StringBuilder builder, String name, String? schema)
    {
        if (schema.IsNullOrEmpty())
        {
            DelimitIdentifier(builder, name);
            return;
        }

        // catalog.schema 拆成两段分别引用
        var index = schema.IndexOf('.');
        if (index > 0)
        {
            DelimitIdentifier(builder, schema[..index]);
            builder.Append('.');
            DelimitIdentifier(builder, schema[(index + 1)..]);
        }
        else
            DelimitIdentifier(builder, schema);

        builder.Append('.');
        DelimitIdentifier(builder, name);
    }

    /// <summary>转义标识符中的双引号</summary>
    /// <param name="identifier">标识符</param>
    /// <returns>转义后的标识符</returns>
    public override String EscapeIdentifier(String identifier) => identifier.Replace("\"", "\"\"");

    /// <summary>转义标识符中的双引号并写入 StringBuilder</summary>
    /// <param name="builder">字符串构建器</param>
    /// <param name="identifier">标识符</param>
    public override void EscapeIdentifier(StringBuilder builder, String identifier) => builder.Append(identifier.Replace("\"", "\"\""));
}
