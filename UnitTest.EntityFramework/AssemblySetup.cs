using System.Data.Entity;
using System.Runtime.CompilerServices;

namespace UnitTest.EntityFramework;

/// <summary>测试程序集初始化。注册 Presto 提供程序并关闭数据库初始化器</summary>
internal static class AssemblySetup
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        // 关闭驱动默认的 TextFileLog，避免其后台定时器阻止 net472 测试宿主退出
        NewLife.Log.XTrace.Log = null;

        DbConfiguration.SetConfiguration(new TestConfiguration());
        Database.SetInitializer<NationContext>(null);
    }
}
