using System.Data;
using System.Data.Common;

namespace NewLife.Presto;

/// <summary>Presto/Trino 数据适配器</summary>
/// <remarks>
/// 只读适配器：通过 <see cref="SelectCommand"/> 支持 <c>Fill</c> 填充 <see cref="DataSet"/>/<see cref="DataTable"/>；
/// Presto/Trino 不支持事务与批量写入，写操作请直接用 <see cref="PrestoCommand"/> 执行。
/// </remarks>
public sealed class PrestoDataAdapter : DbDataAdapter
{
    #region 构造
    /// <summary>实例化数据适配器</summary>
    public PrestoDataAdapter() { }

    /// <summary>实例化数据适配器</summary>
    /// <param name="selectCommand">查询命令</param>
    public PrestoDataAdapter(PrestoCommand selectCommand) => SelectCommand = selectCommand;

    /// <summary>实例化数据适配器</summary>
    /// <param name="commandText">查询语句</param>
    /// <param name="connection">连接</param>
    public PrestoDataAdapter(String commandText, PrestoConnection connection) => SelectCommand = new PrestoCommand(commandText, connection);
    #endregion

    #region 属性
    /// <summary>查询命令</summary>
    public new PrestoCommand? SelectCommand
    {
        get => (PrestoCommand?)base.SelectCommand;
        set => base.SelectCommand = value;
    }
    #endregion
}
