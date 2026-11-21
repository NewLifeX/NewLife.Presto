using System.Data.Common;

namespace PerfTest.Runners;

/// <summary>Trino.Ado（官方客户端的持续维护分支）</summary>
/// <param name="connectionString">连接串</param>
public sealed class TrinoAdoRunner(String connectionString) : DbRunnerBase
{
    /// <summary>显示名称</summary>
    public override String Name => "Trino.Ado 1.1.0";

    /// <summary>目标端点描述</summary>
    public override String Target => connectionString;

    /// <summary>创建连接</summary>
    protected override DbConnection CreateConnection() => new Trino.Ado.Server.TrinoConnection(connectionString);
}

/// <summary>Trino.Client.ADO（官方旧版 ADO 封装）</summary>
public sealed class TrinoClientAdoRunner : DbRunnerBase
{
    private readonly Trino.Data.ADO.Server.TrinoConnectionProperties _properties;

    /// <summary>以端点信息构造属性对象</summary>
    /// <param name="host">主机</param>
    /// <param name="port">端口</param>
    /// <param name="catalog">Catalog</param>
    /// <param name="schema">Schema</param>
    /// <param name="user">用户名</param>
    /// <param name="isPresto">是否按 PrestoDB 协议头访问</param>
    public TrinoClientAdoRunner(String host, Int32 port, String catalog, String schema, String user, Boolean isPresto)
    {
        _properties = new Trino.Data.ADO.Server.TrinoConnectionProperties
        {
            Host = host,
            Port = port,
            Catalog = catalog,
            Schema = schema,
            EnableSsl = false,
            ServerType = isPresto ? "Presto" : "Trino",
            Source = "NewLife.PerfTest",
        };
        _properties.User = user;
    }

    /// <summary>显示名称</summary>
    public override String Name => "Trino.Client.ADO 1.0.11";

    /// <summary>目标端点描述</summary>
    public override String Target => $"{_properties.Host}:{_properties.Port}";

    /// <summary>创建连接</summary>
    protected override DbConnection CreateConnection() => new Trino.Data.ADO.Server.TrinoConnection(_properties);
}

/// <summary>NReco.PrestoAdo（内部基于 PrestoClient）</summary>
/// <param name="connectionString">连接串</param>
public sealed class NRecoRunner(String connectionString) : DbRunnerBase
{
    /// <summary>显示名称</summary>
    public override String Name => "NReco.PrestoAdo 1.1.0";

    /// <summary>目标端点描述</summary>
    public override String Target => connectionString;

    /// <summary>创建连接</summary>
    protected override DbConnection CreateConnection() => new NReco.PrestoAdo.PrestoConnection(connectionString);
}
