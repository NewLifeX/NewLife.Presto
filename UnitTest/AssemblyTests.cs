using System.ComponentModel;
using System.Reflection;

namespace UnitTest;

/// <summary>仓库骨架自检</summary>
[DisplayName("仓库骨架")]
public class AssemblyTests
{
    /// <summary>核心程序集可加载</summary>
    [Fact(DisplayName = "核心程序集可加载")]
    public void CoreAssemblyLoads()
    {
        var asm = Assembly.Load("NewLife.Presto");

        Assert.Equal("NewLife.Presto", asm.GetName().Name);
    }

    /// <summary>依赖包 NewLife.Core 可加载</summary>
    [Fact(DisplayName = "依赖包 NewLife.Core 可加载")]
    public void NewLifeCoreLoads()
    {
        var asm = Assembly.Load("NewLife.Core");

        Assert.Equal("NewLife.Core", asm.GetName().Name);
    }
}
