using System.Data.Common;
using System.Data.Entity.Infrastructure;

namespace NewLife.Presto.EntityFramework;

/// <summary>Presto 连接工厂。供 EF6 按名称或连接字符串创建连接</summary>
public class PrestoConnectionFactory : IDbConnectionFactory
{
    /// <summary>创建连接</summary>
    /// <param name="nameOrConnectionString">连接名或连接字符串</param>
    /// <returns>Presto 连接</returns>
    public DbConnection CreateConnection(String nameOrConnectionString) => new PrestoConnection(nameOrConnectionString);
}
