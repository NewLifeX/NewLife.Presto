using System.Data.Entity.Core.Common.CommandTrees;

namespace NewLife.Presto.EntityFramework;

/// <summary>SQL 生成结果</summary>
internal sealed class PrestoSqlResult
{
    /// <summary>命令文本</summary>
    public String CommandText { get; }

    /// <summary>参数名集合。值由 EF6 在命令执行前填充</summary>
    public IList<String> Parameters { get; }

    /// <summary>实例化</summary>
    /// <param name="commandText">命令文本</param>
    /// <param name="parameters">参数名集合</param>
    public PrestoSqlResult(String commandText, IList<String> parameters)
    {
        CommandText = commandText;
        Parameters = parameters;
    }
}
