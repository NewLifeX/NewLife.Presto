using System.Data.Common;
using System.Data.Entity.Core.Common;

namespace NewLife.Presto.EntityFramework;

/// <summary>Presto 命令定义。每次执行克隆出新的命令，避免 EF6 复用同一个命令实例</summary>
internal sealed class PrestoCommandDefinition : DbCommandDefinition
{
    private readonly String _commandText;
    private readonly String[] _parameters;

    /// <summary>实例化命令定义</summary>
    /// <param name="commandText">命令文本</param>
    /// <param name="parameters">参数名集合</param>
    public PrestoCommandDefinition(String commandText, IEnumerable<String> parameters) : base(CreatePrototype(commandText, parameters), Clone)
    {
        _commandText = commandText;
        _parameters = parameters.ToArray();
    }

    /// <summary>创建新命令</summary>
    /// <returns>数据库命令</returns>
    public override DbCommand CreateCommand() => CreatePrototype(_commandText, _parameters);

    private static DbCommand Clone(DbCommand prototype) => CreatePrototype(prototype.CommandText, prototype.Parameters.Cast<DbParameter>().Select(p => p.ParameterName));

    private static PrestoCommand CreatePrototype(String commandText, IEnumerable<String> parameters)
    {
        var command = new PrestoCommand { CommandText = commandText };
        foreach (var name in parameters)
        {
            command.Parameters.Add(new PrestoParameter(name, DBNull.Value));
        }

        return command;
    }
}
