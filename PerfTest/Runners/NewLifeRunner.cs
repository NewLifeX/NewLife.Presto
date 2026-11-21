using System.Data.Common;
using NewLife.Presto;

namespace PerfTest.Runners;

/// <summary>NewLife.Presto 驱动</summary>
/// <param name="connectionString">连接串</param>
public sealed class NewLifeRunner(String connectionString) : DbRunnerBase
{
    /// <summary>显示名称</summary>
    public override String Name => "NewLife.Presto 1.0";

    /// <summary>目标端点描述</summary>
    public override String Target => connectionString;

    /// <summary>创建连接</summary>
    protected override DbConnection CreateConnection() => new PrestoConnection(connectionString);
}
