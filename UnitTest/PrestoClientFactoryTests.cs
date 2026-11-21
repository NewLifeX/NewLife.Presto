using System.Data.Common;
using NewLife.Presto;

namespace UnitTest;

/// <summary>客户端工厂测试</summary>
public class PrestoClientFactoryTests
{
    [Fact(DisplayName = "工厂创建驱动对象")]
    public void CreateObjects()
    {
        var factory = PrestoClientFactory.Instance;

        Assert.IsType<PrestoConnection>(factory.CreateConnection());
        Assert.IsType<PrestoCommand>(factory.CreateCommand());
        Assert.IsType<PrestoParameter>(factory.CreateParameter());
        Assert.IsType<PrestoConnectionStringBuilder>(factory.CreateConnectionStringBuilder());
        Assert.Same(factory, PrestoClientFactory.Instance);
    }

    [Fact(DisplayName = "工厂能力标志")]
    public void Capabilities()
    {
        var factory = PrestoClientFactory.Instance;

        Assert.True(factory.CanCreateDataAdapter);
        Assert.IsType<PrestoDataAdapter>(factory.CreateDataAdapter());
        Assert.False(factory.CanCreateCommandBuilder);
        Assert.False(factory.CanCreateDataSourceEnumerator);
    }

    [Fact(DisplayName = "注册到 DbProviderFactories")]
    public void Registered()
    {
        var factory = DbProviderFactories.GetFactory("NewLife.Presto");

        Assert.Same(PrestoClientFactory.Instance, factory);
    }
}
